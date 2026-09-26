using System;
using System.Numerics;
using AlpineLib.Procedural.Grid;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural {
    public sealed class GridFrameTests {
        [Fact]
        public void CellCornerAndCentreMapToKitFrame() {
            // Station frame: X(i) = 5.90 + 3(i − 1), so origin x = 2.90.
            var frame = new GridFrame(3f, new Vector3(2.9f, 0f, 0f));

            Assert.Equal(new Vector3(5.9f, 0f, 1.5f), frame.ToKit(new CellCoord(1, 0), 1.5f));
            Assert.Equal(new Vector3(7.4f, 4.5f, 0f), frame.CellCentre(new CellCoord(1, 1), 0f));
        }

        [Fact]
        public void ContinuousCoordinatesScaleByCellSize() {
            var frame = new GridFrame(2f, new Vector3(1f, -1f, 0.5f));

            Assert.Equal(new Vector3(2f, 5f, 1.5f), frame.ToKit(0.5f, 3f, 1f));
            Assert.Equal(frame.ToKit(-2f, 3f, 0f), frame.ToKit(new CellCoord(-2, 3), 0f));
        }

        [Fact]
        public void RejectsNonPositiveCellSize() {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GridFrame(0f, Vector3.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GridFrame(float.NaN, Vector3.Zero));
        }
    }
}
