using AlpineLib.Procedural.Sky;

namespace AlpineLib.Sky {
    /// <summary>Where one camera picks ring features from: its absolute xz, keep-out cell, world-to-absolute offset, ground height and fog.</summary>
    internal readonly struct BackdropSectorView {
        public BackdropSectorView(double absoluteX, double absoluteZ, long cellX, long cellZ, double offsetX, double offsetZ, float groundY, BackdropFog fog) {
            AbsoluteX = absoluteX;
            AbsoluteZ = absoluteZ;
            CellX = cellX;
            CellZ = cellZ;
            OffsetX = offsetX;
            OffsetZ = offsetZ;
            GroundY = groundY;
            Fog = fog;
        }

        public double AbsoluteX { get; }
        public double AbsoluteZ { get; }
        public long CellX { get; }
        public long CellZ { get; }
        public double OffsetX { get; }
        public double OffsetZ { get; }
        public float GroundY { get; }
        public BackdropFog Fog { get; }
    }
}
