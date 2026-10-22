using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Grid;

namespace AlpineLib.Maps {
    /// <summary>
    /// Slice allocation for one tile level: maps cells to array slices, evicting the least recently
    /// used slice when full. A slice touched in the current or the previous frame is pinned and never
    /// evicted (a capture can land before this frame's draw has touched it), so a full pool refuses
    /// rather than drop a tile on screen.
    /// </summary>
    public class MapTileSlotPool {
        private readonly Dictionary<CellCoord, int> _slotsByCell = new Dictionary<CellCoord, int>();
        private readonly CellCoord[] _cells;
        private readonly bool[] _used;
        private readonly int[] _lastFrame;
        private readonly long[] _lastSequence;
        private long _sequence;

        /// <summary>A pool of <paramref name="capacity"/> slices.</summary>
        public MapTileSlotPool(int capacity) {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Must be positive.");

            Capacity = capacity;
            _cells = new CellCoord[capacity];
            _used = new bool[capacity];
            _lastFrame = new int[capacity];
            _lastSequence = new long[capacity];
        }

        /// <summary>Number of slices.</summary>
        public int Capacity { get; }

        /// <summary>Slices in use.</summary>
        public int Count => _slotsByCell.Count;

        /// <summary>True when <paramref name="cell"/> holds a slice.</summary>
        public bool Contains(CellCoord cell) {
            return _slotsByCell.ContainsKey(cell);
        }

        /// <summary>The slice <paramref name="cell"/> holds, without touching it.</summary>
        public bool TryGet(CellCoord cell, out int slot) {
            return _slotsByCell.TryGetValue(cell, out slot);
        }

        /// <summary>Marks <paramref name="cell"/>'s slice used in <paramref name="frame"/> (pinning it for that frame); false when it holds none.</summary>
        public bool Touch(CellCoord cell, int frame) {
            if (!_slotsByCell.TryGetValue(cell, out int slot)) return false;

            Stamp(slot, frame);
            return true;
        }

        /// <summary>
        /// A slice for <paramref name="cell"/>: its own if it has one, else a free one, else the least
        /// recently used one not pinned in <paramref name="frame"/> or the frame before (<paramref name="evicted"/> names the
        /// cell that lost it). False when every slice is pinned.
        /// </summary>
        public bool TryAcquire(CellCoord cell, int frame, out int slot, out CellCoord? evicted) {
            evicted = null;
            if (_slotsByCell.TryGetValue(cell, out slot)) {
                Stamp(slot, frame);
                return true;
            }

            slot = FindFreeSlot();
            if (slot < 0) slot = FindEvictableSlot(frame);
            if (slot < 0) return false;

            if (_used[slot]) {
                evicted = _cells[slot];
                _slotsByCell.Remove(_cells[slot]);
            }

            _used[slot] = true;
            _cells[slot] = cell;
            _slotsByCell[cell] = slot;
            Stamp(slot, frame);
            return true;
        }

        /// <summary>Frees <paramref name="cell"/>'s slice; false when it held none.</summary>
        public bool Release(CellCoord cell) {
            if (!_slotsByCell.TryGetValue(cell, out int slot)) return false;

            _slotsByCell.Remove(cell);
            _used[slot] = false;
            return true;
        }

        private void Stamp(int slot, int frame) {
            _lastFrame[slot] = frame;
            _lastSequence[slot] = ++_sequence;
        }

        private int FindFreeSlot() {
            for (int slot = 0; slot < Capacity; slot++) {
                if (!_used[slot]) return slot;
            }

            return -1;
        }

        private int FindEvictableSlot(int frame) {
            int best = -1;
            for (int slot = 0; slot < Capacity; slot++) {
                if (_lastFrame[slot] >= frame - 1) continue;
                if (best >= 0 && _lastSequence[slot] >= _lastSequence[best]) continue;

                best = slot;
            }

            return best;
        }
    }
}
