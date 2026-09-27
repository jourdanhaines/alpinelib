using System;
using AlpineLib.Procedural.Grid;

namespace AlpineLib.Procedural.Streaming {
    /// <summary>
    /// Maps world XZ positions (metres, double) to square cells of a fixed size and back. World X is
    /// cell X (East); world Z is cell Y (North).
    /// </summary>
    /// <remarks>
    /// <see cref="OriginCell"/> is the cell whose min corner sits at world (0, 0), so a floating origin
    /// that rebases by whole cells swaps the origin and keeps every cell address stable.
    /// </remarks>
    public readonly struct CellSpace : IEquatable<CellSpace> {
        /// <summary>A space of <paramref name="cellSize"/>-metre cells with cell (0,0) at world (0,0).</summary>
        public CellSpace(double cellSize) : this(cellSize, default) {
        }

        /// <summary>A space of <paramref name="cellSize"/>-metre cells with <paramref name="originCell"/> at world (0,0).</summary>
        public CellSpace(double cellSize, CellCoord originCell) {
            if (!(cellSize > 0.0) || double.IsInfinity(cellSize)) {
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be positive and finite.");
            }

            CellSize = cellSize;
            OriginCell = originCell;
        }

        /// <summary>Edge length of one cell, in metres.</summary>
        public double CellSize { get; }

        /// <summary>The cell whose min corner is world (0, 0).</summary>
        public CellCoord OriginCell { get; }

        /// <summary>The same cells, re-anchored so <paramref name="originCell"/> sits at world (0, 0).</summary>
        public CellSpace WithOrigin(CellCoord originCell) {
            return new CellSpace(CellSize, originCell);
        }

        /// <summary>The cell containing world (x, z). Cell min edges are inclusive, max edges exclusive.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The position is not finite or its cell overflows an int.</exception>
        public CellCoord CellOf(double x, double z) {
            return new CellCoord(ToCellIndex(x, OriginCell.X, nameof(x)), ToCellIndex(z, OriginCell.Y, nameof(z)));
        }

        /// <summary>World X of the cell's West edge.</summary>
        public double MinX(CellCoord cell) {
            return ((long)cell.X - OriginCell.X) * CellSize;
        }

        /// <summary>World Z of the cell's South edge.</summary>
        public double MinZ(CellCoord cell) {
            return ((long)cell.Y - OriginCell.Y) * CellSize;
        }

        /// <summary>World X of the cell's centre.</summary>
        public double CenterX(CellCoord cell) {
            return MinX(cell) + CellSize * 0.5;
        }

        /// <summary>World Z of the cell's centre.</summary>
        public double CenterZ(CellCoord cell) {
            return MinZ(cell) + CellSize * 0.5;
        }

        /// <summary>World (x, z) expressed relative to the min corner of <paramref name="cell"/>.</summary>
        public void ToCellLocal(CellCoord cell, double x, double z, out double localX, out double localZ) {
            localX = x - MinX(cell);
            localZ = z - MinZ(cell);
        }

        /// <summary>Cells within Chebyshev <paramref name="radius"/> of <paramref name="center"/>: a (2r+1)² square.</summary>
        public static GridRect Square(CellCoord center, int radius) {
            if (radius < 0) {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must not be negative.");
            }

            int side = radius * 2 + 1;
            return new GridRect(center.X - radius, center.Y - radius, side, side);
        }

        /// <inheritdoc />
        public bool Equals(CellSpace other) {
            return CellSize.Equals(other.CellSize) && OriginCell == other.OriginCell;
        }

        /// <inheritdoc />
        public override bool Equals(object obj) {
            return obj is CellSpace other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() {
            unchecked {
                return (CellSize.GetHashCode() * 397) ^ OriginCell.GetHashCode();
            }
        }

        private int ToCellIndex(double coordinate, int originIndex, string parameterName) {
            if (double.IsNaN(coordinate) || double.IsInfinity(coordinate)) {
                throw new ArgumentOutOfRangeException(parameterName, coordinate, "Position must be finite.");
            }

            double index = Math.Floor(coordinate / CellSize) + originIndex;

            if (index < int.MinValue || index > int.MaxValue) {
                throw new ArgumentOutOfRangeException(parameterName, coordinate, "Position lies outside the addressable cell range.");
            }

            return (int)index;
        }
    }
}
