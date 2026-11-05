using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// One cell's admission pass carried bucket by bucket (<see cref="RollerSpawner"/>): the end drifts of its admitted
    /// spawns still out and the next bucket to admit. Buckets cover disjoint, rising drifts, so taking each bucket's
    /// candidates in drift order keeps the whole pass in spawn order.
    /// </summary>
    public sealed class RollerSpawnChain {
        private readonly List<double> _outEnds = new List<double>();
        private readonly List<RollerSpawn> _ordered = new List<RollerSpawn>();

        public long NextBucket { get; private set; }

        /// <summary>Admitted spawns still out after the last bucket admitted.</summary>
        public int OutCount => _outEnds.Count;

        public void Restart() {
            NextBucket = 0;
            _outEnds.Clear();
        }

        /// <summary>Admits <paramref name="candidates"/> (the next bucket's) in spawn order into <paramref name="admitted"/> while fewer than <paramref name="cap"/> are out.</summary>
        public void Admit(RollerSpawn[] candidates, int cap, List<RollerSpawn> admitted) {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));

            _ordered.Clear();
            _ordered.AddRange(candidates);
            _ordered.Sort(RollerSpawner.CompareSpawnOrder);
            foreach (RollerSpawn spawn in _ordered) {
                DropEnded(spawn.Drift);
                if (_outEnds.Count >= cap) continue;

                _outEnds.Add(spawn.EndDrift);
                admitted.Add(spawn);
            }
            NextBucket++;
        }

        private void DropEnded(double drift) {
            for (int index = _outEnds.Count - 1; index >= 0; index--) {
                if (_outEnds[index] <= drift) _outEnds.RemoveAt(index);
            }
        }
    }
}
