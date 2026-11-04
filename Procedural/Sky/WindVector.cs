using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>A horizontal vector in double precision: x East, z North (metres, or metres per second).</summary>
    public readonly struct WindVector : IEquatable<WindVector> {
        public WindVector(double x, double z) {
            X = x;
            Z = z;
        }

        public double X { get; }
        public double Z { get; }

        /// <summary>Length of the vector.</summary>
        public double Length => Math.Sqrt(X * X + Z * Z);

        public static WindVector Zero => default;

        public static WindVector operator +(WindVector left, WindVector right) {
            return new WindVector(left.X + right.X, left.Z + right.Z);
        }

        public static WindVector operator -(WindVector left, WindVector right) {
            return new WindVector(left.X - right.X, left.Z - right.Z);
        }

        public static WindVector operator *(WindVector vector, double scale) {
            return new WindVector(vector.X * scale, vector.Z * scale);
        }

        /// <summary>Bit-for-bit equality of both components.</summary>
        public bool Equals(WindVector other) {
            return BitConverter.DoubleToInt64Bits(X) == BitConverter.DoubleToInt64Bits(other.X)
                && BitConverter.DoubleToInt64Bits(Z) == BitConverter.DoubleToInt64Bits(other.Z);
        }

        public override bool Equals(object obj) {
            return obj is WindVector other && Equals(other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(X, Z);
        }

        public override string ToString() {
            return $"({X:0.###}, {Z:0.###})";
        }
    }
}
