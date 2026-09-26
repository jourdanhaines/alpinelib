using System;
using System.Globalization;

namespace AlpineLib.Procedural.Grid {
    /// <summary>An integer cell address. X grows East, Y grows North; negative values are allowed.</summary>
    public readonly struct CellCoord : IEquatable<CellCoord> {
        /// <summary>Builds a coordinate.</summary>
        public CellCoord(int x, int y) {
            X = x;
            Y = y;
        }

        /// <summary>Column (East is positive).</summary>
        public int X { get; }

        /// <summary>Row (North is positive).</summary>
        public int Y { get; }

        /// <summary>The neighbouring cell towards <paramref name="direction"/>.</summary>
        public CellCoord Offset(GridDirection direction) {
            return this + GridDirections.ToOffset(direction);
        }

        /// <summary>Component-wise sum.</summary>
        public static CellCoord operator +(CellCoord left, CellCoord right) {
            return new CellCoord(left.X + right.X, left.Y + right.Y);
        }

        /// <summary>Component-wise difference.</summary>
        public static CellCoord operator -(CellCoord left, CellCoord right) {
            return new CellCoord(left.X - right.X, left.Y - right.Y);
        }

        /// <summary>Value equality.</summary>
        public static bool operator ==(CellCoord left, CellCoord right) {
            return left.Equals(right);
        }

        /// <summary>Value inequality.</summary>
        public static bool operator !=(CellCoord left, CellCoord right) {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public bool Equals(CellCoord other) {
            return X == other.X && Y == other.Y;
        }

        /// <inheritdoc />
        public override bool Equals(object obj) {
            return obj is CellCoord other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() {
            unchecked {
                return (X * 397) ^ Y;
            }
        }

        /// <summary>"(x,y)", culture-invariant.</summary>
        public override string ToString() {
            return string.Format(CultureInfo.InvariantCulture, "({0},{1})", X, Y);
        }
    }
}
