using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Sky;

namespace AlpineLib.Sky {
    // The spawns a RollingPropField keeps, by id: which are out at the drift (from the spawner, over the registered
    // cells), their builds (new spawns first; rolls cut at the window's edge or provisional rebuilt once the cells
    // settle; a viewer's overrun), the ones a viewer never sees born (born up close) and the ones a moving blocker spent
    // (remembered for the session, so their spawns never come back while the cells reload).
    internal sealed class RollingPropSpawns {
        // Drift metres between spawn refreshes (a prop's first few centimetres are scaled to nothing anyway).
        private const double RefreshDrift = 0.25;

        // Longest forward drift step a refresh adds only the newly appeared spawns over.
        private const double StepDrift = 5.0;

        // Drift metres ahead the next bucket of every cell is worked out.
        private const double PrefetchDrift = 10.0;

        // Metres rolled within which a newly seen spawn counts as being born now.
        private const double YoungRoll = 5.0;

        // Farthest a drawn prop may sit from where its rebuilt roll puts it for the rebuild to be taken, metres.
        private const double ResumeTolerance = 0.05;

        // Refreshes between prunes of the spawner's cache and the spent ids.
        private const int PruneEvery = 64;

        private readonly Dictionary<ulong, RollingPropEntry> _entries = new Dictionary<ulong, RollingPropEntry>();
        private readonly RollerSpentSet _spent = new RollerSpentSet();
        private readonly List<RollerSpawn> _alive = new List<RollerSpawn>();
        private readonly List<ulong> _dropped = new List<ulong>();
        private readonly List<RollingPropBuildResult> _results = new List<RollingPropBuildResult>();
        private readonly RollingPropBuilds _builds = new RollingPropBuilds();
        private RollerSpawner _spawner;
        private IRollerLooks _looks;
        private double _blockReach;
        private double _refreshedDrift = double.NaN;
        private int _refreshedVersion = -1;
        private int _generation;
        private int _refreshes;
        private bool _hasRefreshed;

        public RollerSpawner Spawner => _spawner;
        public RollingPropBuilds Builds => _builds;
        public int Count => _entries.Count;
        public int SpentCount => _spent.Count;
        public Dictionary<ulong, RollingPropEntry>.ValueCollection Entries => _entries.Values;

        public void Configure(RollerSpawner spawner, IRollerLooks looks, double blockReach) {
            _spawner = spawner;
            _looks = looks;
            _blockReach = Math.Max(0.0, blockReach);
            Forget();
            _spent.Clear();
            _builds.Configure(spawner, looks);
        }

        /// <summary>Drops every spawn kept (every cell left); the spent ones stay spent.</summary>
        public void Forget() {
            _entries.Clear();
            _builds.Clear();
            _generation++;
            _refreshedDrift = double.NaN;
            _refreshedVersion = -1;
            _hasRefreshed = false;
        }

        public bool TryGet(ulong id, out RollingPropEntry entry) {
            return _entries.TryGetValue(id, out entry);
        }

        public bool IsSpent(ulong id) {
            return _spent.Contains(id);
        }

        /// <summary>A moving blocker's fade is over: the spawn is dropped and never comes back this session.</summary>
        public void MarkSpent(RollingPropEntry entry) {
            _spent.Add(entry.Spawn, entry.Spawn.Drift + (entry.Spawn.Life + _spawner.Spawn.MostOverrun) / entry.Spawn.GroundSpeedShare);
            _entries.Remove(entry.Spawn.Id);
        }

