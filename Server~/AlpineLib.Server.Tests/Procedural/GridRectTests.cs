using System;
using System.Linq;
using AlpineLib.Procedural.Grid;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural {
    public sealed class GridRectTests {
        [Fact]
        public void CellsAreRowMajor() {
            var rect = new GridRect(-1, 2, 3, 2);

            var cells = rect.Cells().ToArray();

            Assert.Equal(new[] {
                new CellCoord(-1, 2), new CellCoord(0, 2), new CellCoord(1, 2),
                new CellCoord(-1, 3), new CellCoord(0, 3), new CellCoord(1, 3),
            }, cells);
        }

        [Fact]
        public void EmptyRectHasNoCells() {
            Assert.Empty(new GridRect(0, 0, 0, 5).Cells());
            Assert.True(new GridRect(0, 0, 3, 0).IsEmpty);
        }

        [Fact]
        public void RejectsNegativeSize() {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GridRect(0, 0, -1, 1));
        }

        [Fact]
        public void ContainsUsesExclusiveMax() {
            var rect = new GridRect(0, 0, 2, 2);

            Assert.True(rect.Contains(new CellCoord(1, 1)));
            Assert.False(rect.Contains(new CellCoord(2, 1)));
            Assert.False(rect.Contains(new CellCoord(-1, 0)));
            Assert.True(rect.Contains(new GridRect(1, 0, 1, 2)));
            Assert.False(rect.Contains(new GridRect(1, 0, 2, 2)));
        }

        [Fact]
        public void OverlapsNeedsSharedCell() {
            var rect = new GridRect(0, 0, 2, 2);

            Assert.True(rect.Overlaps(new GridRect(1, 1, 2, 2)));
            Assert.False(rect.Overlaps(new GridRect(2, 0, 2, 2)));
            Assert.False(rect.Overlaps(new GridRect(0, 2, 2, 2)));
            Assert.False(rect.Overlaps(new GridRect(1, 1, 0, 0)));
        }

        [Fact]
        public void IntersectReturnsSharedBlockOrEmpty() {
            var rect = new GridRect(0, 0, 4, 3);

            Assert.Equal(new GridRect(2, 1, 2, 2), rect.Intersect(new GridRect(2, 1, 5, 5)));
            Assert.True(rect.Intersect(new GridRect(10, 10, 2, 2)).IsEmpty);
        }

        [Fact]
        public void InflateGrowsAndClamps() {
            var rect = new GridRect(1, 1, 2, 3);

            Assert.Equal(new GridRect(0, 0, 4, 5), rect.Inflate(1));
            Assert.Equal(new GridRect(2, 1, 0, 3), rect.Inflate(-1, 0));
            Assert.True(rect.Inflate(-5).IsEmpty);
        }

        [Fact]
        public void FromCornersAndMinMax() {
            var rect = GridRect.FromCorners(new CellCoord(3, -1), new CellCoord(1, 2));

            Assert.Equal(new GridRect(1, -1, 3, 4), rect);
            Assert.Equal(new CellCoord(1, -1), rect.Min);
            Assert.Equal(new CellCoord(3, 2), rect.MaxInclusive);
            Assert.Equal(12, rect.Area);
        }
    }
}
