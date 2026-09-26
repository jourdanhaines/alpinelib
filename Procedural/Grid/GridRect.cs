using System;
using System.Collections.Generic;
using System.Globalization;

namespace AlpineLib.Procedural.Grid {
    /// <summary>
    /// An axis-aligned block of cells: <see cref="Width"/> columns from <see cref="X"/> and
    /// <see cref="Height"/> rows from <see cref="Y"/>. Max edges are exclusive.
    /// </summary>
    public readonly struct GridRect : IEquatable<GridRect> {
        /// <summary>Builds a rect; width and height may be zero (empty) but not negative.</summary>
        public GridRect(int x, int y, int width, int height) {
            if (width < 0) {
                throw new ArgumentOutOfRangeException(nameof(width), width, "Must not be negative.");
            }
            if (height < 0) {
                throw new ArgumentOutOfRangeException(nameof(height), height, "Must not be negative.");
            }
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>First column.</summary>
        public int X { get; }

        /// <summary>First row.</summary>
        public int Y { get; }

        /// <summary>Number of columns.</summary>
        public int Width { get; }

        /// <summary>Number of rows.</summary>
        public int Height { get; }

        /// <summary>Same as <see cref="X"/>.</summary>
        public int XMin => X;

        /// <summary>Same as <see cref="Y"/>.</summary>
        public int YMin => Y;

        /// <summary>One past the last column.</summary>
        public int XMaxExclusive => X + Width;

        /// <summary>One past the last row.</summary>
        public int YMaxExclusive => Y + Height;

        /// <summary>The lowest cell.</summary>
        public CellCoord Min => new CellCoord(X, Y);

        /// <summary>The highest cell still inside the rect. Meaningless when <see cref="IsEmpty"/>.</summary>
        public CellCoord MaxInclusive => new CellCoord(X + Width - 1, Y + Height - 1);

        /// <summary>True when the rect holds no cells.</summary>
        public bool IsEmpty => Width == 0 || Height == 0;

        /// <summary>Number of cells.</summary>
        public int Area => Width * Height;

        /// <summary>The rect spanning two inclusive corner cells, in either order.</summary>
        public static GridRect FromCorners(CellCoord first, CellCoord second) {
            int xMin = Math.Min(first.X, second.X);
            int yMin = Math.Min(first.Y, second.Y);
            int xMax = Math.Max(first.X, second.X);
            int yMax = Math.Max(first.Y, second.Y);
            return new GridRect(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
        }

        /// <summary>True when <paramref name="cell"/> lies inside.</summary>
        public bool Contains(CellCoord cell) {
            return cell.X >= X && cell.X < XMaxExclusive && cell.Y >= Y && cell.Y < YMaxExclusive;
        }

        /// <summary>True when every cell of <paramref name="other"/> lies inside. An empty rect is contained.</summary>
        public bool Contains(GridRect other) {
            if (other.IsEmpty) {
                return true;
            }
            return other.X >= X && other.XMaxExclusive <= XMaxExclusive
                && other.Y >= Y && other.YMaxExclusive <= YMaxExclusive;
        }

        /// <summary>True when the two rects share at least one cell.</summary>
        public bool Overlaps(GridRect other) {
            return !Intersect(other).IsEmpty;
        }

        /// <summary>The shared cells, or an empty rect when there are none.</summary>
        public GridRect Intersect(GridRect other) {
            int xMin = Math.Max(X, other.X);
            int yMin = Math.Max(Y, other.Y);
            int xMax = Math.Min(XMaxExclusive, other.XMaxExclusive);
            int yMax = Math.Min(YMaxExclusive, other.YMaxExclusive);
            return new GridRect(xMin, yMin, Math.Max(0, xMax - xMin), Math.Max(0, yMax - yMin));
        }

        /// <summary>Grows by <paramref name="amount"/> cells on every side (negative shrinks, clamped to empty).</summary>
        public GridRect Inflate(int amount) {
            return Inflate(amount, amount);
        }

        /// <summary>Grows by <paramref name="amountX"/> left and right and <paramref name="amountY"/> below and above.</summary>
        public GridRect Inflate(int amountX, int amountY) {
            int width = Math.Max(0, Width + 2 * amountX);
            int height = Math.Max(0, Height + 2 * amountY);
            return new GridRect(X - amountX, Y - amountY, width, height);
        }

        /// <summary>Every cell, row by row: Y ascending, then X ascending within a row.</summary>
        public IEnumerable<CellCoord> Cells() {
            for (int y = Y; y < YMaxExclusive; y++) {
                for (int x = X; x < XMaxExclusive; x++) {
                    yield return new CellCoord(x, y);
                }
            }
        }

        /// <summary>Value equality.</summary>
        public static bool operator ==(GridRect left, GridRect right) {
            return left.Equals(right);
        }

        /// <summary>Value inequality.</summary>
        public static bool operator !=(GridRect left, GridRect right) {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public bool Equals(GridRect other) {
            return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        }

        /// <inheritdoc />
        public override bool Equals(object obj) {
            return obj is GridRect other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() {
            unchecked {
                int hash = X;
                hash = (hash * 397) ^ Y;
                hash = (hash * 397) ^ Width;
                return (hash * 397) ^ Height;
            }
        }

        /// <summary>"[x,y wxh]", culture-invariant.</summary>
        public override string ToString() {
            return string.Format(CultureInfo.InvariantCulture, "[{0},{1} {2}x{3}]", X, Y, Width, Height);
        }
    }
}