        // Takes finished builds in; once the drift moved on or the cells changed, adds the spawns out now, drops the
        // gone ones and queues the builds still needed.
        public void Refresh(RollingPropCells cells, double drift, RollingPropViewer viewer, double now, RollingPropCap cap) {
            TakeResults(drift, now, cap);
            if (_spawner == null || _looks == null) return;

            bool cellsChanged = cells.Version != _refreshedVersion;
            if (!cellsChanged && !double.IsNaN(_refreshedDrift) && Math.Abs(drift - _refreshedDrift) < RefreshDrift) return;

            RollerCellWorld world = cells.WorldOf(_spawner.CellSize, _blockReach);
            bool isStep = !cellsChanged && !double.IsNaN(_refreshedDrift) && drift > _refreshedDrift && drift - _refreshedDrift < StepDrift;
            AddAlive(cells, drift, isStep ? _refreshedDrift : double.NaN, viewer);
            DropGone(cells, drift);
            QueueBuilds(world, cells.Version, !cellsChanged, drift, viewer);
            if (cellsChanged || ++_refreshes % PruneEvery == 0) Prune(cells, drift);
            _refreshedDrift = drift;
            _refreshedVersion = cells.Version;
        }

        /// <summary>Starts the worker on the builds queued (inline when synchronous); the field asks once its frame's work is done.</summary>
        public void Kick(double drift, double now, RollingPropCap cap) {
            _builds.Kick();
            if (_builds.IsSynchronous) TakeResults(drift, now, cap);
        }

        /// <summary>Waits for every queued build and takes the results in (gates).</summary>
        public void Complete(double drift, double now, RollingPropCap cap) {
            _builds.Complete();
            TakeResults(drift, now, cap);
        }

        // Every spawn out now (after a change of cells or a jump of the drift), or only those that appeared since the
        // last refresh (`after`, a small forward step).
        private void AddAlive(RollingPropCells cells, double drift, double after, RollingPropViewer viewer) {
            _alive.Clear();
            foreach ((long X, long Z) cell in cells.Keys) {
                if (double.IsNaN(after)) _spawner.AliveAt(cell.X, cell.Z, drift, _alive);
                else _spawner.AppearedBetween(cell.X, cell.Z, after, drift, _alive);
                _spawner.Prefetch(cell.X, cell.Z, drift + PrefetchDrift);
            }

            foreach (RollerSpawn spawn in _alive) {
                if (_entries.ContainsKey(spawn.Id) || _spent.Contains(spawn.Id)) continue;

                _entries.Add(spawn.Id, new RollingPropEntry(spawn) { IsHidden = _hasRefreshed && IsBornInView(spawn, drift, viewer) });
            }
            _hasRefreshed |= cells.Count > 0;
        }

        // Born just now in a cell the viewer sees up close: neither far off nor well upwind of it.
        private bool IsBornInView(RollerSpawn spawn, double drift, RollingPropViewer viewer) {
            if (!viewer.HasPlane || spawn.RolledAt(drift) > YoungRoll) return false;

            double centreX = RollerCellWorld.CentreOf(spawn.CellX, _spawner.CellSize);
            double centreZ = RollerCellWorld.CentreOf(spawn.CellZ, _spawner.CellSize);
            double upwind = (viewer.X - centreX) * viewer.WindX + (viewer.Z - centreZ) * viewer.WindZ;
            if (upwind >= _spawner.Spawn.VisibleBirthUpwind) return false;

            double radius = _spawner.Spawn.HiddenBirthRadius;
            return viewer.DistanceSquaredTo(centreX, centreZ) <= radius * radius;
        }

        private void DropGone(RollingPropCells cells, double drift) {
            _dropped.Clear();
            foreach (KeyValuePair<ulong, RollingPropEntry> pair in _entries) {
                if (IsGone(pair.Value, cells, drift)) _dropped.Add(pair.Key);
            }
            foreach (ulong id in _dropped) _entries.Remove(id);
        }

        // Past its roll (or its whole life unbuilt), waiting on an unloaded spawn cell, or out of the loaded window.
        private bool IsGone(RollingPropEntry entry, RollingPropCells cells, double drift) {
            RollerSpawn spawn = entry.Spawn;
            if (drift < spawn.Drift || drift >= entry.EndDrift) return true;
            if (!entry.Prop.HasValue) return !entry.IsBuilt && !cells.Contains(spawn.CellX, spawn.CellZ);

            entry.Prop.Value.Path.GroundPoint(spawn.RolledAt(drift), out double x, out double z);
            return !cells.IsLoadedAt(x, z, _spawner.CellSize);
        }

