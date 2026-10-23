using System;
using System.Collections.Generic;
using System.Diagnostics;
using AlpineLib.Procedural.Grid;
using Unity.Profiling;

namespace AlpineLib.Maps {
    /// <summary>
    /// Decides which cells get map tiles and drives their bakes: every known or extra cell without a tile,
    /// cheapest source first and nearest the focus first (<see cref="MapBakeOrder"/>), prioritised cells
    /// ahead of all. Each <see cref="Tick"/> steps the active jobs within a main-thread budget and captures
    /// the ready ones — more while the map is on screen, less (and only every few frames) while hidden.
    /// Inert on a null graphics device.
    /// </summary>
    public sealed class MapBakeQueue : IDisposable {
        private static readonly ProfilerMarker TickMarker = new ProfilerMarker("MapBakeQueue.Tick");
        private static readonly ProfilerMarker ResortMarker = new ProfilerMarker("MapBakeQueue.Resort");
        private static readonly ProfilerMarker StepMarker = new ProfilerMarker("MapBakeQueue.StepJob");
        private static readonly ProfilerMarker CaptureMarker = new ProfilerMarker("MapBakeQueue.Capture");

        private readonly HashSet<CellCoord> _extra = new HashSet<CellCoord>();
        private readonly HashSet<CellCoord> _baked = new HashSet<CellCoord>();
        private readonly List<CellCoord> _priority = new List<CellCoord>();
        private readonly HashSet<CellCoord> _forced = new HashSet<CellCoord>();
        private readonly List<(CellCoord cell, MapBakeCost cost)> _pending = new List<(CellCoord cell, MapBakeCost cost)>();
        private readonly List<IMapBakeJob> _active = new List<IMapBakeJob>();
        private readonly List<CellCoord> _scratch = new List<CellCoord>();
        private readonly HashSet<CellCoord> _candidates = new HashSet<CellCoord>();
        private readonly Comparison<(CellCoord cell, MapBakeCost cost)> _comparePending;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private KnownCellSet _known;
        private MapTileStore _store;
        private MapTileFillSink _fillSink;
        private MapTileCapturer _capturer;
        private IMapCellBakeSource[] _sources = Array.Empty<IMapCellBakeSource>();
        private CellCoord _focus;
        private int _pendingCursor;
        private long _sortedKnownVersion = -1;
        private float _lastSortTime = float.NegativeInfinity;
        private bool _dirty = true;

        public MapBakeQueue() {
            _comparePending = ComparePending;
        }

        /// <summary>Raised after a cell's tile was captured.</summary>
        public event Action<CellCoord> CellBaked;

        /// <summary>Main-thread milliseconds per tick while the map is visible.</summary>
        public float VisibleBudgetMilliseconds { get; set; } = 4f;

        /// <summary>Captures per tick while the map is visible.</summary>
        public int VisibleCapturesPerTick { get; set; } = 2;

        /// <summary>Main-thread milliseconds per working tick while the map is hidden.</summary>
        public float HiddenBudgetMilliseconds { get; set; } = 2f;

        /// <summary>Captures per working tick while the map is hidden.</summary>
        public int HiddenCapturesPerTick { get; set; } = 1;

        /// <summary>While hidden, only every this many frames does work.</summary>
        public int HiddenFrameInterval { get; set; } = 4;

        /// <summary>
        /// Asked first on every tick; while it returns true the queue does nothing at all (no job started,
        /// stepped or captured), so higher-priority main-thread work such as cell streaming owns the frame.
        /// </summary>
        public Func<bool> ShouldYield { get; set; }

        /// <summary>Ticks skipped because <see cref="ShouldYield"/> said so.</summary>
        public int YieldedTicks { get; private set; }

        /// <summary>Seconds between re-sorts even when nothing announced a change (sources' costs drift).</summary>
        public float ResortSeconds { get; set; } = 1f;

        /// <summary>Cells waiting, as of the last sort.</summary>
        public int PendingCount => Math.Max(0, _pending.Count - _pendingCursor) + _priority.Count;

        /// <summary>Jobs in progress.</summary>
        public int ActiveCount => _active.Count;

