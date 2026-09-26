using System;
using AlpineLib.Procedural.Grid;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural {
    public sealed class CellGridTests {
        [Fact]
        public void NegativeOriginIndexesCorrectly() {
            var grid = new CellGrid<int>(new GridRect(-2, -3, 4, 5));

            grid[new CellCoord(-2, -3)] = 1;
            grid.Set(new CellCoord(1, 1), 2);

            Assert.Equal(1, grid[new CellCoord(-2, -3)]);
            Assert.Equal(2, grid[new CellCoord(1, 1)]);
            Assert.Equal(0, grid[new CellCoord(0, 0)]);
        }

        [Fact]
        public void OutOfBoundsThrowsAndTryGetFails() {
            var grid = new CellGrid<int>(new GridRect(0, 0, 2, 2));

            Assert.Throws<ArgumentOutOfRangeException>(() => grid[new CellCoord(2, 0)]);
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.Set(new CellCoord(-1, 0), 1));
            Assert.False(grid.TryGet(new CellCoord(0, 2), out int missing));
            Assert.Equal(0, missing);
            Assert.True(grid.TryGet(new CellCoord(1, 1), out _));
        }

        [Fact]
        public void FillAllAndCount() {
            var grid = new CellGrid<char>(new GridRect(0, 0, 5, 4));
            grid.Fill(grid.Bounds, '.');
            grid.Fill(new GridRect(1, 1, 2, 2), 'B');

            Assert.True(grid.All(new GridRect(1, 1, 2, 2), (cell) => cell == 'B'));
            Assert.False(grid.All(new GridRect(0, 1, 2, 2), (cell) => cell == 'B'));
            Assert.False(grid.All(new GridRect(4, 0, 2, 1), (cell) => cell == '.'));
            Assert.Equal(4, grid.Count((cell) => cell == 'B'));
            Assert.Equal(16, grid.Count((cell) => cell == '.'));
        }

        [Fact]
        public void FillOutsideBoundsThrows() {
            var grid = new CellGrid<int>(new GridRect(0, 0, 2, 2));

            Assert.Throws<ArgumentOutOfRangeException>(() => grid.Fill(new GridRect(1, 1, 2, 2), 1));
        }
    }
}
