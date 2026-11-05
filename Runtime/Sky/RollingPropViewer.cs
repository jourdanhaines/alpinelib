namespace AlpineLib.Sky {
    // Where a RollingPropField's viewer stands this refresh, in the absolute plane, with its draw distance and the wind's
    // unit direction now: what it would see born and what it would see break up.
    internal readonly struct RollingPropViewer {
        public RollingPropViewer(bool hasPlane, double x, double z, double drawDistance, double windX, double windZ) {
            HasPlane = hasPlane;
            X = x;
            Z = z;
            DrawDistance = drawDistance;
            WindX = windX;
            WindZ = windZ;
        }

        /// <summary>False when no cell root places the viewer (nothing is near it).</summary>
        public bool HasPlane { get; }

        public double X { get; }
        public double Z { get; }
        public double DrawDistance { get; }
        public double WindX { get; }
        public double WindZ { get; }

        /// <summary>Squared plane distance from the viewer to (x, z).</summary>
        public double DistanceSquaredTo(double x, double z) {
            double dx = x - X, dz = z - Z;
            return dx * dx + dz * dz;
        }
    }
}
