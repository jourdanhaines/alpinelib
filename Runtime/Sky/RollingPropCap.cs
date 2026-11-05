using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Sky;

namespace AlpineLib.Sky {
    // Which of a RollingPropField's candidates are drawn (RollerDrawCap: one drawn stays drawn while it is a candidate,
    // new ones wait for a free slot) and their ramps: a prop admitted at full size ramps in, and its ramp starts again
    // whenever it is dropped and admitted anew.
    internal sealed class RollingPropCap {
        private readonly RollerDrawCap _cap = new RollerDrawCap();
        private readonly List<RollerSpawn> _spawns = new List<RollerSpawn>();
        private readonly List<RollingPropDrawn> _selected = new List<RollingPropDrawn>();
        private readonly Dictionary<ulong, RollingPropEntry> _drawnEntries = new Dictionary<ulong, RollingPropEntry>();
        private readonly Dictionary<ulong, RollingPropEntry> _nextEntries = new Dictionary<ulong, RollingPropEntry>();

        /// <summary>The props drawn by the last <see cref="Select"/>, in spawn order.</summary>
        public List<RollingPropDrawn> Selected => _selected;

        public int Count => _cap.Count;

        public bool IsDrawn(ulong id) {
            return _cap.IsDrawn(id);
        }

        public void Clear() {
            foreach (RollingPropEntry entry in _drawnEntries.Values) Hide(entry);

            _drawnEntries.Clear();
            _cap.Clear();
            _selected.Clear();
        }

        /// <summary>Draws at most <paramref name="most"/> of <paramref name="candidates"/>, ramping the newly admitted ones and resetting every dropped one.</summary>
        public void Select(List<RollingPropDrawn> candidates, int most, double now, bool isFresh) {
            _spawns.Clear();
            foreach (RollingPropDrawn candidate in candidates) _spawns.Add(candidate.Entry.Spawn);

            _cap.Select(_spawns, most);
            foreach (int index in _cap.Admitted) Ramp(candidates[index], now, isFresh);
            foreach (ulong id in _cap.Dropped) {
                if (_drawnEntries.TryGetValue(id, out RollingPropEntry entry)) Hide(entry);
            }
            _selected.Clear();
            _nextEntries.Clear();
            foreach (int index in _cap.Selected) {
                _selected.Add(candidates[index]);
                _nextEntries[candidates[index].Entry.Spawn.Id] = candidates[index].Entry;
            }
            _drawnEntries.Clear();
            foreach (KeyValuePair<ulong, RollingPropEntry> pair in _nextEntries) _drawnEntries.Add(pair.Key, pair.Value);
        }

        /// <summary>Share of its size the prop's ramp has reached: 1 when it needs none or is not drawn.</summary>
        public static float ShownShare(RollingPropEntry entry, double now) {
            if (double.IsNaN(entry.ShownAt) || double.IsNegativeInfinity(entry.ShownAt)) return 1f;

            double share = Math.Max(0.0, Math.Min(1.0, (now - entry.ShownAt) / RollingPropField.ShowSeconds));
            return (float)(entry.ShownFrom + (1.0 - entry.ShownFrom) * share);
        }

        // A prop first drawn while still scaling in, or on the first draw after a (re)configure, needs no ramp.
        private static void Ramp(RollingPropDrawn candidate, double now, bool isFresh) {
            RollingPropEntry entry = candidate.Entry;
            entry.ShownAt = isFresh || candidate.Pose.Scale < 1.0 ? double.NegativeInfinity : now;
            entry.ShownFrom = 0.0;
        }

        private static void Hide(RollingPropEntry entry) {
            entry.ShownAt = double.NaN;
            entry.ShownFrom = 0.0;
        }
    }
}
