using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Procedural.Grid;
using AlpineLib.Procedural.Streaming;
using AlpineLib.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Sky {
    /// <summary>
    /// Visual-only copies of a streamed world's big props for the cells around the streamer's focus out to
    /// <see cref="Radius"/>, so they already stand before (and after) the full cell is built. Every cell in the square
    /// is prepared on worker threads through an <see cref="IFarPropSource"/>; a cell draws while it is in range and
    /// its built twin has not taken over (<see cref="Suppress"/> / <see cref="Unsuppress"/>, same frame, so the two
    /// never double up or leave a gap). Newly drawn cells fade in with the stylized screen-door.
    /// </summary>
    /// <remarks>
    /// Cells are placed through the streamer's own cell space at every draw (its transform times the cell centre,
    /// as the streamer places its cell roots), so whole-cell origin rebases move the copies in the same frame. Drawn
    /// on one layer per camera through <see cref="SkyCameraFilter"/> (never into map or portrait captures), shadows
    /// off both ways. Play mode ticks from <c>Update</c>; edit-mode callers use <see cref="Tick"/> or
    /// <see cref="PrepareAllNow"/>.
    /// </remarks>
    [ExecuteAlways]
    public sealed class FarPropLayer : MonoBehaviour {
        /// <summary>Cells past the radius kept prepared before they are dropped.</summary>
        public const int KeepMargin = 1;

        private const ulong FocusObserver = 1UL;

        private readonly Dictionary<CellCoord, FarPropLayerCell> _cells = new Dictionary<CellCoord, FarPropLayerCell>();
        private readonly List<CellCoord> _added = new List<CellCoord>();
        private readonly List<CellCoord> _removed = new List<CellCoord>();
        private readonly List<FarPropLayerCell> _scratch = new List<FarPropLayerCell>();
        private CellStreamer _streamer;
        private IFarPropSource _source;
        private CellInterestSet _interest;
        private FarCellVisibility _visibility;
        private DitherFadeMaterials _fadeMaterials;
        private Func<double> _clock = DitherFade.DefaultClock;
        private double _fadeSeconds = 1.0;
        private float _maxOffsetSeconds;
        private int _layer;
        private int _maxConcurrentPrepares = 2;

        /// <summary>Chebyshev radius around the focus cell that draws.</summary>
        public int Radius => _visibility != null ? _visibility.Radius : 0;

        public bool IsConfigured => _streamer != null && _source != null;

        /// <summary>The layer the copies draw on.</summary>
        public int Layer => _layer;

        /// <summary>Which cells draw, and since when.</summary>
        public FarCellVisibility Visibility => _visibility;

        /// <summary>Cells tracked (preparing or prepared).</summary>
        public int CellCount => _cells.Count;

        /// <summary>Worker prepares in flight at once.</summary>
        public int MaxConcurrentPrepares {
            get => _maxConcurrentPrepares;
            set => _maxConcurrentPrepares = Mathf.Max(1, value);
        }

        /// <summary>
        /// Draws <paramref name="source"/>'s copies around <paramref name="streamer"/>'s focus out to
        /// <paramref name="radius"/> on <paramref name="layer"/>, fading new cells in over <paramref name="fadeSeconds"/>
        /// (instance delays below <paramref name="maxOffsetSeconds"/>) with twins from <paramref name="fadeMaterials"/>
        /// (null: no fade) on <paramref name="clock"/>. Drops whatever was configured before.
        /// </summary>
        public void Configure(CellStreamer streamer, IFarPropSource source, int radius, int layer, DitherFadeMaterials fadeMaterials,
                double fadeSeconds, float maxOffsetSeconds, Func<double> clock) {
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must not be negative.");
            if (layer < 0 || layer > 31) throw new ArgumentOutOfRangeException(nameof(layer), layer, "Layer must be 0..31.");

            Clear();
            _streamer = streamer ? streamer : throw new ArgumentNullException(nameof(streamer));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _interest = new CellInterestSet(radius, KeepMargin);
            _visibility = new FarCellVisibility(radius);
            _layer = layer;
            _fadeMaterials = fadeMaterials;
            _fadeSeconds = fadeSeconds > 0.0 ? fadeSeconds : 1.0;
            _maxOffsetSeconds = Mathf.Max(0f, maxOffsetSeconds);
            _clock = clock ?? DitherFade.DefaultClock;
        }

        /// <summary>The built cell shows its own copies: this cell stops drawing now.</summary>
        public void Suppress(CellCoord cell) {
            _visibility?.Suppress(cell);
        }

        /// <summary>The built cell is gone: this cell draws again now, fully shown, when ready and in range.</summary>
        public void Unsuppress(CellCoord cell) {
            if (_visibility == null) return;

            _visibility.Unsuppress(cell, _clock());
            if (_cells.TryGetValue(cell, out FarPropLayerCell tracked)) SyncFade(tracked);
        }

        /// <summary>True when the cell draws, with the clock time it began to show.</summary>
        public bool TryGetShownSince(CellCoord cell, out double since) {
            since = double.NaN;
            return _visibility != null && _visibility.TryGetShownSince(cell, out since);
        }

        /// <summary>Copies the cell would draw (0 until prepared).</summary>
        public int InstanceCountOf(CellCoord cell) {
            return _cells.TryGetValue(cell, out FarPropLayerCell tracked) && tracked.IsReady ? tracked.InstanceCount : 0;
        }

        /// <summary>Copies the cell draws right now (0 when hidden or suppressed).</summary>
        public int DrawnCountOf(CellCoord cell) {
            return _visibility != null && _visibility.IsDrawn(cell) ? InstanceCountOf(cell) : 0;
        }

        /// <summary>The cell's batches once prepared.</summary>
        public bool TryGetBatches(CellCoord cell, out IReadOnlyList<InstancedBatch> batches) {
            batches = null;
            if (!_cells.TryGetValue(cell, out FarPropLayerCell tracked) || !tracked.IsReady) return false;

            batches = tracked.Batches;
            return true;
        }

        /// <summary>Cell-centre frame to world, as the streamer places that cell's root.</summary>
        public Matrix4x4 CellToWorld(CellCoord cell) {
            return _streamer.transform.localToWorldMatrix * Matrix4x4.Translate(_streamer.LocalCenterOf(cell));
        }

        /// <summary>One frame: follow the focus, collect finished prepares, start new ones and refresh what draws.</summary>
        public void Tick() {
            if (!IsConfigured) return;

            RefreshInterest();
            CollectPrepared();
            StartPrepares();
            RefreshVisibility();
        }

        /// <summary>Refreshes the square and prepares every cell in it on the calling thread (gates, previews).</summary>
        public void PrepareAllNow() {
            if (!IsConfigured) return;

            RefreshInterest();
            foreach (FarPropLayerCell cell in _cells.Values) {
                if (cell.IsReady) continue;

                WaitQuietly(cell.PrepareTask);
                if (cell.PrepareTask == null || !cell.PrepareTask.IsCompleted) Adopt(cell, PrepareNow(cell.Coord));
            }
            CollectPrepared();
            RefreshVisibility();
        }

        /// <summary>Forgets every cell and suppression, cancelling prepares in flight.</summary>
        public void Clear() {
            foreach (FarPropLayerCell cell in _cells.Values) cell.Cancellation?.Cancel();

            _cells.Clear();
            _interest = _interest != null ? new CellInterestSet(_interest.Radius, KeepMargin) : null;
            _visibility?.Clear();
        }

        private void Update() {
            if (Application.isPlaying) Tick();
        }

        private void OnEnable() {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable() {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        }

        private void OnDestroy() {
            Clear();
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera) {
            Draw(camera);
        }

        /// <summary>Submits every drawn cell for <paramref name="camera"/>.</summary>
        public void Draw(Camera camera) {
            if (!IsConfigured || !SkyCameraFilter.ShouldDraw(camera, _layer)) return;

            foreach (FarPropLayerCell cell in _cells.Values) {
                if (!cell.IsReady || !_visibility.IsDrawn(cell.Coord)) continue;

                SyncFade(cell);
                Matrix4x4 toWorld = CellToWorld(cell.Coord);
                foreach (InstancedBatch batch in cell.Batches) batch.Draw(camera, _layer, toWorld, ShadowCastingMode.Off, false);
            }
        }

        private void RefreshInterest() {
            if (!_streamer.TryGetFocusCell(out CellCoord focus)) return;

            _visibility.SetFocus(focus);
            _interest.SetObserver(FocusObserver, focus);
            _interest.Update(_added, _removed);
            foreach (CellCoord coord in _removed) Drop(coord);
            foreach (CellCoord coord in _added) _cells[coord] = new FarPropLayerCell(coord);
        }

        private void Drop(CellCoord coord) {
            if (!_cells.TryGetValue(coord, out FarPropLayerCell cell)) return;

            cell.Cancellation?.Cancel();
            _cells.Remove(coord);
            _visibility.Forget(coord);
        }

        private void CollectPrepared() {
            foreach (FarPropLayerCell cell in _cells.Values) {
                if (cell.IsReady || cell.PrepareTask == null || !cell.PrepareTask.IsCompleted) continue;

                Adopt(cell, ResultOf(cell));
            }
        }

        private FarPropCell ResultOf(FarPropLayerCell cell) {
            if (cell.PrepareTask.Status == TaskStatus.RanToCompletion) return cell.PrepareTask.Result;
            if (cell.PrepareTask.IsFaulted) Debug.LogException(cell.PrepareTask.Exception, this);

            return null;
        }

        // A failed prepare leaves the cell ready and empty: it is not retried until it leaves and re-enters the square.
        private void Adopt(FarPropLayerCell cell, FarPropCell result) {
            cell.PrepareTask = null;
            cell.Cancellation = null;
            cell.Batches.Clear();
            cell.InstanceCount = 0;
            if (result != null) {
                foreach (FarPropBatch batch in result.Batches) {
                    cell.Batches.Add(new InstancedBatch(batch.Mesh, batch.Material, batch.Submesh, batch.Matrices, batch.Bounds));
                    cell.InstanceCount += batch.Matrices.Length;
                }
            }
            cell.IsReady = true;
            cell.FadeSetFor = double.NaN;
            _visibility.MarkReady(cell.Coord);
        }

        private FarPropCell PrepareNow(CellCoord coord) {
            try {
                return _source.Prepare(coord, CancellationToken.None);
            } catch (Exception exception) {
                Debug.LogException(exception, this);
                return null;
            }
        }

        private void StartPrepares() {
            int running = 0;
            foreach (FarPropLayerCell cell in _cells.Values) {
                if (cell.PrepareTask != null) running++;
            }

            while (running < _maxConcurrentPrepares) {
                FarPropLayerCell next = NextToPrepare();
                if (next == null) return;

                BeginPrepare(next);
                running++;
            }
        }

        // Cells the built window does not cover first, nearest the focus first within each group.
        private FarPropLayerCell NextToPrepare() {
            FarPropLayerCell best = null;
            long bestRank = long.MaxValue;
            foreach (FarPropLayerCell cell in _cells.Values) {
                if (cell.IsReady || cell.PrepareTask != null) continue;

                long rank = CellInterestSet.ChebyshevDistance(cell.Coord, _visibility.Focus) + (_visibility.IsSuppressed(cell.Coord) ? 1000L : 0L);
                if (rank >= bestRank) continue;

                best = cell;
                bestRank = rank;
            }
            return best;
        }

        private void BeginPrepare(FarPropLayerCell cell) {
            IFarPropSource source = _source;
            CellCoord coord = cell.Coord;
            var cancellation = new CancellationTokenSource();
            CancellationToken token = cancellation.Token;
            cell.Cancellation = cancellation;
            cell.PrepareTask = Task.Run(() => source.Prepare(coord, token), token);
        }

        private void RefreshVisibility() {
            _visibility.Refresh(_cells.Keys, _clock());
            foreach (FarPropLayerCell cell in _cells.Values) {
                if (cell.IsReady) SyncFade(cell);
            }
        }

        // A cell that began to show at a new time fades in from then (a long-ago start reads fully shown at once).
        private void SyncFade(FarPropLayerCell cell) {
            if (!_visibility.TryGetShownSince(cell.Coord, out double since)) return;
            if (since.Equals(cell.FadeSetFor)) return;

            cell.FadeSetFor = since;
            if (_fadeMaterials == null) return;

            foreach (InstancedBatch batch in cell.Batches) {
                if (!_fadeMaterials.TryGetTwin(batch.Material, out Material twin)) continue;

                batch.HoldFade(twin, _fadeSeconds, _maxOffsetSeconds, _clock).StartAt(since);
            }
        }

        private static void WaitQuietly(Task task) {
            if (task == null) return;

            try {
                task.Wait();
            } catch (AggregateException) {
            }
        }
    }
}
