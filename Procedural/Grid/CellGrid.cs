using System;

namespace AlpineLib.Procedural.Grid {
    /// <summary>
    /// A dense value per cell over a fixed <see cref="GridRect"/>. The bounds may start at negative
    /// coordinates; cells outside them are rejected rather than silently grown into.
    /// </summary>
    public sealed class CellGrid<T> {
        private readonly T[] _cells;

        /// <summary>Allocates a grid over <paramref name="bounds"/>, every cell at <c>default(T)</c>.</summary>
        public CellGrid(GridRect bounds) {
            Bounds = bounds;
            _cells = new T[bounds.Area];
        }

        /// <summary>The cells this grid covers.</summary>
        public GridRect Bounds { get; }

        /// <summary>Reads or writes one cell; throws outside <see cref="Bounds"/>.</summary>
        public T this[CellCoord cell] {
            get => _cells[IndexOf(cell)];
            set => _cells[IndexOf(cell)] = value;
        }

        /// <summary>True when <paramref name="cell"/> is inside <see cref="Bounds"/>.</summary>
        public bool Contains(CellCoord cell) {
            return Bounds.Contains(cell);
        }

        /// <summary>Reads a cell, or returns false (and <c>default</c>) outside <see cref="Bounds"/>.</summary>
        public bool TryGet(CellCoord cell, out T value) {
            if (!Bounds.Contains(cell)) {
                value = default;
                return false;
            }
            value = _cells[RawIndex(cell)];
            return true;
        }

        /// <summary>Writes one cell; throws outside <see cref="Bounds"/>.</summary>
        public void Set(CellCoord cell, T value) {
            _cells[IndexOf(cell)] = value;
        }

        /// <summary>Writes every cell of <paramref name="area"/>, which must lie inside <see cref="Bounds"/>.</summary>
        public void Fill(GridRect area, T value) {
            if (!Bounds.Contains(area)) {
                throw new ArgumentOutOfRangeException(nameof(area), area, $"Not inside grid bounds {Bounds}.");
            }
            foreach (CellCoord cell in area.Cells()) {
                _cells[RawIndex(cell)] = value;
            }
        }

        /// <summary>
        /// True when every cell of <paramref name="area"/> is inside the grid and satisfies
        /// <paramref name="predicate"/>. An area reaching outside the grid is false.
        /// </summary>
        public bool All(GridRect area, Func<T, bool> predicate) {
            if (predicate == null) {
                throw new ArgumentNullException(nameof(predicate));
            }
            if (!Bounds.Contains(area)) {
                return false;
            }
            foreach (CellCoord cell in area.Cells()) {
                if (!predicate(_cells[RawIndex(cell)])) {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Number of cells in the whole grid satisfying <paramref name="predicate"/>.</summary>
        public int Count(Func<T, bool> predicate) {
            if (predicate == null) {
                throw new ArgumentNullException(nameof(predicate));
            }
            int count = 0;
            for (int index = 0; index < _cells.Length; index++) {
                if (predicate(_cells[index])) {
                    count++;
                }
            }
            return count;
        }

        private int IndexOf(CellCoord cell) {
            if (!Bounds.Contains(cell)) {
                throw new ArgumentOutOfRangeException(nameof(cell), cell, $"Outside grid bounds {Bounds}.");
            }
            return RawIndex(cell);
        }

        private int RawIndex(CellCoord cell) {
            return (cell.Y - Bounds.Y) * Bounds.Width + (cell.X - Bounds.X);
        }
    }
}
