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
    /// never double up or leave a gap). Newly drawn cells fade in with the stylized screen-door. A cell's
    /// <see cref="FarPropCell.MeshPayloads"/> upload on the main thread, at most <see cref="MaxUploadsPerFrame"/> a
    /// frame, and the cell draws only once every one is up; each draws as a one-copy batch at the cell centre.
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

        /// <summary>Payload meshes uploaded per <see cref="Tick"/> unless set otherwise.</summary>
        public const int DefaultUploadsPerFrame = 2;

        private const ulong FocusObserver = 1UL;

        /// <summary>A failed prepare is retried 1 s later, doubling to at most 30 s while it keeps failing.</summary>
        public static readonly RetryBackoff PrepareRetry = new RetryBackoff(1.0, 30.0);

        private readonly Dictionary<CellCoord, FarPropLayerCell> _cells = new Dictionary<CellCoord, FarPropLayerCell>();
        private readonly List<CellCoord> _added = new List<CellCoord>();
        private readonly List<CellCoord> _removed = new List<CellCoord>();
        private readonly List<FarPropLayerCell> _scratch = new List<FarPropLayerCell>();
        private readonly List<FarPropLayerCell> _uploads = new List<FarPropLayerCell>();
        private static readonly Matrix4x4[] PayloadPlacement = { Matrix4x4.identity };
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
        private int _maxUploadsPerFrame = DefaultUploadsPerFrame;

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
        /// Payload meshes uploaded per <see cref="Tick"/> (<see cref="PrepareAllNow"/> uploads every one), oldest result
        /// first whatever its size or distance: keep payloads small, with normals, so two never hitch a frame.
        /// </summary>
        public int MaxUploadsPerFrame {
            get => _maxUploadsPerFrame;
            set => _maxUploadsPerFrame = Mathf.Max(1, value);
        }

        /// <summary>Payload meshes prepared but not uploaded yet, over every cell.</summary>
        public int PendingUploads {
            get {
                int pending = 0;
                foreach (FarPropLayerCell cell in _uploads) pending += cell.Pending.MeshPayloads.Count - cell.PayloadMeshes.Count;
                return pending;
            }
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
            ConfigureFade(fadeMaterials, fadeSeconds, maxOffsetSeconds, clock);
        }

        /// <summary>
        /// Cells that begin to show from now on fade in over <paramref name="fadeSeconds"/> with twins from
        /// <paramref name="fadeMaterials"/> (null: they show at once) on <paramref name="clock"/>; cells already drawn keep their fade.
        /// </summary>
        public void ConfigureFade(DitherFadeMaterials fadeMaterials, double fadeSeconds, float maxOffsetSeconds, Func<double> clock) {
            _fadeMaterials = fadeMaterials;
            _fadeSeconds = fadeSeconds > 0.0 ? fadeSeconds : 1.0;
            _maxOffsetSeconds = Mathf.Max(0f, maxOffsetSeconds);
            _clock = clock ?? DitherFade.DefaultClock;
        }

        /// <summary>True when cells fade in as they begin to show.</summary>
        public bool IsFading => _fadeMaterials != null;

        /// <summary>The built cell shows its own copies: this cell stops drawing now.</summary>
        public void Suppress(CellCoord cell) {
            _visibility?.Suppress(cell);
        }

        /// <summary>
        /// The built cell is gone: this cell draws again now when ready and in range, carrying on the fade the built
        /// copies began at <paramref name="shownSince"/> (NaN, the default: fully shown).
        /// </summary>
        public void Unsuppress(CellCoord cell, double shownSince = double.NaN) {
            if (_visibility == null) return;

            _visibility.Unsuppress(cell, _clock(), shownSince);
            if (_cells.TryGetValue(cell, out FarPropLayerCell tracked)) SyncFade(tracked);
        }

        /// <summary>Prepares that failed so far (each is retried with <see cref="PrepareRetry"/>).</summary>
        public int FailedPrepares { get; private set; }

        /// <summary>True when the cell draws, with the clock time it began to show.</summary>
        public bool TryGetShownSince(CellCoord cell, out double since) {
            since = double.NaN;
            return _visibility != null && _visibility.TryGetShownSince(cell, out since);
        }

        /// <summary>Copies the cell would draw, each payload mesh counting as one (0 until prepared and uploaded).</summary>
        public int InstanceCountOf(CellCoord cell) {
            return _cells.TryGetValue(cell, out FarPropLayerCell tracked) && tracked.IsReady ? tracked.InstanceCount : 0;
        }

        /// <summary>Payload meshes the cell would draw (0 until prepared and uploaded).</summary>
        public int PayloadMeshCountOf(CellCoord cell) {
            return _cells.TryGetValue(cell, out FarPropLayerCell tracked) && tracked.IsReady ? tracked.PayloadMeshes.Count : 0;
        }

        /// <summary>Copies the cell draws right now, payload meshes included (0 when hidden or suppressed).</summary>
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
            UploadPayloads(_maxUploadsPerFrame);
            StartPrepares();
            RefreshVisibility();
        }

        /// <summary>Refreshes the square and prepares every cell in it on the calling thread (gates, previews).</summary>
        public void PrepareAllNow() {
            if (!IsConfigured) return;

            RefreshInterest();
            double now = _clock();
            foreach (FarPropLayerCell cell in _cells.Values) {
                if (cell.IsReady || cell.Pending != null || cell.RetryAt > now) continue;

                WaitQuietly(cell.PrepareTask);
                if (cell.PrepareTask == null || !cell.PrepareTask.IsCompleted) AdoptOrRetry(cell, PrepareNow(cell.Coord, out bool isFailed), isFailed);
            }
            CollectPrepared();
            UploadPayloads(int.MaxValue);
            RefreshVisibility();
        }

        /// <summary>Forgets every cell and suppression, cancelling prepares in flight and freeing payload meshes.</summary>
        public void Clear() {
            foreach (FarPropLayerCell cell in _cells.Values) {
                cell.Cancellation?.Cancel();
                DestroyPayloadMeshes(cell);
            }

            _cells.Clear();
            _uploads.Clear();
            _interest = _interest != null ? new CellInterestSet(_interest.Radius, KeepMargin) : null;
            _visibility?.Clear();
        }

        private void Update() {
            if (Application.isPlaying) Tick();
        }

        private void OnEnable() {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        // Edit mode frees payload meshes here (domain reloads skip OnDestroy); those cells prepare again on enable.
        private void OnDisable() {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            if (!Application.isPlaying) ResetPayloadCells();
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
            DestroyPayloadMeshes(cell);
            _uploads.Remove(cell);
            _cells.Remove(coord);
            _visibility.Forget(coord);
        }

        private void CollectPrepared() {
            foreach (FarPropLayerCell cell in _cells.Values) {
                if (cell.IsReady || cell.PrepareTask == null || !cell.PrepareTask.IsCompleted) continue;

                FarPropCell result = ResultOf(cell, out bool isFailed);
                AdoptOrRetry(cell, result, isFailed);
            }
        }

        private FarPropCell ResultOf(FarPropLayerCell cell, out bool isFailed) {
            isFailed = cell.PrepareTask.Status != TaskStatus.RanToCompletion;
            if (!isFailed) return cell.PrepareTask.Result;
            if (cell.PrepareTask.IsFaulted) Debug.LogWarning($"FarPropLayer: cell {cell.Coord} prepare failed: {cell.PrepareTask.Exception?.GetBaseException()}", this);

            return null;
        }

        // A failed prepare leaves the cell unready (its built twin, if any, still covers it) and retries it later.
        private void AdoptOrRetry(FarPropLayerCell cell, FarPropCell result, bool isFailed) {
            if (!isFailed) {
                Adopt(cell, result);
                return;
            }

            cell.PrepareTask = null;
            cell.Cancellation = null;
            cell.Failures++;
            cell.RetryAt = _clock() + PrepareRetry.DelayAfter(cell.Failures);
            FailedPrepares++;
        }

        // A result with mesh payloads waits for its uploads; one without is ready at once.
        private void Adopt(FarPropLayerCell cell, FarPropCell result) {
            cell.PrepareTask = null;
            cell.Cancellation = null;
            cell.Failures = 0;
            if (result != null && result.MeshPayloads.Count > 0) {
                cell.Pending = result;
                _uploads.Add(cell);
                return;
            }

            MakeReady(cell, result);
        }

        private void MakeReady(FarPropLayerCell cell, FarPropCell result) {
            cell.Batches.Clear();
            cell.InstanceCount = 0;
            if (result != null) {
                foreach (FarPropBatch batch in result.Batches) {
                    cell.Batches.Add(new InstancedBatch(batch.Mesh, batch.Material, batch.Submesh, batch.Matrices, batch.Bounds));
                    cell.InstanceCount += batch.Matrices.Length;
                }
                AddPayloadBatches(cell, result);
            }
            cell.IsReady = true;
            cell.FadeSetFor = double.NaN;
            _visibility.MarkReady(cell.Coord);
        }

        private FarPropCell PrepareNow(CellCoord coord, out bool isFailed) {
            isFailed = false;
            try {
                return _source.Prepare(coord, CancellationToken.None);
            } catch (Exception exception) {
                Debug.LogWarning($"FarPropLayer: cell {coord} prepare failed: {exception}", this);
                isFailed = true;
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
            double now = _clock();
            foreach (FarPropLayerCell cell in _cells.Values) {
                if (cell.IsReady || cell.PrepareTask != null || cell.Pending != null || cell.RetryAt > now) continue;

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

        // Oldest result first, one mesh at a time (the budget counts meshes, not vertices); a cell turns ready with
        // its last upload, and a failed upload frees that cell's meshes and retries its prepare like a failed one.
        private void UploadPayloads(int budget) {
            while (budget > 0 && _uploads.Count > 0) {
                FarPropLayerCell cell = _uploads[0];
                budget--;
                if (!TryUploadNext(cell)) {
                    FailUpload(cell);
                    continue;
                }
                if (cell.PayloadMeshes.Count < cell.Pending.MeshPayloads.Count) continue;

                _uploads.RemoveAt(0);
                FarPropCell result = cell.Pending;
                cell.Pending = null;
                MakeReady(cell, result);
            }
        }

        private bool TryUploadNext(FarPropLayerCell cell) {
            int index = cell.PayloadMeshes.Count;
            try {
                FarPropMeshPayload payload = cell.Pending.MeshPayloads[index];
                cell.PayloadMeshes.Add(payload.CreateMesh($"Far Payload {cell.Coord} {index}"));
                return true;
            } catch (Exception exception) {
                Debug.LogWarning($"FarPropLayer: cell {cell.Coord} payload {index} upload failed: {exception}", this);
                return false;
            }
        }

        private void FailUpload(FarPropLayerCell cell) {
            _uploads.Remove(cell);
            DestroyPayloadMeshes(cell);
            cell.Pending = null;
            AdoptOrRetry(cell, null, true);
        }

        // Each payload draws as one copy and counts as one in the cell's instance count.
        private static void AddPayloadBatches(FarPropLayerCell cell, FarPropCell result) {
            for (int index = 0; index < cell.PayloadMeshes.Count; index++) {
                FarPropMeshPayload payload = result.MeshPayloads[index];
                Material material = result.PayloadMaterials[payload.MaterialSlot];
                cell.Batches.Add(new InstancedBatch(cell.PayloadMeshes[index], material, 0, PayloadPlacement, payload.Bounds));
                cell.InstanceCount++;
            }
        }

        // Cells holding payload meshes go back to unprepared, their meshes freed.
        private void ResetPayloadCells() {
            foreach (FarPropLayerCell cell in _cells.Values) {
                if (cell.PayloadMeshes.Count == 0 && cell.Pending == null) continue;

                DestroyPayloadMeshes(cell);
                cell.Pending = null;
                cell.Batches.Clear();
                cell.InstanceCount = 0;
                cell.IsReady = false;
                cell.FadeSetFor = double.NaN;
                _visibility?.Forget(cell.Coord);
            }
            _uploads.Clear();
        }

        private static void DestroyPayloadMeshes(FarPropLayerCell cell) {
            foreach (Mesh mesh in cell.PayloadMeshes) {
                if (mesh == null) continue;

                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
            cell.PayloadMeshes.Clear();
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
