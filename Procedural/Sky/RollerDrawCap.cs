using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Which of a frame's drawable wind-rolled props are drawn under a cap: one drawn the frame before stays drawn while
    /// it is still drawable (until it fades out or leaves the draw distance), and new ones are admitted in spawn order
    /// while slots are free, so a prop never pops out because an earlier one came into range. Engine-free; one per viewer.
    /// </summary>
    public sealed class RollerDrawCap {
        private readonly List<int> _waiting = new List<int>();
        private readonly List<int> _selected = new List<int>();
        private readonly List<int> _admitted = new List<int>();
        private readonly List<ulong> _dropped = new List<ulong>();
        private HashSet<ulong> _drawn = new HashSet<ulong>();
        private HashSet<ulong> _next = new HashSet<ulong>();
        private IReadOnlyList<RollerSpawn> _candidates;

        /// <summary>Indices into the last candidates of the props drawn, in spawn order.</summary>
        public IReadOnlyList<int> Selected => _selected;

        /// <summary>Indices into the last candidates of the props drawn that were not drawn the frame before.</summary>
        public IReadOnlyList<int> Admitted => _admitted;

        /// <summary>Ids drawn the frame before and not now.</summary>
        public IReadOnlyList<ulong> Dropped => _dropped;

        public int Count => _drawn.Count;

        public bool IsDrawn(ulong id) {
            return _drawn.Contains(id);
        }

        public void Clear() {
            _drawn.Clear();
            _selected.Clear();
            _admitted.Clear();
            _dropped.Clear();
        }

        /// <summary>Selects among <paramref name="candidates"/> (each drawable now, any order) at most <paramref name="most"/>, the ones drawn before first.</summary>
        public void Select(IReadOnlyList<RollerSpawn> candidates, int most) {
            _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
            _selected.Clear();
            _admitted.Clear();
            _dropped.Clear();
            _waiting.Clear();
            _next.Clear();
            for (int index = 0; index < candidates.Count; index++) {
                if (_drawn.Contains(candidates[index].Id) && _selected.Count < most) Take(index);
                else _waiting.Add(index);
            }
            _waiting.Sort(CompareIndices);
            foreach (int index in _waiting) {
                if (_selected.Count >= most) break;

                Take(index);
                _admitted.Add(index);
            }
            foreach (ulong id in _drawn) {
                if (!_next.Contains(id)) _dropped.Add(id);
            }
            _selected.Sort(CompareIndices);
            (_drawn, _next) = (_next, _drawn);
        }

        private void Take(int index) {
            _selected.Add(index);
            _next.Add(_candidates[index].Id);
        }

        private int CompareIndices(int first, int second) {
            int order = RollerSpawner.CompareSpawnOrder(_candidates[first], _candidates[second]);
            return order != 0 ? order : first.CompareTo(second);
        }
    }
}
