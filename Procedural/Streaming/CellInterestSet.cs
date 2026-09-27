using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Grid;

namespace AlpineLib.Procedural.Streaming {
    /// <summary>
    /// Which cells should be live: the union of a (2r+1)² square around every observer, with a hysteresis
    /// band so an observer pacing along a cell edge does not load and unload the same row every step.
    /// </summary>
    /// <remarks>
    /// A cell loads once it is within <see cref="Radius"/> (Chebyshev) of any observer and unloads only once
    /// it is beyond <see cref="UnloadRadius"/> of all of them. <see cref="Update"/> reports the change since
    /// the last call in row-major order (Y, then X), so two ends fed the same observers get the same diffs.
    /// </remarks>
    public sealed class CellInterestSet {
        /// <summary>Unload band past the load radius when none is given.</summary>
        public const int DefaultUnloadMargin = 1;

        private readonly int _radius;
        private readonly int _unloadRadius;
        private readonly Dictionary<ulong, CellCoord> _observers = new Dictionary<ulong, CellCoord>();
        private readonly HashSet<CellCoord> _loaded = new HashSet<CellCoord>();

        /// <summary>Loads within <paramref name="radius"/> and unloads one cell further out.</summary>
        public CellInterestSet(int radius) : this(radius, DefaultUnloadMargin) {
        }

        /// <summary>Loads within <paramref name="radius"/> and unloads beyond radius + <paramref name="unloadMargin"/>.</summary>
        public CellInterestSet(int radius, int unloadMargin) {
            if (radius < 0) {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must not be negative.");
            }

            if (unloadMargin < 0) {
                throw new ArgumentOutOfRangeException(nameof(unloadMargin), unloadMargin, "Unload margin must not be negative.");
            }

            _radius = radius;
            _unloadRadius = radius + unloadMargin;
        }

        /// <summary>Chebyshev distance within which an observer loads a cell.</summary>
        public int Radius => _radius;

        /// <summary>Chebyshev distance beyond which a cell with no nearer observer unloads.</summary>
        public int UnloadRadius => _unloadRadius;

        /// <summary>How many observers are registered.</summary>
        public int ObserverCount => _observers.Count;

        /// <summary>Cells live as of the last <see cref="Update"/>, in no particular order.</summary>
        public IReadOnlyCollection<CellCoord> Loaded => _loaded;

        /// <summary>True when the cell was live as of the last <see cref="Update"/>.</summary>
        public bool IsLoaded(CellCoord cell) {
            return _loaded.Contains(cell);
        }

        /// <summary>Adds an observer or moves an existing one. Takes effect on the next <see cref="Update"/>.</summary>
        public void SetObserver(ulong observerId, CellCoord cell) {
            _observers[observerId] = cell;
        }

        /// <summary>Drops an observer. Its cells unload on the next <see cref="Update"/> unless another holds them.</summary>
        public bool RemoveObserver(ulong observerId) {
            return _observers.Remove(observerId);
        }

        /// <summary>The cell an observer was last placed in.</summary>
        public bool TryGetObserver(ulong observerId, out CellCoord cell) {
            return _observers.TryGetValue(observerId, out cell);
        }

        /// <summary>
        /// Applies the observers' current cells and reports what changed, both lists cleared first and
        /// sorted row-major.
        /// </summary>
        public void Update(List<CellCoord> added, List<CellCoord> removed) {
            if (added == null) {
                throw new ArgumentNullException(nameof(added));
            }

            if (removed == null) {
                throw new ArgumentNullException(nameof(removed));
            }

            added.Clear();
            removed.Clear();

            CollectReleased(removed);

            for (int index = 0; index < removed.Count; index++) {
                _loaded.Remove(removed[index]);
            }

            foreach (CellCoord observerCell in _observers.Values) {
                LoadSquare(observerCell, added);
            }

            added.Sort(CompareRowMajor);
            removed.Sort(CompareRowMajor);
        }

        /// <summary>Drops every observer and reports every live cell as removed, sorted row-major.</summary>
        public void Clear(List<CellCoord> removed) {
            if (removed == null) {
                throw new ArgumentNullException(nameof(removed));
            }

            removed.Clear();
            removed.AddRange(_loaded);
            removed.Sort(CompareRowMajor);
            _loaded.Clear();
            _observers.Clear();
        }

        /// <summary>Copies the live cells into <paramref name="cells"/>, cleared first and sorted row-major.</summary>
        public void CopyLoaded(List<CellCoord> cells) {
            if (cells == null) {
                throw new ArgumentNullException(nameof(cells));
            }

            cells.Clear();
            cells.AddRange(_loaded);
            cells.Sort(CompareRowMajor);
        }

        /// <summary>Row-major order: Y ascending, then X ascending — the order <see cref="GridRect.Cells"/> walks.</summary>
        public static int CompareRowMajor(CellCoord left, CellCoord right) {
            int byRow = left.Y.CompareTo(right.Y);
            return byRow != 0 ? byRow : left.X.CompareTo(right.X);
        }

        /// <summary>Chebyshev (king-move) distance between two cells, widened so extreme coordinates cannot overflow.</summary>
        public static long ChebyshevDistance(CellCoord left, CellCoord right) {
            long deltaX = Math.Abs((long)left.X - right.X);
            long deltaY = Math.Abs((long)left.Y - right.Y);
            return Math.Max(deltaX, deltaY);
        }

        private void CollectReleased(List<CellCoord> released) {
            foreach (CellCoord cell in _loaded) {
                if (IsHeldByAnyObserver(cell)) {
                    continue;
                }

                released.Add(cell);
            }
        }

        private bool IsHeldByAnyObserver(CellCoord cell) {
            foreach (CellCoord observerCell in _observers.Values) {
                if (ChebyshevDistance(cell, observerCell) <= _unloadRadius) {
                    return true;
                }
            }

            return false;
        }

        private void LoadSquare(CellCoord center, List<CellCoord> added) {
            GridRect square = CellSpace.Square(center, _radius);

            foreach (CellCoord cell in square.Cells()) {
                if (!_loaded.Add(cell)) {
                    continue;
                }

                added.Add(cell);
            }
        }
    }
}
