using System;
using System.Globalization;

namespace AlpineLib.Maps {
    /// <summary>An absolute map position in metres: <see cref="X"/> east, <see cref="Z"/> north. Double, so a far-out world keeps sub-millimetre precision.</summary>
    public readonly struct MapPoint : IEquatable<MapPoint> {
        /// <summary>Builds a point.</summary>
        public MapPoint(double x, double z) {
            X = x;
            Z = z;
        }

        /// <summary>Metres east.</summary>
        public double X { get; }

        /// <summary>Metres north.</summary>
        public double Z { get; }

        /// <summary>Distance to <paramref name="other"/>, metres.</summary>
        public double DistanceTo(MapPoint other) {
            double deltaX = other.X - X;
            double deltaZ = other.Z - Z;
            return Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        }

        /// <summary>Component-wise sum.</summary>
        public static MapPoint operator +(MapPoint left, MapPoint right) {
            return new MapPoint(left.X + right.X, left.Z + right.Z);
        }

        /// <summary>Component-wise difference.</summary>
        public static MapPoint operator -(MapPoint left, MapPoint right) {
            return new MapPoint(left.X - right.X, left.Z - right.Z);
        }

        /// <summary>Uniform scale.</summary>
        public static MapPoint operator *(MapPoint point, double scale) {
            return new MapPoint(point.X * scale, point.Z * scale);
        }

        /// <summary>Value equality.</summary>
        public static bool operator ==(MapPoint left, MapPoint right) {
            return left.Equals(right);
        }

        /// <summary>Value inequality.</summary>
        public static bool operator !=(MapPoint left, MapPoint right) {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public bool Equals(MapPoint other) {
            return X.Equals(other.X) && Z.Equals(other.Z);
        }

        /// <inheritdoc />
        public override bool Equals(object obj) {
            return obj is MapPoint other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() {
            unchecked {
                return (X.GetHashCode() * 397) ^ Z.GetHashCode();
            }
        }

        /// <summary>"(x,z)", culture-invariant.</summary>
        public override string ToString() {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.###},{1:0.###})", X, Z);
        }
    }
}
