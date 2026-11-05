using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// An <see cref="IRollerWorld"/> over a snapshot of streamed cells: absolute (x, z) lies in cell
    /// (floor(x / size), floor(z / size)) at local (x, z) less the cell's centre; outside every cell is the
    /// <see cref="RollerBlock.Edge"/>. A point within <see cref="BlockReach"/> of a border is also tested against the
    /// neighbours' blocks (a mesa standing across the border stops a prop on either side). Immutable once built, so a
    /// worker may roll props over it while cells stream.
    /// </summary>
    public sealed class RollerCellWorld : IRollerWorld {
        private readonly Dictionary<(long X, long Z), IRollerCell> _cells;

        /// <summary>
        /// A world of <paramref name="cells"/> (copied) of <paramref name="cellSize"/> metres whose blocks reach at most
        /// <paramref name="blockReach"/> metres past their cell's border.
        /// </summary>
        public RollerCellWorld(double cellSize, IEnumerable<KeyValuePair<(long X, long Z), IRollerCell>> cells, double blockReach = 0.0) {
            if (!(cellSize > 0.0)) throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Must be positive.");
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            if (!(blockReach >= 0.0)) throw new ArgumentOutOfRangeException(nameof(blockReach), blockReach, "Must not be negative.");

            CellSize = cellSize;
            BlockReach = Math.Min(blockReach, cellSize * 0.5);
            _cells = new Dictionary<(long X, long Z), IRollerCell>();
            foreach (KeyValuePair<(long X, long Z), IRollerCell> cell in cells) {
                if (cell.Value != null) _cells[cell.Key] = cell.Value;
            }
        }

        public double CellSize { get; }

        /// <summary>Farthest past its border a cell's block may reach, metres.</summary>
        public double BlockReach { get; }

        public int CellCount => _cells.Count;

        public bool Contains(long cellX, long cellZ) {
            return _cells.ContainsKey((cellX, cellZ));
        }

        /// <summary>The cell absolute (<paramref name="x"/>, <paramref name="z"/>) lies in.</summary>
        public static void CellOf(double x, double z, double cellSize, out long cellX, out long cellZ) {
            cellX = (long)Math.Floor(x / cellSize);
            cellZ = (long)Math.Floor(z / cellSize);
        }

        /// <summary>Absolute coordinate of a cell's centre along one axis.</summary>
        public static double CentreOf(long cell, double cellSize) {
            return (cell + 0.5) * cellSize;
        }

        public RollerBlock BlockAt(double x, double z, double radius) {
            if (!TryCell(x, z, out IRollerCell cell, out double localX, out double localZ)) return RollerBlock.Edge;

            RollerBlock own = cell.BlockAt(localX, localZ, radius);
            if (own != RollerBlock.None || BlockReach <= 0.0) return own;

            return IsBlockedByNeighbour(x, z, localX, localZ, radius) ? RollerBlock.Stop : RollerBlock.None;
        }

        // The neighbours whose border lies within the block reach of (x, z), tested in their own planes.
        private bool IsBlockedByNeighbour(double x, double z, double localX, double localZ, double radius) {
            double inner = CellSize * 0.5 - BlockReach - radius;
            int stepX = localX > inner ? 1 : localX < -inner ? -1 : 0;
            int stepZ = localZ > inner ? 1 : localZ < -inner ? -1 : 0;
            if (stepX == 0 && stepZ == 0) return false;

            CellOf(x, z, CellSize, out long cellX, out long cellZ);
            for (int dx = Math.Min(0, stepX); dx <= Math.Max(0, stepX); dx++) {
                if (IsBlockedInColumn(x, z, radius, cellX + dx, cellZ, dx == 0, stepZ)) return true;
            }
            return false;
        }

        // The neighbours in one column of cells (the point's own cell skipped) that stop the ball.
        private bool IsBlockedInColumn(double x, double z, double radius, long cellX, long cellZ, bool isOwnColumn, int stepZ) {
            for (int dz = Math.Min(0, stepZ); dz <= Math.Max(0, stepZ); dz++) {
                if ((isOwnColumn && dz == 0) || !_cells.TryGetValue((cellX, cellZ + dz), out IRollerCell neighbour)) continue;
                if (neighbour.BlockAt(x - CentreOf(cellX, CellSize), z - CentreOf(cellZ + dz, CellSize), radius) == RollerBlock.Stop) return true;
            }
            return false;
        }

        public RollerSupport SupportAt(double x, double z, double radius) {
            if (!TryCell(x, z, out IRollerCell cell, out double localX, out double localZ)) return RollerSupport.Level(0.0);

            return cell.SupportAt(localX, localZ, radius);
        }

        public double SurfaceAt(double x, double z) {
            if (!TryCell(x, z, out IRollerCell cell, out double localX, out double localZ)) return double.NegativeInfinity;

            return cell.SurfaceAt(localX, localZ);
        }

        private bool TryCell(double x, double z, out IRollerCell cell, out double localX, out double localZ) {
            CellOf(x, z, CellSize, out long cellX, out long cellZ);
            localX = x - CentreOf(cellX, CellSize);
            localZ = z - CentreOf(cellZ, CellSize);
            return _cells.TryGetValue((cellX, cellZ), out cell);
        }
    }
}
