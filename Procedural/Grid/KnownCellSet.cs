using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Grid {
    /// <summary>
    /// A grow-only set of cells (e.g. every cell a player has discovered), with monotonic
    /// <see cref="Bounds"/>, a <see cref="Version"/> stamp and a compact <see cref="CellRunCodec"/> form.
    /// </summary>
    public sealed class KnownCellSet {
        private readonly HashSet<CellCoord> _cells = new HashSet<CellCoord>();
        private readonly List<CellCoord> _added = new List<CellCoord>();
        private int _minX;
        private int _minY;
        private int _maxX;
        private int _maxY;
        private bool _raising;

        /// <summary>
        /// Fires once per <see cref="Add"/>/<see cref="AddRange"/> that added anything, with only the new cells.
        /// The list is reused: copy it to keep it. Handlers must not add cells.
        /// </summary>
        public event Action<IReadOnlyList<CellCoord>> CellsAdded;

        /// <summary>Number of known cells.</summary>
        public int Count => _cells.Count;

        /// <summary>Bumped once per call that added at least one cell; 0 for a set never added to.</summary>
        public long Version { get; private set; }

        /// <summary>
        /// Smallest rect holding every known cell; only ever grows. Empty (0,0 0x0) while the set is empty.
        /// Width and height saturate at <see cref="int.MaxValue"/>.
        /// </summary>
        public GridRect Bounds {
            get {
                if (_cells.Count == 0) {
                    return default;
                }
                int width = (int)Math.Min(int.MaxValue, (long)_maxX - _minX + 1);
                int height = (int)Math.Min(int.MaxValue, (long)_maxY - _minY + 1);
                return new GridRect(_minX, _minY, width, height);
            }
        }

        /// <summary>True when <paramref name="cell"/> is known.</summary>
        public bool Contains(CellCoord cell) {
            return _cells.Contains(cell);
        }

        /// <summary>Adds one cell; returns false when it was already known.</summary>
        public bool Add(CellCoord cell) {
            ThrowIfRaising();
            _added.Clear();
            Insert(cell);
            return Publish() > 0;
        }

        /// <summary>Adds every cell; returns how many were new.</summary>
        public int AddRange(IEnumerable<CellCoord> cells) {
            if (cells == null) {
                throw new ArgumentNullException(nameof(cells));
            }
            ThrowIfRaising();
            _added.Clear();
            foreach (CellCoord cell in cells) {
                Insert(cell);
            }
            return Publish();
        }

        /// <summary>Appends every known cell to <paramref name="destination"/>, in no particular order.</summary>
        public void CopyTo(List<CellCoord> destination) {
            if (destination == null) {
                throw new ArgumentNullException(nameof(destination));
            }
            destination.AddRange(_cells);
        }

        /// <summary>The set in <see cref="CellRunCodec"/> form.</summary>
        public byte[] Serialize() {
            return CellRunCodec.Encode(_cells);
        }

        /// <summary>
        /// Rebuilds a set from <see cref="Serialize"/> output; false on any malformed input or more than
        /// <paramref name="maxCells"/> cells. Never throws on hostile bytes.
        /// </summary>
        public static bool TryDeserialize(ReadOnlySpan<byte> data, int maxCells, out KnownCellSet set) {
            set = null;
            List<CellCoord> cells = new List<CellCoord>();
            if (!CellRunCodec.TryDecode(data, maxCells, cells)) {
                return false;
            }
            set = new KnownCellSet();
            set.AddRange(cells);
            return true;
        }

        private void Insert(CellCoord cell) {
            if (!_cells.Add(cell)) {
                return;
            }
            GrowBounds(cell);
            _added.Add(cell);
        }

        private void GrowBounds(CellCoord cell) {
            if (_cells.Count == 1) {
                _minX = _maxX = cell.X;
                _minY = _maxY = cell.Y;
                return;
            }
            _minX = Math.Min(_minX, cell.X);
            _minY = Math.Min(_minY, cell.Y);
            _maxX = Math.Max(_maxX, cell.X);
            _maxY = Math.Max(_maxY, cell.Y);
        }

        private int Publish() {
            int addedCount = _added.Count;
            if (addedCount == 0) {
                return 0;
            }
            Version++;
            _raising = true;
            try {
                CellsAdded?.Invoke(_added);
            } finally {
                _raising = false;
            }
            return addedCount;
        }

        private void ThrowIfRaising() {
            if (_raising) {
                throw new InvalidOperationException("Cells cannot be added from a CellsAdded handler.");
            }
        }
    }
}
