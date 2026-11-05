using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// Greedy cover of target cells of a base <see cref="PlanFrame"/> by rectangles of turned frames that stay on
    /// allowed cells: each pick is the grown rectangle (any frame, sampled seeds) that newly covers the most
    /// targets; a base cell counts as covered only when the rectangle holds the whole cell. Then the pair whose
    /// merged rectangle adds the least area merges while the merge stays on allowed cells.
    /// </summary>
    internal sealed class PlanCoverage {
        private const int SeedsPerFrame = 24;
        private const double EdgeTolerance = 1e-9;
        private static readonly double[] SampleU = { 0.01, 0.99, 0.01, 0.99, 0.5 };
        private static readonly double[] SampleV = { 0.01, 0.01, 0.99, 0.99, 0.5 };

        private readonly PlanFrame _base;
        private readonly List<PlanFrame> _frames = new List<PlanFrame>();

        public PlanCoverage(PlanFrame baseFrame, int yawStepDegrees) {
            _base = baseFrame;
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
            for (int corner = 0; corner < 4; corner++) {
                baseFrame.PointIn(corner == 0 || corner == 3 ? 0 : baseFrame.Columns, corner < 2 ? 0 : baseFrame.Rows, 0.0, 0.0, out double x, out double z);
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minZ = Math.Min(minZ, z);
                maxZ = Math.Max(maxZ, z);
            }
            _frames.Add(baseFrame);
            for (int step = yawStepDegrees; step < 90; step += yawStepDegrees) {
                _frames.Add(PlanFrame.Around((baseFrame.YawDegrees + step) % 90, baseFrame.Cell, minX, maxX, minZ, maxZ));
            }
        }

        /// <summary>Covers every target not yet in <paramref name="covered"/>, marking what it covers; the rectangles carry their cells.</summary>
        public List<PlanRect> Cover(bool[] target, bool[] allowed, bool[] covered) {
            var allowedSums = new List<int[]>(_frames.Count);
            foreach (PlanFrame frame in _frames) allowedSums.Add(Sums(frame, AllowedIn(frame, allowed)));

            var rects = new List<PlanRect>();
            while (TryPick(target, covered, allowedSums, out PlanRect best)) {
                Assign(best, target, covered);
                if (best.Cells.Count == 0) best = SingleCell(FirstOpen(target, covered), target, covered);
                rects.Add(best);
            }
            Merge(rects, allowedSums);
            return rects;
        }

        // The grown rectangle with the most new targets over every frame; false once every target is covered.
        private bool TryPick(bool[] target, bool[] covered, List<int[]> allowedSums, out PlanRect best) {
            best = null;
            if (FirstOpen(target, covered) < 0) return false;

            long bestGain = 0;
            for (int frameIndex = 0; frameIndex < _frames.Count; frameIndex++) PickInFrame(frameIndex, target, covered, allowedSums[frameIndex], ref bestGain, ref best);
            if (best == null) best = SingleCell(FirstOpen(target, covered), null, null);
            return true;
        }

        private void PickInFrame(int frameIndex, bool[] target, bool[] covered, int[] allowedSums, ref long bestGain, ref PlanRect best) {
            PlanFrame frame = _frames[frameIndex];
            bool[] open = OpenIn(frame, target, covered);
            int[] openSums = Sums(frame, open);
            foreach (int seed in Seeds(frame, open, allowedSums)) {
                PlanRect grown = Grow(frame, seed, allowedSums, openSums, out long gain);
                if (gain <= bestGain) continue;

                bestGain = gain;
                best = grown;
            }
        }

        private static List<int> Seeds(PlanFrame frame, bool[] open, int[] allowedSums) {
            var candidates = new List<int>();
            for (int index = 0; index < frame.Count; index++) {
                if (open[index] && CountIn(frame, allowedSums, index % frame.Columns, index % frame.Columns, index / frame.Columns, index / frame.Columns) == 1) candidates.Add(index);
            }
            if (candidates.Count <= SeedsPerFrame) return candidates;

            var seeds = new List<int>(SeedsPerFrame);
            for (int pick = 0; pick < SeedsPerFrame; pick++) seeds.Add(candidates[(int)((long)pick * candidates.Count / SeedsPerFrame)]);
            return seeds;
        }

        // Grows one row or column at a time, always the side that adds the most open cells, while every cell is allowed.
        private static PlanRect Grow(PlanFrame frame, int seed, int[] allowedSums, int[] openSums, out long gain) {
            int minColumn = seed % frame.Columns, maxColumn = minColumn, minRow = seed / frame.Columns, maxRow = minRow;
            gain = CountIn(frame, openSums, minColumn, maxColumn, minRow, maxRow);
            while (true) {
                int side = BestSide(frame, allowedSums, openSums, minColumn, maxColumn, minRow, maxRow, ref gain);
                if (side < 0) return new PlanRect(frame, minColumn, maxColumn, minRow, maxRow);

                Expand(side, minColumn, maxColumn, minRow, maxRow, out minColumn, out maxColumn, out minRow, out maxRow);
            }
        }

        // The side whose new row or column stays allowed and adds the most open cells (−1 for none); gain becomes the grown count.
        private static int BestSide(PlanFrame frame, int[] allowedSums, int[] openSums, int minColumn, int maxColumn, int minRow, int maxRow, ref long gain) {
            int side = -1;
            for (int candidate = 0; candidate < 4; candidate++) {
                Expand(candidate, minColumn, maxColumn, minRow, maxRow, out int c0, out int c1, out int r0, out int r1);
                if (c0 < 0 || r0 < 0 || c1 >= frame.Columns || r1 >= frame.Rows) continue;
                if (CountIn(frame, allowedSums, c0, c1, r0, r1) != (long)(c1 - c0 + 1) * (r1 - r0 + 1)) continue;

                long candidateGain = CountIn(frame, openSums, c0, c1, r0, r1);
                if (candidateGain <= gain) continue;

                gain = candidateGain;
                side = candidate;
            }
            return side;
        }

        private static void Expand(int side, int minColumn, int maxColumn, int minRow, int maxRow, out int c0, out int c1, out int r0, out int r1) {
            c0 = side == 0 ? minColumn - 1 : minColumn;
            c1 = side == 1 ? maxColumn + 1 : maxColumn;
            r0 = side == 2 ? minRow - 1 : minRow;
            r1 = side == 3 ? maxRow + 1 : maxRow;
        }

        // Base cells the rectangle holds whole that are still open targets become its cells.
        private void Assign(PlanRect rect, bool[] target, bool[] covered) {
            BaseRange(rect, out int minColumn, out int maxColumn, out int minRow, out int maxRow);
            for (int row = minRow; row <= maxRow; row++) {
                for (int column = minColumn; column <= maxColumn; column++) AssignCell(rect, row * _base.Columns + column, target, covered);
            }
        }

        private void AssignCell(PlanRect rect, int index, bool[] target, bool[] covered) {
            if (!target[index] || covered[index] || !HoldsCell(rect, index)) return;

            covered[index] = true;
            rect.Cells.Add(index);
        }

        public bool HoldsCell(PlanRect rect, int index) {
            int column = index % _base.Columns;
            int row = index / _base.Columns;
            for (int corner = 0; corner < 4; corner++) {
                _base.PointIn(column, row, corner == 0 || corner == 3 ? 0.0 : 1.0, corner < 2 ? 0.0 : 1.0, out double x, out double z);
                if (!rect.Holds(x, z, EdgeTolerance)) return false;
            }
            return true;
        }

        /// <summary>Adds to the rectangle's cells every occupied base cell it holds whole and marks them in <paramref name="held"/>.</summary>
        public void TakeHeld(PlanRect rect, Func<int, bool> occupied, bool[] held) {
            BaseRange(rect, out int minColumn, out int maxColumn, out int minRow, out int maxRow);
            for (int row = minRow; row <= maxRow; row++) {
                for (int column = minColumn; column <= maxColumn; column++) TakeCell(rect, row * _base.Columns + column, occupied, held);
            }
        }

        private void TakeCell(PlanRect rect, int index, Func<int, bool> occupied, bool[] held) {
            if (held[index] || !HoldsCell(rect, index)) return;

            held[index] = true;
            if (occupied(index)) rect.Cells.Add(index);
        }

        private void BaseRange(PlanRect rect, out int minColumn, out int maxColumn, out int minRow, out int maxRow) {
            double lowX = double.PositiveInfinity, highX = double.NegativeInfinity, lowZ = double.PositiveInfinity, highZ = double.NegativeInfinity;
            for (int corner = 0; corner < 4; corner++) {
                rect.Corner(corner, out double x, out double z);
                _base.ToLocal(x, z, out double localX, out double localZ);
                lowX = Math.Min(lowX, localX);
                highX = Math.Max(highX, localX);
                lowZ = Math.Min(lowZ, localZ);
                highZ = Math.Max(highZ, localZ);
            }
            minColumn = Math.Max(0, (int)Math.Floor((lowX - _base.OriginX) / _base.Cell));
            maxColumn = Math.Min(_base.Columns - 1, (int)Math.Floor((highX - _base.OriginX) / _base.Cell));
            minRow = Math.Max(0, (int)Math.Floor((lowZ - _base.OriginZ) / _base.Cell));
            maxRow = Math.Min(_base.Rows - 1, (int)Math.Floor((highZ - _base.OriginZ) / _base.Cell));
        }

        // A one-cell base rectangle: the fallback that always makes progress.
        private PlanRect SingleCell(int index, bool[] target, bool[] covered) {
            var rect = new PlanRect(_base, index % _base.Columns, index % _base.Columns, index / _base.Columns, index / _base.Columns);
            if (target != null) AssignCell(rect, index, target, covered);
            return rect;
        }

        private static int FirstOpen(bool[] target, bool[] covered) {
            for (int index = 0; index < target.Length; index++) {
                if (target[index] && !covered[index]) return index;
            }
            return -1;
        }

        // A frame cell is allowed when all five samples land on allowed base cells.
        private bool[] AllowedIn(PlanFrame frame, bool[] allowed) {
            var result = new bool[frame.Count];
            for (int index = 0; index < frame.Count; index++) result[index] = SamplesAll(frame, index, allowed);
            return result;
        }

        private bool SamplesAll(PlanFrame frame, int index, bool[] allowed) {
            for (int sample = 0; sample < SampleU.Length; sample++) {
                frame.PointIn(index % frame.Columns, index / frame.Columns, SampleU[sample], SampleV[sample], out double x, out double z);
                int baseIndex = _base.IndexAt(x, z);
                if (baseIndex < 0 || !allowed[baseIndex]) return false;
            }
            return true;
        }

        // A frame cell is open when its centre lands on an open target.
        private bool[] OpenIn(PlanFrame frame, bool[] target, bool[] covered) {
            var result = new bool[frame.Count];
            for (int index = 0; index < frame.Count; index++) {
                frame.PointIn(index % frame.Columns, index / frame.Columns, 0.5, 0.5, out double x, out double z);
                int baseIndex = _base.IndexAt(x, z);
                result[index] = baseIndex >= 0 && target[baseIndex] && !covered[baseIndex];
            }
            return result;
        }

        private static int[] Sums(PlanFrame frame, bool[] cells) {
            int stride = frame.Columns + 1;
            var sums = new int[stride * (frame.Rows + 1)];
            for (int row = 0; row < frame.Rows; row++) {
                int running = 0;
                for (int column = 0; column < frame.Columns; column++) {
                    running += cells[row * frame.Columns + column] ? 1 : 0;
                    sums[(row + 1) * stride + column + 1] = sums[row * stride + column + 1] + running;
                }
            }
            return sums;
        }

        private static long CountIn(PlanFrame frame, int[] sums, int minColumn, int maxColumn, int minRow, int maxRow) {
            int stride = frame.Columns + 1;
            return sums[(maxRow + 1) * stride + maxColumn + 1] - sums[minRow * stride + maxColumn + 1] - sums[(maxRow + 1) * stride + minColumn] + sums[minRow * stride + minColumn];
        }

        // Cheapest valid merge first, until none stays on allowed cells.
        private void Merge(List<PlanRect> rects, List<int[]> allowedSums) {
            while (TryCheapestMerge(rects, allowedSums, out int keep, out int drop, out PlanRect merged)) {
                merged.Cells.AddRange(rects[keep].Cells);
                merged.Cells.AddRange(rects[drop].Cells);
                rects[keep] = merged;
                rects.RemoveAt(drop);
            }
        }

        private bool TryCheapestMerge(List<PlanRect> rects, List<int[]> allowedSums, out int keep, out int drop, out PlanRect merged) {
            keep = -1;
            drop = -1;
            merged = null;
            long bestAdded = long.MaxValue;
            for (int pair = 0; pair < rects.Count * rects.Count; pair++) {
                int first = pair / rects.Count;
                int second = pair % rects.Count;
                if (second <= first) continue;

                PlanRect union = CheapestUnion(rects[first], rects[second], allowedSums);
                if (union == null || union.Area - rects[first].Area - rects[second].Area >= bestAdded) continue;

                bestAdded = union.Area - rects[first].Area - rects[second].Area;
                keep = first;
                drop = second;
                merged = union;
            }
            return merged != null;
        }

        private PlanRect CheapestUnion(PlanRect first, PlanRect second, List<int[]> allowedSums) {
            PlanRect inFirst = UnionIn(first.Frame, first, second, allowedSums);
            PlanRect inSecond = first.Frame == second.Frame ? null : UnionIn(second.Frame, first, second, allowedSums);
            if (inFirst == null) return inSecond;
            if (inSecond == null) return inFirst;

            return inSecond.Area < inFirst.Area ? inSecond : inFirst;
        }

        // The frame's cell rectangle around both rectangles, or null when it leaves the grid or allowed cells.
        private PlanRect UnionIn(PlanFrame frame, PlanRect first, PlanRect second, List<int[]> allowedSums) {
            double lowX = double.PositiveInfinity, highX = double.NegativeInfinity, lowZ = double.PositiveInfinity, highZ = double.NegativeInfinity;
            foreach (PlanRect rect in new[] { first, second }) {
                for (int corner = 0; corner < 4; corner++) {
                    rect.Corner(corner, out double x, out double z);
                    frame.ToLocal(x, z, out double localX, out double localZ);
                    lowX = Math.Min(lowX, localX);
                    highX = Math.Max(highX, localX);
                    lowZ = Math.Min(lowZ, localZ);
                    highZ = Math.Max(highZ, localZ);
                }
            }
            int minColumn = (int)Math.Floor((lowX - frame.OriginX) / frame.Cell + 1e-6);
            int maxColumn = (int)Math.Ceiling((highX - frame.OriginX) / frame.Cell - 1e-6) - 1;
            int minRow = (int)Math.Floor((lowZ - frame.OriginZ) / frame.Cell + 1e-6);
            int maxRow = (int)Math.Ceiling((highZ - frame.OriginZ) / frame.Cell - 1e-6) - 1;
            if (minColumn < 0 || minRow < 0 || maxColumn >= frame.Columns || maxRow >= frame.Rows) return null;

            int[] sums = allowedSums[_frames.IndexOf(frame)];
            long area = (long)(maxColumn - minColumn + 1) * (maxRow - minRow + 1);
            return CountIn(frame, sums, minColumn, maxColumn, minRow, maxRow) == area ? new PlanRect(frame, minColumn, maxColumn, minRow, maxRow) : null;
        }
    }
}
