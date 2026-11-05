using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// The spawns a moving blocker broke up this session, each kept until the drift by which it could no longer be out:
    /// forgetting the props kept (every cell unloading on a respawn) never brings a spent spawn back. Engine-free.
    /// </summary>
    public sealed class RollerSpentSet {
        private readonly Dictionary<ulong, double> _until = new Dictionary<ulong, double>();
        private readonly List<ulong> _stale = new List<ulong>();

        public int Count => _until.Count;

        /// <summary>Marks <paramref name="spawn"/> spent until drift <paramref name="untilDrift"/>.</summary>
        public void Add(RollerSpawn spawn, double untilDrift) {
            _until[spawn.Id] = untilDrift;
        }

        public bool Contains(ulong id) {
            return _until.ContainsKey(id);
        }

        /// <summary>Forgets the spawns that cannot be out at <paramref name="drift"/> or later.</summary>
        public void Prune(double drift) {
            _stale.Clear();
            foreach (KeyValuePair<ulong, double> pair in _until) {
                if (drift >= pair.Value) _stale.Add(pair.Key);
            }
            foreach (ulong id in _stale) _until.Remove(id);
        }

        /// <summary>Forgets every spawn (a new session).</summary>
        public void Clear() {
            _until.Clear();
        }
    }
}