        // Unbuilt spawns first; once the cells settled, rolls cut at the window's edge or built blind of a neighbour; and
        // rolls whose life would end within the viewer's draw distance, carried on.
        private void QueueBuilds(RollerCellWorld world, int version, bool isSettled, double drift, RollingPropViewer viewer) {
            foreach (RollingPropEntry entry in _entries.Values) {
                if (entry.IsQueued) continue;
                if (!entry.IsBuilt) {
                    Enqueue(entry, world, version, false);
                    continue;
                }
                if (!entry.Prop.HasValue) continue;

                RollerPath path = entry.Prop.Value.Path;
                if (isSettled && entry.BuiltVersion != version && (path.End == RollerEnd.Edge || path.IsProvisional)) {
                    Enqueue(entry, world, version, true);
                    continue;
                }
                if (!NeedsOverrun(entry, path, drift, viewer)) continue;

                entry.Overrun += _spawner.Spawn.OverrunStep;
                Enqueue(entry, world, version, true);
            }
        }

        private void Enqueue(RollingPropEntry entry, RollerCellWorld world, int version, bool isRebuild) {
            entry.IsQueued = true;
            double life = entry.Spawn.Life + entry.Overrun;
            _builds.Enqueue(new RollingPropBuildRequest(entry.Spawn, world, life, version, _generation, isRebuild));
        }

        // A life end near the viewer, close ahead, with overrun left.
        private bool NeedsOverrun(RollingPropEntry entry, RollerPath path, double drift, RollingPropViewer viewer) {
            RollerSpawnSettings settings = _spawner.Spawn;
            if (entry.IsHidden || !viewer.HasPlane || path.End != RollerEnd.Life || !(settings.OverrunStep > 0.0)) return false;
            if (entry.Overrun + settings.OverrunStep > settings.MostOverrun + 1e-9) return false;
            if (entry.RolledAt(drift) < path.Length - settings.OverrunLead) return false;

            path.GroundPoint(path.Length, out double endX, out double endZ);
            double reach = viewer.DrawDistance + settings.OverrunLead;
            return viewer.DistanceSquaredTo(endX, endZ) <= reach * reach;
        }

        private void TakeResults(double drift, double now, RollingPropCap cap) {
            _results.Clear();
            _builds.Drain(_results);
            foreach (RollingPropBuildResult result in _results) {
                if (result.Generation != _generation || !_entries.TryGetValue(result.Id, out RollingPropEntry entry)) continue;

                entry.IsQueued = false;
                entry.IsBuilt = true;
                entry.BuiltVersion = result.Version;
                if (!entry.Prop.HasValue) {
                    entry.Prop = result.Prop;
                    continue;
                }
                if (!result.Prop.HasValue) continue;
                if (!TryAdopt(entry, result.Prop.Value, drift, now, cap.IsDrawn(result.Id))) entry.Overrun = _spawner.Spawn.MostOverrun;
            }
        }

        // A rebuilt roll replaces a drawn one only where it puts the prop where it is now, resuming from its current scale.
        private static bool TryAdopt(RollingPropEntry entry, RollingProp rebuilt, double drift, double now, bool isDrawn) {
            RollerPath old = entry.Prop.Value.Path;
            RollerPath path = rebuilt.Path;
            double rolled = Math.Max(0.0, entry.RolledAt(drift));
            if (isDrawn) {
                old.GroundPoint(rolled, out double oldX, out double oldZ);
                path.GroundPoint(rolled, out double newX, out double newZ);
                double dx = newX - oldX, dz = newZ - oldZ;
                if (dx * dx + dz * dz > ResumeTolerance * ResumeTolerance) return false;

                double before = old.FadeAt(rolled) * RollingPropCap.ShownShare(entry, now);
                double after = path.FadeAt(rolled);
                if (after > before + 1e-6) {
                    entry.ShownFrom = after > 0.0 ? before / after : 0.0;
                    entry.ShownAt = now;
                }
            }
            entry.Prop = rebuilt;
            return true;
        }

        private void Prune(RollingPropCells cells, double drift) {
            _spawner.Prune(drift, cells.Contains);
            _spent.Prune(drift);
        }
    }
}
