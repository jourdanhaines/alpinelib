namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// A round stop a wind-rolled prop steers around (a cactus trunk, a boulder, a mesa's foot): its centre in the asking
    /// world's plane and its radius at ground height, metres, without the prop's ball.
    /// </summary>
    public readonly struct RollerDisc {
        public RollerDisc(double x, double z, double radius) {
            X = x;
            Z = z;
            Radius = radius;
        }

        public double X { get; }
        public double Z { get; }
        public double Radius { get; }

        /// <summary>The same disc moved by (<paramref name="offsetX"/>, <paramref name="offsetZ"/>), from a cell's plane to the world's.</summary>
        public RollerDisc Moved(double offsetX, double offsetZ) {
            return new RollerDisc(X + offsetX, Z + offsetZ, Radius);
        }

        /// <summary>Gap between a ball of <paramref name="radius"/> centred over (x, z) and the disc, metres (negative: overlapping).</summary>
        public double ClearanceOf(double x, double z, double radius) {
            double dx = x - X, dz = z - Z;
            return System.Math.Sqrt(dx * dx + dz * dz) - Radius - radius;
        }
    }
}
