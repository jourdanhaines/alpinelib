using System;
using System.Collections.Generic;
using System.Linq;
using AlpineLib.Procedural.Grid;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural {
    public sealed class KnownCellSetTests {
        private const int Unlimited = int.MaxValue;

        [Fact]
        public void EmptySetRoundTrips() {
            KnownCellSet empty = new KnownCellSet();

            byte[] bytes = empty.Serialize();

            Assert.Equal(new byte[] { CellRunCodec.FormatVersion, 0 }, bytes);
            Assert.True(KnownCellSet.TryDeserialize(bytes, Unlimited, out KnownCellSet copy));
            Assert.Equal(0, copy.Count);
            Assert.True(copy.Bounds.IsEmpty);
            Assert.Equal(0, copy.Version);
        }

        [Fact]
        public void SingleCellRoundTrips() {
            AssertRoundTrip(new[] { new CellCoord(7, -3) });
        }

        [Fact]
        public void NegativeAndExtremeCoordinatesRoundTrip() {
            AssertRoundTrip(new[] {
                new CellCoord(-5, -5), new CellCoord(-4, -5), new CellCoord(3, -5),
                new CellCoord(int.MinValue, int.MinValue), new CellCoord(int.MaxValue, int.MaxValue),
                new CellCoord(int.MaxValue - 1, int.MaxValue), new CellCoord(int.MinValue, 0),
            });
        }

        [Fact]
        public void SeventeenSquareBlockFitsIn64Bytes() {
            GridRect block = new GridRect(-8, -8, 17, 17);

            byte[] bytes = CellRunCodec.Encode(block.Cells());

            Assert.True(bytes.Length <= 64, $"17x17 block took {bytes.Length} bytes.");
            AssertRoundTrip(block.Cells().ToArray());
        }

        [Fact]
        public void RandomCellsRoundTrip() {
            Random random = new Random(1234);
            HashSet<CellCoord> cells = new HashSet<CellCoord>();
            while (cells.Count < 2000) {
                cells.Add(new CellCoord(random.Next(-60, 60), random.Next(-60, 60)));
            }

            AssertRoundTrip(cells.ToArray());
        }

        [Fact]
        public void EncodeIgnoresOrderAndDuplicates() {
            CellCoord[] shuffled = { new CellCoord(2, 1), new CellCoord(0, 0), new CellCoord(1, 1), new CellCoord(0, 0) };

            byte[] bytes = CellRunCodec.Encode(shuffled);

            List<CellCoord> decoded = new List<CellCoord>();
            Assert.True(CellRunCodec.TryDecode(bytes, Unlimited, decoded));
            Assert.Equal(new[] { new CellCoord(0, 0), new CellCoord(1, 1), new CellCoord(2, 1) }, decoded);
        }

        [Fact]
        public void SpanEncodeMatchesArrayEncodeAndReportsTooSmall() {
            List<CellCoord> sorted = new GridRect(0, 0, 4, 3).Cells().ToList();
            byte[] expected = CellRunCodec.Encode(sorted);
            byte[] buffer = new byte[64];

            int written = CellRunCodec.Encode(sorted, buffer);

            Assert.Equal(expected, buffer.Take(written).ToArray());
            Assert.Equal(-1, CellRunCodec.Encode(sorted, new byte[expected.Length - 1]));
        }

        [Fact]
        public void EveryTruncationFails() {
            byte[] bytes = CellRunCodec.Encode(new GridRect(-3, 2, 5, 4).Cells());

            for (int length = 0; length < bytes.Length; length++) {
                List<CellCoord> destination = new List<CellCoord> { new CellCoord(99, 99) };
                Assert.False(CellRunCodec.TryDecode(bytes.AsSpan(0, length), Unlimited, destination));
                Assert.Single(destination);
            }
        }

        [Fact]
        public void HostileBytesFail() {
            byte[][] hostile = {
                new byte[] { 2, 0 },
                new byte[] { 1, 0, 0 },
                new byte[] { 1, 1, 0, 0, 0, 0 },
                new byte[] { 1, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F, 0, 0, 0 },
                new byte[] { 1, 1, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F },
                new byte[] { 1, 1, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x02, 0, 0 },
                new byte[] { 1, 2, 0, 0, 4, 0, 2, 0 },
                new byte[] { 1, 2, 0, 0, 4, 1, 0, 0 },
                new byte[] { 1, 1, 0, 0xFE, 0xFF, 0xFF, 0xFF, 0x0F, 1 },
            };

            foreach (byte[] bytes in hostile) {
                List<CellCoord> destination = new List<CellCoord>();
                Assert.False(CellRunCodec.TryDecode(bytes, Unlimited, destination));
                Assert.Empty(destination);
                Assert.False(KnownCellSet.TryDeserialize(bytes, Unlimited, out KnownCellSet set));
                Assert.Null(set);
            }
        }

        [Fact]
        public void RandomGarbageNeverThrows() {
            Random random = new Random(99);
            byte[] valid = CellRunCodec.Encode(new GridRect(0, 0, 9, 9).Cells());
            for (int trial = 0; trial < 5000; trial++) {
                byte[] bytes = (byte[])valid.Clone();
                bytes[1 + random.Next(bytes.Length - 1)] = (byte)random.Next(256);
                CellRunCodec.TryDecode(bytes, 1000, new List<CellCoord>());

                byte[] noise = new byte[random.Next(1, 40)];
                random.NextBytes(noise);
                noise[0] = CellRunCodec.FormatVersion;
                CellRunCodec.TryDecode(noise, 1000, new List<CellCoord>());
            }
        }

        [Fact]
        public void MaxCellsIsEnforcedOnTheSumOfRunLengths() {
            byte[] bytes = CellRunCodec.Encode(new GridRect(0, 0, 10, 10).Cells());

            Assert.True(CellRunCodec.TryDecode(bytes, 100, new List<CellCoord>()));
            Assert.False(CellRunCodec.TryDecode(bytes, 99, new List<CellCoord>()));
            Assert.False(KnownCellSet.TryDeserialize(bytes, 99, out _));
            Assert.False(CellRunCodec.TryDecode(new byte[] { 1, 1, 0, 0, 0x7F }, 100, new List<CellCoord>()));
        }

        [Fact]
        public void CellsAddedFiresOncePerCallWithOnlyNewCells() {
            KnownCellSet set = new KnownCellSet();
            List<List<CellCoord>> raised = new List<List<CellCoord>>();
            set.CellsAdded += (cells) => raised.Add(cells.ToList());

            int firstAdded = set.AddRange(new GridRect(0, 0, 2, 2).Cells());
            int secondAdded = set.AddRange(new GridRect(1, 0, 2, 2).Cells());
            int repeatAdded = set.AddRange(new GridRect(0, 0, 3, 2).Cells());
            bool single = set.Add(new CellCoord(5, 5));
            bool duplicate = set.Add(new CellCoord(5, 5));

            Assert.Equal(4, firstAdded);
            Assert.Equal(2, secondAdded);
            Assert.Equal(0, repeatAdded);
            Assert.True(single);
            Assert.False(duplicate);
            Assert.Equal(3, raised.Count);
            Assert.Equal(new[] { new CellCoord(2, 0), new CellCoord(2, 1) }, raised[1]);
            Assert.Equal(new[] { new CellCoord(5, 5) }, raised[2]);
            Assert.Equal(3, set.Version);
            Assert.Equal(7, set.Count);
        }

        [Fact]
        public void AddingFromAHandlerThrows() {
            KnownCellSet set = new KnownCellSet();
            set.CellsAdded += (cells) => set.Add(new CellCoord(9, 9));

            Assert.Throws<InvalidOperationException>(() => set.Add(new CellCoord(0, 0)));
            Assert.False(set.Contains(new CellCoord(9, 9)));
        }

        [Fact]
        public void BoundsGrowMonotonically() {
            KnownCellSet set = new KnownCellSet();
            Assert.True(set.Bounds.IsEmpty);

            set.Add(new CellCoord(2, 3));
            Assert.Equal(new GridRect(2, 3, 1, 1), set.Bounds);

            set.Add(new CellCoord(-1, 5));
            Assert.Equal(new GridRect(-1, 3, 4, 3), set.Bounds);

            set.Add(new CellCoord(0, 4));
            Assert.Equal(new GridRect(-1, 3, 4, 3), set.Bounds);
        }

        [Fact]
        public void CopyToAppendsEveryCell() {
            KnownCellSet set = new KnownCellSet();
            set.AddRange(new GridRect(0, 0, 3, 3).Cells());
            List<CellCoord> destination = new List<CellCoord>();

            set.CopyTo(destination);

            Assert.Equal(new GridRect(0, 0, 3, 3).Cells().ToHashSet(), destination.ToHashSet());
            Assert.Equal(9, destination.Count);
        }

        [Fact]
        public void EncodeChunkPacksWholeRunsAndResumes() {
            Random random = new Random(7);
            HashSet<CellCoord> unique = new HashSet<CellCoord>();
            while (unique.Count < 3000) {
                unique.Add(new CellCoord(random.Next(-200, 200), random.Next(-40, 40)));
            }
            List<CellCoord> sorted = unique.ToList();
            sorted.Sort(CellRunCodec.CompareRowMajor);
            byte[] buffer = new byte[64];
            List<CellCoord> decoded = new List<CellCoord>();
            int startIndex = 0;
            int chunks = 0;

            while (startIndex < sorted.Count) {
                int written = CellRunCodec.EncodeChunk(sorted, startIndex, buffer, out int consumed);
                Assert.InRange(written, 2, buffer.Length);
                Assert.True(consumed > 0);
                AssertEndsOnRunBoundary(sorted, startIndex + consumed);
                Assert.True(CellRunCodec.TryDecode(buffer.AsSpan(0, written), Unlimited, decoded));
                startIndex += consumed;
                chunks++;
            }

            Assert.True(chunks > 1);
            Assert.Equal(sorted, decoded);
        }

        [Fact]
        public void EncodeChunkCapsCellsPerChunk() {
            List<CellCoord> sorted = new GridRect(-50, -50, 100, 100).Cells().ToList();
            byte[] buffer = new byte[960];
            List<CellCoord> decoded = new List<CellCoord>();
            int startIndex = 0;
            int chunks = 0;

            while (startIndex < sorted.Count) {
                int written = CellRunCodec.EncodeChunk(sorted, startIndex, buffer, 4096, out int consumed);
                Assert.InRange(consumed, 1, 4096);
                AssertEndsOnRunBoundary(sorted, startIndex + consumed);
                Assert.True(CellRunCodec.TryDecode(buffer.AsSpan(0, written), 4096, decoded));
                startIndex += consumed;
                chunks++;
            }

            Assert.Equal(3, chunks);
            Assert.Equal(sorted, decoded);
        }

        [Fact]
        public void EncodeChunkSplitsARunLongerThanTheCap() {
            List<CellCoord> sorted = new GridRect(0, 0, 10000, 1).Cells().ToList();
            byte[] buffer = new byte[960];
            List<CellCoord> decoded = new List<CellCoord>();
            int startIndex = 0;

            while (startIndex < sorted.Count) {
                int written = CellRunCodec.EncodeChunk(sorted, startIndex, buffer, 4096, out int consumed);
                Assert.InRange(consumed, 1, 4096);
                Assert.True(CellRunCodec.TryDecode(buffer.AsSpan(0, written), 4096, decoded));
                startIndex += consumed;
            }

            Assert.Equal(sorted, decoded);
        }

        [Fact]
        public void EncodeChunkDefersALongRunBehindAShortOne() {
            List<CellCoord> sorted = new List<CellCoord> { new CellCoord(5, -1) };
            sorted.AddRange(new GridRect(0, 0, 10000, 1).Cells());
            byte[] buffer = new byte[960];
            List<CellCoord> decoded = new List<CellCoord>();
            List<int> consumedPerChunk = new List<int>();
            int startIndex = 0;

            while (startIndex < sorted.Count) {
                int written = CellRunCodec.EncodeChunk(sorted, startIndex, buffer, 4096, out int consumed);
                Assert.True(CellRunCodec.TryDecode(buffer.AsSpan(0, written), 4096, decoded));
                consumedPerChunk.Add(consumed);
                startIndex += consumed;
            }

            Assert.Equal(new[] { 1, 4096, 4096, 1808 }, consumedPerChunk);
            Assert.Equal(sorted, decoded);
        }

        [Fact]
        public void EncodeChunkFillsOneChunkWhenEverythingFits() {
            List<CellCoord> sorted = new GridRect(0, 0, 17, 17).Cells().ToList();
            byte[] buffer = new byte[960];

            int written = CellRunCodec.EncodeChunk(sorted, 0, buffer, out int consumed);

            Assert.Equal(sorted.Count, consumed);
            Assert.Equal(CellRunCodec.Encode(sorted), buffer.Take(written).ToArray());
        }

        [Fact]
        public void EncodeChunkReportsTooSmallAndEmptyTail() {
            List<CellCoord> sorted = new List<CellCoord> { new CellCoord(100000, 100000) };

            Assert.Equal(-1, CellRunCodec.EncodeChunk(sorted, 0, new byte[4], out int consumed));
            Assert.Equal(0, consumed);
            Assert.Equal(2, CellRunCodec.EncodeChunk(sorted, 1, new byte[4], out consumed));
            Assert.Equal(0, consumed);
        }

        [Fact]
        public void UnsortedInputToSortedEncodersThrows() {
            List<CellCoord> unsorted = new List<CellCoord> { new CellCoord(1, 0), new CellCoord(0, 0) };

            Assert.Throws<ArgumentException>(() => CellRunCodec.Encode(unsorted, new byte[16]));
            Assert.Throws<ArgumentException>(() => CellRunCodec.EncodeChunk(unsorted, 0, new byte[16], out _));
        }

        private static void AssertRoundTrip(IReadOnlyCollection<CellCoord> cells) {
            KnownCellSet set = new KnownCellSet();
            set.AddRange(cells);

            Assert.True(KnownCellSet.TryDeserialize(set.Serialize(), cells.Count, out KnownCellSet copy));

            Assert.Equal(set.Count, copy.Count);
            Assert.Equal(set.Bounds, copy.Bounds);
            Assert.All(cells, (cell) => Assert.True(copy.Contains(cell)));
        }

        private static void AssertEndsOnRunBoundary(List<CellCoord> sorted, int endIndex) {
            if (endIndex == 0 || endIndex >= sorted.Count) {
                return;
            }
            CellCoord last = sorted[endIndex - 1];
            CellCoord next = sorted[endIndex];
            Assert.False(last.Y == next.Y && last.X + 1 == next.X, $"Chunk split a run at {last}.");
        }
    }
}
