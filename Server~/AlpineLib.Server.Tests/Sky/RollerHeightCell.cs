using AlpineLib.Procedural.Sky;

namespace AlpineLib.Server.Tests.Sky {
    // One cell of the test world's gentle global ground, answering in its own local plane.
    public sealed class RollerHeightCell : IRollerCell {
        private readonly double _originX;
        private readonly double _originZ;

        public RollerHeightCell(long cellX, long cellZ, double cellSize) {
            _originX = RollerCellWorld.CentreOf(cellX, cellSize);
            _originZ = RollerCellWorld.CentreOf(cellZ, cellSize);
        }

        public RollerBlock BlockAt(double x, double z, double radius) {
            return RollerBlock.None;
        }

        public RollerSupport SupportAt(double x, double z, double radius) {
            return RollerSupport.BallOnHeights(RollerTestWorld.Height, x + _originX, z + _originZ, radius);
        }

        public double SurfaceAt(double x, double z) {
            return RollerTestWorld.Height(x + _originX, z + _originZ);
        }
    }
}
