using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Sky;
using UnityEngine;

namespace AlpineLib.Sky {
    // Moving blockers (a train) against every built prop a RollingPropField keeps, drawn or not, once per session time
    // (and again when the blocker changes):
    // a prop met at its centre or a fade's roll ahead fades out over the blocked fade and is then spent. Train poses are
    // the one input here that is not a pure function of the clock (each client sees its own replicated trains).
    internal sealed class RollingPropBlocking {
        private readonly List<RollingPropEntry> _spentNow = new List<RollingPropEntry>();
        private double _checkedAt = double.NaN;
        private Func<Vector3, float, bool> _checkedWith;

        public void Reset() {
            _checkedAt = double.NaN;
            _checkedWith = null;
        }

        public void Update(RollingPropSpawns spawns, RollingPropCells cells, double drift, double now, double fadeSeconds, Func<Vector3, float, bool> blocker) {
            if ((now == _checkedAt && blocker == _checkedWith) || spawns.Spawner == null) return;

            _checkedAt = now;
            _checkedWith = blocker;
            _spentNow.Clear();
            foreach (RollingPropEntry entry in spawns.Entries) {
                if (!entry.Prop.HasValue) continue;
                if (entry.BlockedAt >= 0.0) {
                    if (now - entry.BlockedAt >= fadeSeconds) _spentNow.Add(entry);
                    continue;
                }
                if (blocker != null && IsMet(entry, cells, drift, spawns.Spawner, blocker)) entry.BlockedAt = now;
            }
            foreach (RollingPropEntry entry in _spentNow) spawns.MarkSpent(entry);
        }

        /// <summary>Share of its size a prop met by a blocker keeps: 1 until met, then down to 0 over the fade.</summary>
        public static float ShareOf(RollingPropEntry entry, double now, double fadeSeconds) {
            if (entry.BlockedAt < 0.0) return 1f;

            return (float)Math.Max(0.0, Math.Min(1.0, 1.0 - (now - entry.BlockedAt) / Math.Max(1e-6, fadeSeconds)));
        }

        private static bool IsMet(RollingPropEntry entry, RollingPropCells cells, double drift, RollerSpawner spawner, Func<Vector3, float, bool> blocker) {
            RollerPath path = entry.Prop.Value.Path;
            double rolled = entry.RolledAt(drift);
            if (rolled < 0.0 || rolled > path.Length) return false;

            float radius = (float)path.Radius;
            double height = path.GroundHeight(rolled) + path.Radius;
            if (TryWorld(path, rolled, height, cells, spawner.CellSize, out Vector3 centre) && blocker(centre, radius)) return true;

            double ahead = rolled + path.GroundSpeedShare * spawner.Paths.FadeDrift;
            return ahead <= path.Length && TryWorld(path, ahead, height, cells, spawner.CellSize, out Vector3 next) && blocker(next, radius);
        }

        // The prop's centre after `rolled` metres at `height` in world space, through the root of the cell it is in.
        private static bool TryWorld(RollerPath path, double rolled, double height, RollingPropCells cells, double cellSize, out Vector3 world) {
            path.GroundPoint(rolled, out double x, out double z);
            if (!cells.TryLocal(x, z, cellSize, out Transform root, out Vector3 local)) {
                world = default;
                return false;
            }

            local.y = (float)height;
            world = root.TransformPoint(local);
            return true;
        }
    }
}
