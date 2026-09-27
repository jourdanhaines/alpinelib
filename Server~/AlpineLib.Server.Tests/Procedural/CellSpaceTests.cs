using System;
using AlpineLib.Procedural.Grid;
using AlpineLib.Procedural.Streaming;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural {
    public sealed class CellSpaceTests {
        [Theory]
        [InlineData(0.0, 0.0, 0, 0)]
        [InlineData(127.999, 0.0, 0, 0)]
        [InlineData(128.0, 0.0, 1, 0)]
        [InlineData(-0.001, 0.0, -1, 0)]
        [InlineData(-128.0, -128.0, -1, -1)]
        [InlineData(-128.001, 300.0, -2, 2)]
        public void WorldPositionsFloorIntoCells(double x, double z, int cellX, int cellY) {
            Assert.Equal(new CellCoord(cellX, cellY), new CellSpace(128.0).CellOf(x, z));
        }

        [Fact]
        public void AnOriginCellShiftsAddressesButNotGeometry() {
            CellSpace space = new CellSpace(128.0, new CellCoord(40, -3));

            Assert.Equal(new CellCoord(40, -3), space.CellOf(1.0, 1.0));
            Assert.Equal(new CellCoord(39, -2), space.CellOf(-1.0, 129.0));
            Assert.Equal(0.0, space.MinX(new CellCoord(40, -3)));
            Assert.Equal(-128.0, space.MinX(new CellCoord(39, 0)));
            Assert.Equal(128.0 * 3 + 64.0, space.CenterZ(new CellCoord(0, 0)));
        }

        [Fact]
        public void RebasingByWholeCellsKeepsEveryAddress() {
            CellSpace before = new CellSpace(128.0);
            CellSpace after = before.WithOrigin(new CellCoord(10, 4));
            double shiftX = -10 * 128.0;
            double shiftZ = -4 * 128.0;

            CellCoord cell = before.CellOf(1500.25, 700.5);

            Assert.Equal(cell, after.CellOf(1500.25 + shiftX, 700.5 + shiftZ));
        }

        [Fact]
        public void FarPositionsStayExactInDouble() {
            CellSpace space = new CellSpace(128.0);
            double farX = 1_000_000_000.0 * 128.0 + 3.5;

            CellCoord cell = space.CellOf(farX, 0.0);
            space.ToCellLocal(cell, farX, 0.0, out double localX, out double localZ);

            Assert.Equal(1_000_000_000, cell.X);
            Assert.Equal(3.5, localX);
            Assert.Equal(0.0, localZ);
        }

        [Fact]
        public void PositionsOutsideTheCellRangeAreRefused() {
            CellSpace space = new CellSpace(1.0);

            Assert.Throws<ArgumentOutOfRangeException>(() => space.CellOf(1e12, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => space.CellOf(double.NaN, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => space.CellOf(0.0, double.NegativeInfinity));
        }

        [Fact]
        public void CellSizeMustBePositiveAndFinite() {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CellSpace(0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CellSpace(-1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CellSpace(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CellSpace(double.PositiveInfinity));
        }

        [Fact]
        public void SquareCoversTwoRPlusOneCells() {
            GridRect square = CellSpace.Square(new CellCoord(3, -2), 2);

            Assert.Equal(new GridRect(1, -4, 5, 5), square);
            Assert.Equal(1, CellSpace.Square(new CellCoord(0, 0), 0).Area);
        }
    }
}
