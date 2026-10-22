using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Grid {
    /// <summary>
    /// Compact encoding of a cell set as horizontal runs sorted by (Y, X). Format v1: u8 version,
    /// varuint run count, then per run zigzag dY and zigzag dX0 (both from the previous run, the first
    /// absolute) and varuint (length − 1). Decoding is hardened: hostile bytes return false, never throw.
    /// </summary>
    public static class CellRunCodec {
        /// <summary>Format version written as the first byte.</summary>
        public const byte FormatVersion = 1;

        private const int MaxVarIntBytes = 10;
        private const int MinRunBytes = 3;

        /// <summary>Row-major order: Y ascending, then X ascending. The order every encoder expects.</summary>
        public static int CompareRowMajor(CellCoord left, CellCoord right) {
            int byRow = left.Y.CompareTo(right.Y);
            return byRow != 0 ? byRow : left.X.CompareTo(right.X);
        }

        /// <summary>Encodes any collection of cells; duplicates are dropped and order does not matter.</summary>
        public static byte[] Encode(IEnumerable<CellCoord> cells) {
            if (cells == null) {
                throw new ArgumentNullException(nameof(cells));
            }
            List<CellCoord> sorted = SortDistinct(cells);
            List<CellRun> runs = BuildRuns(sorted, 0, sorted.Count);
            byte[] buffer = new byte[MeasureRuns(runs, runs.Count)];
            WriteRuns(runs, runs.Count, buffer);
            return buffer;
        }

        /// <summary>
        /// Encodes cells already sorted row-major and distinct into <paramref name="destination"/>.
        /// Returns the bytes written, or −1 when the destination is too small.
        /// </summary>
        public static int Encode(IReadOnlyList<CellCoord> sorted, Span<byte> destination) {
            if (sorted == null) {
                throw new ArgumentNullException(nameof(sorted));
            }
            List<CellRun> runs = BuildRuns(sorted, 0, sorted.Count);
            int size = MeasureRuns(runs, runs.Count);
            if (size > destination.Length) {
                return -1;
            }
            WriteRuns(runs, runs.Count, destination);
            return size;
        }

        /// <summary>
        /// Encodes as many whole runs as fit, starting at <paramref name="startIndex"/> of row-major sorted,
        /// distinct cells. Each chunk decodes on its own. <paramref name="consumed"/> is the number of cells
        /// packed. Returns the bytes written, or −1 when not even one run fits.
        /// </summary>
        public static int EncodeChunk(List<CellCoord> sortedCells, int startIndex, Span<byte> destination, out int consumed) {
            return EncodeChunk(sortedCells, startIndex, destination, int.MaxValue, out consumed);
        }

        /// <summary>
        /// As <see cref="EncodeChunk(List{CellCoord}, int, Span{byte}, out int)"/>, also packing at most
        /// <paramref name="maxCells"/> cells. Runs stay whole unless the first run alone is longer than
        /// <paramref name="maxCells"/>, which is split so every call makes progress.
        /// </summary>
        public static int EncodeChunk(List<CellCoord> sortedCells, int startIndex, Span<byte> destination, int maxCells, out int consumed) {
            if (maxCells <= 0) {
                throw new ArgumentOutOfRangeException(nameof(maxCells));
            }
            if (sortedCells == null) {
                throw new ArgumentNullException(nameof(sortedCells));
            }
            if (startIndex < 0 || startIndex > sortedCells.Count) {
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            }
            List<CellRun> runs = BuildRunsThatFit(sortedCells, startIndex, destination.Length, maxCells, out consumed);
            if (runs.Count == 0 && startIndex < sortedCells.Count) {
                return -1;
            }
            int size = MeasureRuns(runs, runs.Count);
            if (size > destination.Length) {
                return -1;
            }
            WriteRuns(runs, runs.Count, destination);
            return size;
        }

        /// <summary>
        /// Decodes <paramref name="data"/>, appending the cells row-major to <paramref name="destination"/>.
        /// Returns false (destination unchanged) on a bad version, truncation, overlapping or unsorted runs,
        /// coordinates outside int range, or more than <paramref name="maxCells"/> cells.
        /// </summary>
        public static bool TryDecode(ReadOnlySpan<byte> data, int maxCells, List<CellCoord> destination) {
            if (destination == null) {
                throw new ArgumentNullException(nameof(destination));
            }
            int originalCount = destination.Count;
            if (TryDecodeInto(data, maxCells, destination)) {
                return true;
            }
            destination.RemoveRange(originalCount, destination.Count - originalCount);
            return false;
        }

        private static bool TryDecodeInto(ReadOnlySpan<byte> data, int maxCells, List<CellCoord> destination) {
            if (data.Length < 2 || data[0] != FormatVersion || maxCells < 0) {
                return false;
            }
            int offset = 1;
            if (!TryReadVarUInt(data, ref offset, out ulong runCount)) {
                return false;
            }
            if (runCount > (ulong)maxCells || runCount > (ulong)((data.Length - offset) / MinRunBytes)) {
                return false;
            }
            DecodeCursor cursor = new DecodeCursor { RemainingCells = maxCells };
            for (ulong runIndex = 0; runIndex < runCount; runIndex++) {
                if (!TryReadRun(data, ref offset, ref cursor, runIndex == 0, out CellRun run)) {
                    return false;
                }
                AppendRun(run, destination);
            }
            return offset == data.Length;
        }

        private static bool TryReadRun(ReadOnlySpan<byte> data, ref int offset, ref DecodeCursor cursor, bool isFirst, out CellRun run) {
            run = default;
            if (!TryReadVarUInt(data, ref offset, out ulong rawDeltaY)
                || !TryReadVarUInt(data, ref offset, out ulong rawDeltaX)
                || !TryReadVarUInt(data, ref offset, out ulong lengthMinusOne)) {
                return false;
            }
            long deltaY = ZigZagDecode(rawDeltaY);
            long deltaX = ZigZagDecode(rawDeltaX);
            if (lengthMinusOne >= (ulong)cursor.RemainingCells || IsDeltaOutOfRange(deltaY) || IsDeltaOutOfRange(deltaX)) {
                return false;
            }
            long y = isFirst ? deltaY : cursor.PreviousY + deltaY;
            long x = isFirst ? deltaX : cursor.PreviousX + deltaX;
            long length = (long)lengthMinusOne + 1;
            if (!IsRunInRange(x, y, length) || !IsAfterPrevious(cursor, isFirst, x, y)) {
                return false;
            }
            cursor.PreviousX = x;
            cursor.PreviousY = y;
            cursor.PreviousEndExclusive = x + length;
            cursor.RemainingCells -= (int)length;
            run = new CellRun((int)x, (int)y, (int)length);
            return true;
        }

        private static bool IsDeltaOutOfRange(long delta) {
            return delta < -(long)uint.MaxValue || delta > uint.MaxValue;
        }

        private static bool IsRunInRange(long x, long y, long length) {
            return y >= int.MinValue && y <= int.MaxValue && x >= int.MinValue && x + length - 1 <= int.MaxValue;
        }

        private static bool IsAfterPrevious(DecodeCursor cursor, bool isFirst, long x, long y) {
            if (isFirst || y > cursor.PreviousY) {
                return true;
            }
            return y == cursor.PreviousY && x >= cursor.PreviousEndExclusive;
        }

        private static void AppendRun(CellRun run, List<CellCoord> destination) {
            for (int step = 0; step < run.Length; step++) {
                destination.Add(new CellCoord(run.X + step, run.Y));
            }
        }

        private static List<CellCoord> SortDistinct(IEnumerable<CellCoord> cells) {
            HashSet<CellCoord> distinct = new HashSet<CellCoord>(cells);
            List<CellCoord> sorted = new List<CellCoord>(distinct);
            sorted.Sort(CompareRowMajor);
            return sorted;
        }

        private static List<CellRun> BuildRuns(IReadOnlyList<CellCoord> sorted, int startIndex, int endIndex, int capacity = int.MaxValue, int maxCells = int.MaxValue) {
            // A chunk holds at most capacity / MinRunBytes runs; one extra lets the fit check see the overflow.
            int maxRuns = capacity / MinRunBytes + 1;
            List<CellRun> runs = new List<CellRun>();
            for (int index = startIndex; index < endIndex; index++) {
                if (index - startIndex == maxCells) {
                    break;
                }
                CellCoord cell = sorted[index];
                if (index > startIndex && CompareRowMajor(sorted[index - 1], cell) >= 0) {
                    throw new ArgumentException("Cells must be sorted row-major and distinct.");
                }
                if (ExtendsLastRun(runs, cell)) {
                    CellRun last = runs[runs.Count - 1];
                    runs[runs.Count - 1] = new CellRun(last.X, last.Y, last.Length + 1);
                    continue;
                }
                if (runs.Count == maxRuns) {
                    break;
                }
                runs.Add(new CellRun(cell.X, cell.Y, 1));
            }
            return runs;
        }

        private static bool ExtendsLastRun(List<CellRun> runs, CellCoord cell) {
            if (runs.Count == 0) {
                return false;
            }
            CellRun last = runs[runs.Count - 1];
            return last.Y == cell.Y && (long)last.X + last.Length == cell.X;
        }

        private static List<CellRun> BuildRunsThatFit(List<CellCoord> sorted, int startIndex, int capacity, int maxCells, out int consumed) {
            List<CellRun> runs = BuildRuns(sorted, startIndex, sorted.Count, capacity, maxCells);
            DropRunCutByCellCap(sorted, startIndex, runs);
            int bodySize = 0;
            consumed = 0;
            for (int runIndex = 0; runIndex < runs.Count; runIndex++) {
                bodySize += MeasureRun(runs, runIndex);
                if (1 + VarUIntSize((ulong)(runIndex + 1)) + bodySize > capacity) {
                    runs.RemoveRange(runIndex, runs.Count - runIndex);
                    break;
                }
                consumed += runs[runIndex].Length;
            }
            return runs;
        }

        // The cell cap can end the build mid-run; that run waits for the next chunk unless it is the only one.
        private static void DropRunCutByCellCap(List<CellCoord> sorted, int startIndex, List<CellRun> runs) {
            if (runs.Count < 2) {
                return;
            }
            int built = 0;
            foreach (CellRun run in runs) {
                built += run.Length;
            }
            int next = startIndex + built;
            if (next < sorted.Count && ExtendsLastRun(runs, sorted[next])) {
                runs.RemoveAt(runs.Count - 1);
            }
        }

        private static int MeasureRuns(List<CellRun> runs, int runCount) {
            int size = 1 + VarUIntSize((ulong)runCount);
            for (int runIndex = 0; runIndex < runCount; runIndex++) {
                size += MeasureRun(runs, runIndex);
            }
            return size;
        }

        private static int MeasureRun(List<CellRun> runs, int runIndex) {
            RunDeltas deltas = Deltas(runs, runIndex);
            return VarUIntSize(deltas.DeltaY) + VarUIntSize(deltas.DeltaX) + VarUIntSize(deltas.LengthMinusOne);
        }

        private static void WriteRuns(List<CellRun> runs, int runCount, Span<byte> destination) {
            int offset = 0;
            destination[offset++] = FormatVersion;
            WriteVarUInt(destination, ref offset, (ulong)runCount);
            for (int runIndex = 0; runIndex < runCount; runIndex++) {
                RunDeltas deltas = Deltas(runs, runIndex);
                WriteVarUInt(destination, ref offset, deltas.DeltaY);
                WriteVarUInt(destination, ref offset, deltas.DeltaX);
                WriteVarUInt(destination, ref offset, deltas.LengthMinusOne);
            }
        }

        private static RunDeltas Deltas(List<CellRun> runs, int runIndex) {
            CellRun run = runs[runIndex];
            long previousY = runIndex == 0 ? 0 : runs[runIndex - 1].Y;
            long previousX = runIndex == 0 ? 0 : runs[runIndex - 1].X;
            return new RunDeltas {
                DeltaY = ZigZagEncode(run.Y - previousY),
                DeltaX = ZigZagEncode(run.X - previousX),
                LengthMinusOne = (ulong)(run.Length - 1),
            };
        }

        private static ulong ZigZagEncode(long value) {
            return (ulong)((value << 1) ^ (value >> 63));
        }

        private static long ZigZagDecode(ulong value) {
            return (long)(value >> 1) ^ -(long)(value & 1);
        }

        private static int VarUIntSize(ulong value) {
            int size = 1;
            while (value >= 0x80) {
                value >>= 7;
                size++;
            }
            return size;
        }

        private static void WriteVarUInt(Span<byte> destination, ref int offset, ulong value) {
            while (value >= 0x80) {
                destination[offset++] = (byte)(value | 0x80);
                value >>= 7;
            }
            destination[offset++] = (byte)value;
        }

        private static bool TryReadVarUInt(ReadOnlySpan<byte> data, ref int offset, out ulong value) {
            value = 0;
            for (int byteIndex = 0; byteIndex < MaxVarIntBytes; byteIndex++) {
                if (offset >= data.Length) {
                    return false;
                }
                byte current = data[offset++];
                if (byteIndex == MaxVarIntBytes - 1 && current > 1) {
                    return false;
                }
                value |= (ulong)(current & 0x7F) << (7 * byteIndex);
                if ((current & 0x80) == 0) {
                    return true;
                }
            }
            return false;
        }

        private readonly struct CellRun {
            public CellRun(int x, int y, int length) {
                X = x;
                Y = y;
                Length = length;
            }

            public int X { get; }

            public int Y { get; }

            public int Length { get; }
        }

        private struct RunDeltas {
            public ulong DeltaY;
            public ulong DeltaX;
            public ulong LengthMinusOne;
        }

        private struct DecodeCursor {
            public long PreviousX;
            public long PreviousY;
            public long PreviousEndExclusive;
            public int RemainingCells;
        }
    }
}
