using System;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>A point of a slab-clipped look face in double precision, for <see cref="PrismBandDecomposer"/>.</summary>
    internal readonly struct SlabPoint : IEquatable<SlabPoint> {
        public SlabPoint(double x, double y, double z) {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public bool Equals(SlabPoint other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        public override bool Equals(object obj) => obj is SlabPoint other && Equals(other);

        public override int GetHashCode() => (X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode();
    }
}
