using AlpineLib.Procedural.Grid;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural {
    public sealed class GridDirectionsTests {
        [Fact]
        public void OffsetsFollowNorthIsPlusY() {
            Assert.Equal(new CellCoord(0, 1), GridDirections.ToOffset(GridDirection.North));
            Assert.Equal(new CellCoord(1, 0), GridDirections.ToOffset(GridDirection.East));
            Assert.Equal(new CellCoord(0, -1), GridDirections.ToOffset(GridDirection.South));
            Assert.Equal(new CellCoord(-1, 0), GridDirections.ToOffset(GridDirection.West));
            Assert.Equal(new CellCoord(2, 4), new CellCoord(2, 3).Offset(GridDirection.North));
        }

        [Fact]
        public void RotateIsClockwiseAndWraps() {
            Assert.Equal(GridDirection.East, GridDirections.Rotate(GridDirection.North, 1));
            Assert.Equal(GridDirection.West, GridDirections.Rotate(GridDirection.North, -1));
            Assert.Equal(GridDirection.South, GridDirections.Rotate(GridDirection.West, 7));
            Assert.Equal(GridDirection.North, GridDirections.Rotate(GridDirection.North, -8));
        }

        [Fact]
        public void QuarterTurnsInvertsRotate() {
            foreach (GridDirection from in GridDirections.All) {
                foreach (GridDirection to in GridDirections.All) {
                    int turns = GridDirections.QuarterTurns(from, to);
                    Assert.InRange(turns, 0, 3);
                    Assert.Equal(to, GridDirections.Rotate(from, turns));
                }
            }
        }

        [Fact]
        public void OppositeCancelsOffset() {
            foreach (GridDirection direction in GridDirections.All) {
                CellCoord sum = GridDirections.ToOffset(direction) + GridDirections.ToOffset(GridDirections.Opposite(direction));
                Assert.Equal(new CellCoord(0, 0), sum);
            }
        }

        [Fact]
        public void CellCoordFormatsAndCompares() {
            Assert.Equal("(-3,7)", new CellCoord(-3, 7).ToString());
            Assert.Equal(new CellCoord(1, 2), new CellCoord(3, 5) - new CellCoord(2, 3));
            Assert.True(new CellCoord(1, 2) == new CellCoord(1, 2));
        }
    }
}