        /// <summary>Cells captured this session.</summary>
        public int BakedCount => _baked.Count;

        /// <summary>Sets what is baked from where: <paramref name="known"/> cells, into <paramref name="store"/>, through <paramref name="capturer"/>, from <paramref name="sources"/>.</summary>
        public void Configure(KnownCellSet known, MapTileStore store, MapTileCapturer capturer, params IMapCellBakeSource[] sources) {
            _known = known;
            _store = store;
            _fillSink = new MapTileFillSink(store);
            _capturer = capturer;
            _sources = sources ?? Array.Empty<IMapCellBakeSource>();
            _dirty = true;
        }

        /// <summary>Bakes nearest <paramref name="cell"/> first.</summary>
        public void SetFocus(CellCoord cell) {
            if (cell == _focus) return;

            _focus = cell;
            _dirty = true;
        }

        /// <summary>Cells to bake besides the known ones (e.g. a ring round the train); replaces the previous extra set.</summary>
        public void SetExtraCells(IEnumerable<CellCoord> cells) {
            _extra.Clear();
            if (cells != null) _extra.UnionWith(cells);
            _dirty = true;
        }

        /// <summary>
        /// Bakes <paramref name="cell"/> ahead of the sorted order even if it has a tile — for a finer level
        /// the store reported missing, or a re-bake once its neighbours are complete.
        /// </summary>
        public void Prioritise(CellCoord cell) {
            _baked.Remove(cell);
            _forced.Add(cell);
            if (_priority.Contains(cell)) return;

            _priority.Add(cell);
        }

        /// <summary>True when <paramref name="cell"/> still needs a bake.</summary>
        public bool NeedsBake(CellCoord cell) {
            if (_forced.Contains(cell)) return true;
            if (_baked.Contains(cell)) return false;

            return _store == null || !_store.HasAnyTile(cell);
        }

        /// <summary>One frame of work; <paramref name="visible"/> is whether the map is on screen.</summary>
        public void Tick(bool visible, int frame, float time) {
            if (ShouldYield != null && ShouldYield()) {
                YieldedTicks++;
                return;
            }

            if (_store == null || _capturer == null || !_capturer.CanCapture) return;

            _capturer.Tick(time);
            if (!visible && HiddenFrameInterval > 1 && frame % HiddenFrameInterval != 0) return;

            float budget = visible ? VisibleBudgetMilliseconds : HiddenBudgetMilliseconds;
            int maxCaptures = visible ? VisibleCapturesPerTick : HiddenCapturesPerTick;
            using (TickMarker.Auto()) {
                if (ShouldResort(time)) {
                    using (ResortMarker.Auto()) RebuildPending(time);
                }

                _stopwatch.Restart();
                StartJobs(maxCaptures);
                StepJobs(budget, maxCaptures, time);
            }
        }

        /// <summary>Disposes the jobs in progress.</summary>
        public void Dispose() {
            foreach (IMapBakeJob job in _active) DisposeJob(job);
            _active.Clear();
        }

        private bool ShouldResort(float time) {
            if (_dirty) return true;
            if (_known != null && _known.Version != _sortedKnownVersion) return true;

            return time - _lastSortTime >= ResortSeconds;
        }

        private void RebuildPending(float time) {
            _dirty = false;
            _lastSortTime = time;
            _sortedKnownVersion = _known?.Version ?? -1;
            _scratch.Clear();
            _known?.CopyTo(_scratch);
            _candidates.Clear();
            _candidates.UnionWith(_scratch);
            _candidates.UnionWith(_extra);

            _pending.Clear();
            _pendingCursor = 0;
            foreach (CellCoord cell in _candidates) AddPending(cell);
            _candidates.Clear();
            _pending.Sort(_comparePending);
        }

        private void AddPending(CellCoord cell) {
            if (!NeedsBake(cell)) return;

            MapBakeCost cost = BestCost(cell, out _);
            if (cost == MapBakeCost.Unavailable) return;

            _pending.Add((cell, cost));
        }

        private int ComparePending((CellCoord cell, MapBakeCost cost) first, (CellCoord cell, MapBakeCost cost) second) {
            return MapBakeOrder.Compare(first.cell, first.cost, second.cell, second.cost, _focus);
        }

