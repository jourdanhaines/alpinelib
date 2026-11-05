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
            for (int column = Math.Min(0, stepX); column <= Math.Max(0, stepX); column++) {
                if (IsBlockedInColumn(x, z, radius, cellX + column, cellZ, column == 0, stepZ)) return true;
            }
            return false;
        }

        // The neighbours in one column of cells (the point's own cell skipped) that stop the ball.
        private bool IsBlockedInColumn(double x, double z, double radius, long cellX, long cellZ, bool isOwnColumn, int stepZ) {
            for (int row = Math.Min(0, stepZ); row <= Math.Max(0, stepZ); row++) {
                if ((isOwnColumn && row == 0) || !_cells.TryGetValue((cellX, cellZ + row), out IRollerCell neighbour)) continue;
                if (neighbour.BlockAt(x - CentreOf(cellX, CellSize), z - CentreOf(cellZ + row, CellSize), radius) == RollerBlock.Stop) return true;
            }
            return false;
        }

        /// <summary>
        /// The round stops of every cell whose stops may reach within <paramref name="reach"/> of the point (its own and
        /// the neighbours within <paramref name="reach"/> plus <see cref="BlockReach"/>), in absolute coordinates; false
        /// when one of those cells is not loaded.
        /// </summary>
        public bool StopsNear(double x, double z, double radius, double reach, List<RollerDisc> into) {
            if (into == null) throw new ArgumentNullException(nameof(into));

            double span = Math.Max(0.0, reach) + BlockReach + radius;
            CellOf(x - span, z - span, CellSize, out long fromX, out long fromZ);
            CellOf(x + span, z + span, CellSize, out long toX, out long toZ);
            bool isComplete = true;
            for (long cellX = fromX; cellX <= toX; cellX++) {
                for (long cellZ = fromZ; cellZ <= toZ; cellZ++) isComplete &= AddStopsOf(cellX, cellZ, x, z, radius, reach, into);
            }
            return isComplete;
        }

        // One cell's round stops near absolute (x, z), moved into the world's plane; false when the cell is not loaded.
        private bool AddStopsOf(long cellX, long cellZ, double x, double z, double radius, double reach, List<RollerDisc> into) {
            if (!_cells.TryGetValue((cellX, cellZ), out IRollerCell cell)) return false;

            double centreX = CentreOf(cellX, CellSize), centreZ = CentreOf(cellZ, CellSize);
            int first = into.Count;
            cell.StopsNear(x - centreX, z - centreZ, radius, reach, into);
            for (int index = first; index < into.Count; index++) into[index] = into[index].Moved(centreX, centreZ);

            return true;
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
