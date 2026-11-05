using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Grid;

namespace AlpineLib.Procedural.Streaming {
    /// <summary>
    /// Which far-layer cells draw: a cell draws once it is ready, within <see cref="Radius"/> (Chebyshev) of the
    /// focus cell and not suppressed by its fully built twin. Each drawn cell remembers since when it shows, so a
    /// fade-in can run from there and a built twin taking over can carry on the same fade.
    /// </summary>
    public sealed class FarCellVisibility {
        /// <summary>How long ago a cell counts as shown when its built twin leaves (fully shown at once).</summary>
        public const double ShownLongAgoSeconds = 1e6;

        private readonly Dictionary<CellCoord, double> _shownSince = new Dictionary<CellCoord, double>();
        private readonly HashSet<CellCoord> _ready = new HashSet<CellCoord>();
        private readonly HashSet<CellCoord> _suppressed = new HashSet<CellCoord>();

        public FarCellVisibility(int radius) {
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must not be negative.");

            Radius = radius;
        }

        /// <summary>Chebyshev distance from the focus within which ready cells draw.</summary>
        public int Radius { get; }

        /// <summary>True once a focus cell was set.</summary>
        public bool HasFocus { get; private set; }

        /// <summary>The cell the drawn square is centred on.</summary>
        public CellCoord Focus { get; private set; }

        /// <summary>Cells drawn as of the last <see cref="Refresh"/> (or swap).</summary>
        public int DrawnCount => _shownSince.Count;

        public void SetFocus(CellCoord focus) {
            Focus = focus;
            HasFocus = true;
        }

        /// <summary>The cell's copies are built and may draw.</summary>
        public void MarkReady(CellCoord cell) {
            _ready.Add(cell);
        }

        /// <summary>The cell's copies are gone (dropped or never built): it draws no more.</summary>
        public void Forget(CellCoord cell) {
            _ready.Remove(cell);
            _shownSince.Remove(cell);
        }

        public bool IsReady(CellCoord cell) {
            return _ready.Contains(cell);
        }

        /// <summary>The cell's built twin shows its own copies: this cell stops drawing at once.</summary>
        public void Suppress(CellCoord cell) {
            _suppressed.Add(cell);
            _shownSince.Remove(cell);
        }

        /// <summary>
        /// The built twin is gone: the cell draws again at once, fully shown, when it is ready and in range
        /// (otherwise it fades in later like any other).
        /// </summary>
        public void Unsuppress(CellCoord cell, double now) {
            Unsuppress(cell, now, double.NaN);
        }

        /// <summary>
        /// The built twin is gone: the cell draws again at once, carrying on a fade that began at
        /// <paramref name="shownSince"/> (the twin's own; NaN or later than <paramref name="now"/>: fully shown).
        /// </summary>
        public void Unsuppress(CellCoord cell, double now, double shownSince) {
            if (!_suppressed.Remove(cell)) return;
            if (!IsDrawable(cell)) return;

            _shownSince[cell] = shownSince <= now ? shownSince : now - ShownLongAgoSeconds;
        }

        public bool IsSuppressed(CellCoord cell) {
            return _suppressed.Contains(cell);
        }

        /// <summary>Starts cells that became drawable at <paramref name="now"/> and stops the ones that no longer are.</summary>
        public void Refresh(IEnumerable<CellCoord> cells, double now) {
            foreach (CellCoord cell in cells) {
                bool drawable = IsDrawable(cell);
                bool drawn = _shownSince.ContainsKey(cell);
                if (drawable && !drawn) _shownSince[cell] = now;
                else if (!drawable && drawn) _shownSince.Remove(cell);
            }
        }

        /// <summary>True when the cell draws, with the clock time it began to show.</summary>
        public bool TryGetShownSince(CellCoord cell, out double since) {
            return _shownSince.TryGetValue(cell, out since);
        }

        public bool IsDrawn(CellCoord cell) {
            return _shownSince.ContainsKey(cell);
        }

        /// <summary>Forgets every cell, suppression and the focus.</summary>
        public void Clear() {
            _shownSince.Clear();
            _ready.Clear();
            _suppressed.Clear();
            HasFocus = false;
        }

        private bool IsDrawable(CellCoord cell) {
            if (!HasFocus || !_ready.Contains(cell) || _suppressed.Contains(cell)) return false;

            return CellInterestSet.ChebyshevDistance(cell, Focus) <= Radius;
        }
    }
}