        private MapBakeCost BestCost(CellCoord cell, out IMapCellBakeSource best) {
            best = null;
            MapBakeCost bestCost = MapBakeCost.Unavailable;
            foreach (IMapCellBakeSource source in _sources) {
                MapBakeCost cost = source.GetCost(cell);
                if (cost == MapBakeCost.Unavailable) continue;
                if (bestCost != MapBakeCost.Unavailable && cost >= bestCost) continue;

                bestCost = cost;
                best = source;
            }

            return bestCost;
        }

        private void StartJobs(int maxActive) {
            while (_active.Count < maxActive && TryNextCell(out CellCoord cell)) {
                BestCost(cell, out IMapCellBakeSource source);
                IMapBakeJob job = source?.Begin(cell);
                if (job != null) _active.Add(job);
            }
        }

        private bool TryNextCell(out CellCoord cell) {
            if (TryTakePriority(out cell)) return true;

            while (_pendingCursor < _pending.Count) {
                cell = _pending[_pendingCursor++].cell;
                if (NeedsBake(cell) && !IsActive(cell)) return true;
            }

            return false;
        }

        private bool TryTakePriority(out CellCoord cell) {
            for (int index = 0; index < _priority.Count; index++) {
                cell = _priority[index];
                if (IsActive(cell)) continue;
                if (NeedsBake(cell) && BestCost(cell, out _) == MapBakeCost.Unavailable) continue;

                _priority.RemoveAt(index);
                if (NeedsBake(cell)) return true;

                index--;
            }

            cell = default;
            return false;
        }

        private bool IsActive(CellCoord cell) {
            foreach (IMapBakeJob job in _active) {
                if (job.Cell == cell) return true;
            }

            return false;
        }

        private void StepJobs(float budget, int maxCaptures, float time) {
            int captures = 0;
            for (int index = 0; index < _active.Count; index++) {
                float remaining = budget - (float)_stopwatch.Elapsed.TotalMilliseconds;
                if (remaining <= 0f) return;

                IMapBakeJob job = _active[index];
                MapBakeJobStatus status;
                using (StepMarker.Auto()) status = StepJob(job, remaining);
                if (status == MapBakeJobStatus.Working) continue;
                if (status == MapBakeJobStatus.Ready && !CanCaptureNow(captures, maxCaptures, budget)) continue;

                if (status == MapBakeJobStatus.Ready) {
                    using (CaptureMarker.Auto()) captures += CaptureJob(job, time) ? 1 : 0;
                }

                DisposeJob(job);
                _active.RemoveAt(index);
                index--;
            }
        }

        // A ready job whose build used up this tick's budget is captured on a later tick rather than on top of it.
        private bool CanCaptureNow(int captures, int maxCaptures, float budget) {
            return captures < maxCaptures && _stopwatch.Elapsed.TotalMilliseconds < budget;
        }

        // A job that throws fails: it is dropped and its cell retried later, never stepped again.
        private static MapBakeJobStatus StepJob(IMapBakeJob job, float budget) {
            try {
                return job.Step(budget);
            } catch (Exception exception) {
                UnityEngine.Debug.LogException(exception);
                return MapBakeJobStatus.Failed;
            }
        }

        private bool TryCapture(IMapBakeJob job, float time) {
            try {
                IMapTileSink sink = job.KeepsExistingTiles ? _fillSink : (IMapTileSink)_store;
                return _capturer.Capture(job.Cell, job.CaptureArea, sink, time);
            } catch (Exception exception) {
                UnityEngine.Debug.LogException(exception);
                return false;
            }
        }

        private static void DisposeJob(IMapBakeJob job) {
            try {
                job.Dispose();
            } catch (Exception exception) {
                UnityEngine.Debug.LogException(exception);
            }
        }

        private bool CaptureJob(IMapBakeJob job, float time) {
            if (!TryCapture(job, time)) return false;

            _baked.Add(job.Cell);
            _forced.Remove(job.Cell);
            CellBaked?.Invoke(job.Cell);
            return true;
        }
    }
}
