using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Procedural.Grid;
using AlpineLib.Procedural.Kits;
using UnityEngine;

namespace AlpineLib.Procedural.Streaming {
    /// <summary>
    /// Keeps the cells around a set of observers built: a <see cref="CellInterestSet"/> decides which cells
    /// are wanted, an <see cref="ICellBuilder"/> prepares them on worker threads and instantiates them in
    /// time-sliced steps on the main thread, nearest to the local observer first.
    /// </summary>
    /// <remarks>
    /// Cell roots are children of this transform, placed at each cell's centre in its local frame through
    /// <see cref="Space"/>; observers are mapped into that frame too. A floating origin rebases by whole
    /// cells through <see cref="ShiftOrigin"/> or <see cref="SetOriginCell"/>, which re-place every root from
    /// double-precision cell centres — do not also translate this transform. Play mode ticks from
    /// <c>Update</c>; edit-mode previews call <see cref="Tick"/> or <see cref="BuildAllNow"/> themselves.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class CellStreamer : MonoBehaviour {
        private const double OriginShiftToleranceMetres = 0.01;

        [SerializeField, Tooltip("Edge length of one cell, metres.")]
        private double cellSize = 128.0;

        [SerializeField, Min(0), Tooltip("Chebyshev radius loaded around each observer (2 = 5x5).")]
        private int radius = 2;

        [SerializeField, Min(0), Tooltip("Extra cells an observer must move past before a cell unloads.")]
        private int unloadMargin = CellInterestSet.DefaultUnloadMargin;

        [SerializeField, Min(0.1f), Tooltip("Main-thread milliseconds per frame spent instantiating cells.")]
        private float frameBudgetMilliseconds = 4f;

        [SerializeField, Min(1), Tooltip("Cells prepared on worker threads at once.")]
        private int maxConcurrentPrepares = 2;

        [SerializeField, Tooltip("Optional component implementing ICellBuilder; Builder set from code wins.")]
        private MonoBehaviour builderBehaviour;

        [SerializeField, Tooltip("Optional components implementing ICellObserverSource.")]
        private MonoBehaviour[] observerSourceBehaviours = Array.Empty<MonoBehaviour>();

        private readonly Dictionary<CellCoord, StreamedCell> _cells = new Dictionary<CellCoord, StreamedCell>();
        private readonly List<StreamedCell> _abandoned = new List<StreamedCell>();
        private readonly List<ICellObserverSource> _sources = new List<ICellObserverSource>();
        private readonly List<CellObserver> _observers = new List<CellObserver>();
        private readonly List<Vector3> _priorityPoints = new List<Vector3>();
        private readonly HashSet<ulong> _observerIds = new HashSet<ulong>();
        private readonly HashSet<ulong> _previousObserverIds = new HashSet<ulong>();
        private readonly List<CellCoord> _added = new List<CellCoord>();
        private readonly List<CellCoord> _removed = new List<CellCoord>();
        private readonly List<StreamedCell> _scratchCells = new List<StreamedCell>();

        private CellBuildBudget _budget;
        private CellInterestSet _interest;
        private CellSpace _space;
        private bool _hasSpace;
        private ICellBuilder _builder;
        private StreamedCell _instantiating;
        private bool _tearingDown;

        /// <summary>Raised on the main thread when a cell finishes instantiating.</summary>
        public event Action<StreamedCell> CellLoaded;

        /// <summary>Raised when a tracked cell is dropped, whatever state it had reached.</summary>
        public event Action<CellCoord> CellUnloaded;

        /// <summary>The cell grid in this transform's local frame.</summary>
        public CellSpace Space {
            get {
                EnsureSpace();
                return _space;
            }
        }

        /// <summary>Chebyshev load radius per observer.</summary>
        public int Radius => radius;

        /// <summary>Cells past the load radius before an unload.</summary>
        public int UnloadMargin => unloadMargin;

        /// <summary>Main-thread milliseconds per <see cref="Tick"/> spent instantiating.</summary>
        public float FrameBudgetMilliseconds {
            get => frameBudgetMilliseconds;
            set => frameBudgetMilliseconds = Mathf.Max(0.1f, value);
        }

        /// <summary>Cells prepared on worker threads at once.</summary>
        public int MaxConcurrentPrepares {
            get => maxConcurrentPrepares;
            set => maxConcurrentPrepares = Mathf.Max(1, value);
        }

        /// <summary>The builder; changing it releases every cell built by the previous one.</summary>
        public ICellBuilder Builder {
            get => ResolveBuilder();
            set {
                if (ReferenceEquals(value, _builder)) return;

                ReleaseAll();
                _builder = value;
            }
        }

        /// <summary>Tracked cells (any state, released ones excluded).</summary>
        public int CellCount => _cells.Count;

        /// <summary>Cells fully built.</summary>
        public int LiveCount => CountInState(StreamedCellState.Live);

        /// <summary>True when every tracked cell is live or failed and no worker is still running.</summary>
        public bool IsIdle => _instantiating == null && _abandoned.Count == 0 && CountPending() == 0;

        /// <summary>Sets the grid and radii; releases every cell first. The origin cell is kept.</summary>
        public void Configure(double newCellSize, int newRadius, int newUnloadMargin) {
            if (!(newCellSize > 0.0) || double.IsInfinity(newCellSize)) {
                throw new ArgumentOutOfRangeException(nameof(newCellSize), newCellSize, "Cell size must be positive and finite.");
            }

            if (newRadius < 0) throw new ArgumentOutOfRangeException(nameof(newRadius), newRadius, "Radius must not be negative.");
            if (newUnloadMargin < 0) throw new ArgumentOutOfRangeException(nameof(newUnloadMargin), newUnloadMargin, "Margin must not be negative.");

            CellCoord originCell = _hasSpace ? _space.OriginCell : default;
            ReleaseAll();
            cellSize = newCellSize;
            radius = newRadius;
            unloadMargin = newUnloadMargin;
            _interest = null;
            _space = new CellSpace(cellSize, originCell);
            _hasSpace = true;
        }

        /// <summary>Adds a source of observers; takes effect on the next tick.</summary>
        public void AddObserverSource(ICellObserverSource source) {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (_sources.Contains(source)) return;

            _sources.Add(source);
        }

        /// <summary>Removes a source; its observers' cells unload on the next tick unless others hold them.</summary>
        public bool RemoveObserverSource(ICellObserverSource source) {
            return _sources.Remove(source);
        }

        /// <summary>The tracked cell at <paramref name="coord"/>, in any state.</summary>
        public bool TryGetCell(CellCoord coord, out StreamedCell cell) {
            return _cells.TryGetValue(coord, out cell);
        }

        /// <summary>Copies every tracked cell into <paramref name="cells"/>, cleared first, sorted row-major.</summary>
        public void CopyCells(List<StreamedCell> cells) {
            if (cells == null) throw new ArgumentNullException(nameof(cells));

            cells.Clear();
            cells.AddRange(_cells.Values);
            cells.Sort((left, right) => CellInterestSet.CompareRowMajor(left.Coord, right.Coord));
        }

        /// <summary>The cell centre in this transform's local frame (y = 0).</summary>
        public Vector3 LocalCenterOf(CellCoord coord) {
            CellSpace space = Space;
            return new Vector3((float)space.CenterX(coord), 0f, (float)space.CenterZ(coord));
        }

        /// <summary>
        /// Rebases so <paramref name="originCell"/> sits at local (0, 0) and re-places every cell root.
        /// Matches a session origin's cell when both grids share cell size and absolute zero.
        /// </summary>
        public void SetOriginCell(CellCoord originCell) {
            _space = Space.WithOrigin(originCell);

            foreach (StreamedCell cell in _cells.Values) {
                if (cell.Root == null) continue;

                cell.Root.localPosition = LocalCenterOf(cell.Coord);
            }
        }

        /// <summary>
        /// Applies a floating-origin shift: every position gained <paramref name="delta"/>, which must be a
        /// whole number of cells in X and Z.
        /// </summary>
        /// <exception cref="ArgumentException">The delta is not a whole number of cells.</exception>
        public void ShiftOrigin(Vector3 delta) {
            CellSpace space = Space;
            int shiftX = ToWholeCells(-delta.x, space.CellSize, nameof(delta));
            int shiftZ = ToWholeCells(-delta.z, space.CellSize, nameof(delta));
            if (shiftX == 0 && shiftZ == 0) return;

            SetOriginCell(new CellCoord(space.OriginCell.X + shiftX, space.OriginCell.Y + shiftZ));
        }

        /// <summary>
        /// One frame of streaming: refresh observers and interest, release what left, start worker prepares,
        /// collect finished ones and instantiate within <see cref="FrameBudgetMilliseconds"/>.
        /// </summary>
        public void Tick() {
            if (ResolveBuilder() == null) return;

            RefreshInterest();
            CollectAbandoned();
            CollectPrepared();
            StartPrepares();
            RunInstantiation(ResolveBudget());
        }

        /// <summary>
        /// Refreshes interest and builds every wanted cell to completion on the calling thread (prepares
        /// included), for edit-mode previews and gates.
        /// </summary>
        public void BuildAllNow() {
            ICellBuilder builder = ResolveBuilder();
            if (builder == null) return;

            RefreshInterest();
            WaitForWorkers();
            CollectAbandoned();
            CollectPrepared();
            PrepareQueuedNow(builder);
            RunInstantiation(CellBuildBudget.CreateUnlimited());
        }

        /// <summary>Drops every observer and releases every cell, cancelling builds in flight.</summary>
        public void ReleaseAll() {
            _interest?.Clear(_removed);
            _previousObserverIds.Clear();
            _scratchCells.Clear();
            _scratchCells.AddRange(_cells.Values);

            foreach (StreamedCell cell in _scratchCells) {
                ReleaseCell(cell.Coord);
            }

            _scratchCells.Clear();
        }

        private void Update() {
            Tick();
        }

        private void OnDisable() {
            TearDown();
        }

        private void OnDestroy() {
            TearDown();
        }

        // The hierarchy cannot be edited while it deactivates, so roots are destroyed without reparenting.
        private void TearDown() {
            _tearingDown = true;
            try {
                ReleaseAll();
            } finally {
                _tearingDown = false;
            }
        }

        private ICellBuilder ResolveBuilder() {
            if (_builder == null && builderBehaviour != null) _builder = builderBehaviour as ICellBuilder;

            return _builder;
        }

        private CellBuildBudget ResolveBudget() {
            if (_budget == null) _budget = new CellBuildBudget(frameBudgetMilliseconds);

            _budget.FrameMilliseconds = frameBudgetMilliseconds;
            return _budget;
        }

        private void EnsureSpace() {
            if (_hasSpace) return;

            _space = new CellSpace(cellSize);
            _hasSpace = true;
        }

        private void RefreshInterest() {
            if (_interest == null) _interest = new CellInterestSet(radius, unloadMargin);

            CollectObservers();
            ApplyObservers();
            _interest.Update(_added, _removed);

            foreach (CellCoord coord in _removed) {
                ReleaseCell(coord);
            }

            foreach (CellCoord coord in _added) {
                _cells[coord] = new StreamedCell(coord);
            }
        }

        private void CollectObservers() {
            _observers.Clear();

            foreach (ICellObserverSource source in _sources) {
                source.CollectObservers(_observers);
            }

            foreach (MonoBehaviour behaviour in observerSourceBehaviours) {
                if (behaviour is ICellObserverSource source && !_sources.Contains(source)) source.CollectObservers(_observers);
            }
        }

        private void ApplyObservers() {
            CellSpace space = Space;
            _observerIds.Clear();
            _priorityPoints.Clear();

            foreach (CellObserver observer in _observers) {
                Vector3 local = transform.InverseTransformPoint(observer.Position);
                _interest.SetObserver(observer.Id, space.CellOf(local.x, local.z));
                _observerIds.Add(observer.Id);
                if (observer.IsLocal) _priorityPoints.Add(local);
            }

            if (_priorityPoints.Count == 0) AddAllObserverPoints();

            foreach (ulong previousId in _previousObserverIds) {
                if (!_observerIds.Contains(previousId)) _interest.RemoveObserver(previousId);
            }

            _previousObserverIds.Clear();
            _previousObserverIds.UnionWith(_observerIds);
        }

        private void AddAllObserverPoints() {
            foreach (CellObserver observer in _observers) {
                _priorityPoints.Add(transform.InverseTransformPoint(observer.Position));
            }
        }

        private void StartPrepares() {
            while (_abandoned.Count + CountInState(StreamedCellState.Preparing) < maxConcurrentPrepares) {
                StreamedCell next = NearestIn(StreamedCellState.Queued);
                if (next == null) return;

                BeginPrepare(next);
            }
        }

        private void BeginPrepare(StreamedCell cell) {
            ICellBuilder builder = _builder;
            CellCoord coord = cell.Coord;
            CancellationTokenSource cancellation = new CancellationTokenSource();
            CancellationToken token = cancellation.Token;

            cell.Cancellation = cancellation;
            cell.State = StreamedCellState.Preparing;
            cell.PrepareTask = Task.Run(() => builder.Prepare(coord, token), token);
        }

        private void PrepareQueuedNow(ICellBuilder builder) {
            while (true) {
                StreamedCell next = NearestIn(StreamedCellState.Queued);
                if (next == null) return;

                PrepareNow(builder, next);
            }
        }

        private void PrepareNow(ICellBuilder builder, StreamedCell cell) {
            try {
                cell.Prepared = builder.Prepare(cell.Coord, CancellationToken.None);
                cell.State = StreamedCellState.Prepared;
            } catch (Exception exception) {
                Debug.LogException(exception, this);
                cell.State = StreamedCellState.Failed;
            }
        }

        private void WaitForWorkers() {
            foreach (StreamedCell cell in _cells.Values) {
                if (cell.State == StreamedCellState.Preparing) WaitQuietly(cell.PrepareTask);
            }

            foreach (StreamedCell cell in _abandoned) {
                WaitQuietly(cell.PrepareTask);
            }
        }

        // Faults and cancellations are read back through the task's status by the collectors.
        private static void WaitQuietly(Task task) {
            try {
                task.Wait();
            } catch (AggregateException) {
            }
        }

        private void CollectPrepared() {
            foreach (StreamedCell cell in _cells.Values) {
                if (cell.State != StreamedCellState.Preparing || !cell.PrepareTask.IsCompleted) continue;

                FinishPrepare(cell);
            }
        }

        private void FinishPrepare(StreamedCell cell) {
            Task<object> task = cell.PrepareTask;
            cell.PrepareTask = null;
            DisposeCancellation(cell);

            if (task.Status == TaskStatus.RanToCompletion) {
                cell.Prepared = task.Result;
                cell.State = StreamedCellState.Prepared;
                return;
            }

            if (task.IsFaulted) Debug.LogException(task.Exception?.GetBaseException(), this);

            cell.State = StreamedCellState.Failed;
        }

        // Released cells whose worker was still running: their result is discarded once it lands.
        private void CollectAbandoned() {
            for (int index = _abandoned.Count - 1; index >= 0; index--) {
                StreamedCell cell = _abandoned[index];
                if (!cell.PrepareTask.IsCompleted) continue;

                if (cell.PrepareTask.Status == TaskStatus.RanToCompletion) cell.Prepared = cell.PrepareTask.Result;

                cell.DisposePrepared();
                cell.PrepareTask = null;
                DisposeCancellation(cell);
                _abandoned.RemoveAt(index);
            }
        }

        private void RunInstantiation(CellBuildBudget budget) {
            budget.BeginFrame();

            while (!budget.ShouldYield) {
                if (_instantiating == null && !BeginNextInstantiation(budget)) return;

                StepInstantiation(_instantiating);
            }
        }

        private bool BeginNextInstantiation(CellBuildBudget budget) {
            StreamedCell next = NearestIn(StreamedCellState.Prepared);
            if (next == null) return false;

            next.Root = CreateRoot(next.Coord);
            next.State = StreamedCellState.Instantiating;
            _instantiating = next;

            object prepared = next.Prepared;
            next.Prepared = null;

            try {
                next.BeginSteps(_builder.Instantiate(next, prepared, budget));
            } catch (Exception exception) {
                Debug.LogException(exception, this);
                FailInstantiation(next);
            }

            return true;
        }

        private void StepInstantiation(StreamedCell cell) {
            if (cell == null) return;

            bool running;
            try {
                running = cell.StepBuild();
            } catch (Exception exception) {
                Debug.LogException(exception, this);
                FailInstantiation(cell);
                return;
            }

            if (running) return;

            cell.State = StreamedCellState.Live;
            _instantiating = null;
            CellLoaded?.Invoke(cell);
        }

        private void FailInstantiation(StreamedCell cell) {
            cell.AbandonSteps();
            ReleaseBuilt(cell);
            cell.State = StreamedCellState.Failed;
            if (_instantiating == cell) _instantiating = null;
        }

        private void ReleaseCell(CellCoord coord) {
            if (!_cells.TryGetValue(coord, out StreamedCell cell)) return;

            _cells.Remove(coord);
            if (_instantiating == cell) _instantiating = null;

            StreamedCellState state = cell.State;
            cell.State = StreamedCellState.Released;
            cell.AbandonSteps();
            cell.DisposePrepared();
            ReleaseBuilt(cell);

            if (state == StreamedCellState.Preparing) AbandonPrepare(cell);

            CellUnloaded?.Invoke(coord);
        }

        private void AbandonPrepare(StreamedCell cell) {
            cell.Cancellation?.Cancel();
            _abandoned.Add(cell);
        }

        private void ReleaseBuilt(StreamedCell cell) {
            if (cell.Root == null) return;

            try {
                _builder?.Release(cell);
            } catch (Exception exception) {
                Debug.LogException(exception, this);
            }

            DestroyRoot(cell.Root);
            cell.Root = null;
        }

        private void DestroyRoot(Transform root) {
            if (!_tearingDown) {
                GeneratedPreview.DestroyChild(root);
                return;
            }

            if (Application.isPlaying) {
                Destroy(root.gameObject);
                return;
            }

#if UNITY_EDITOR
            GameObject rootObject = root.gameObject;
            UnityEditor.EditorApplication.delayCall += () => DestroyIfAlive(rootObject);
#endif
        }

        private static void DestroyIfAlive(GameObject target) {
            if (target != null) DestroyImmediate(target);
        }

        private static void DisposeCancellation(StreamedCell cell) {
            cell.Cancellation?.Dispose();
            cell.Cancellation = null;
        }

        private Transform CreateRoot(CellCoord coord) {
            GameObject root = new GameObject($"Cell {coord}");
            if (!Application.isPlaying) root.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

            Transform rootTransform = root.transform;
            rootTransform.SetParent(transform, false);
            rootTransform.localPosition = LocalCenterOf(coord);
            return rootTransform;
        }

        private StreamedCell NearestIn(StreamedCellState state) {
            StreamedCell best = null;
            double bestDistance = double.PositiveInfinity;

            foreach (StreamedCell cell in _cells.Values) {
                if (cell.State != state) continue;

                double distance = PriorityDistance(cell.Coord);
                if (!IsCloser(distance, cell, bestDistance, best)) continue;

                best = cell;
                bestDistance = distance;
            }

            return best;
        }

        // Ties break row-major so equal-distance cells build in a stable order.
        private static bool IsCloser(double distance, StreamedCell cell, double bestDistance, StreamedCell best) {
            if (best == null || distance < bestDistance) return true;
            if (distance > bestDistance) return false;

            return CellInterestSet.CompareRowMajor(cell.Coord, best.Coord) < 0;
        }

        private double PriorityDistance(CellCoord coord) {
            CellSpace space = Space;
            double centerX = space.CenterX(coord);
            double centerZ = space.CenterZ(coord);
            double nearest = double.PositiveInfinity;

            foreach (Vector3 point in _priorityPoints) {
                double deltaX = centerX - point.x;
                double deltaZ = centerZ - point.z;
                nearest = Math.Min(nearest, deltaX * deltaX + deltaZ * deltaZ);
            }

            return double.IsPositiveInfinity(nearest) ? 0.0 : nearest;
        }

        private int CountInState(StreamedCellState state) {
            int count = 0;

            foreach (StreamedCell cell in _cells.Values) {
                if (cell.State == state) count++;
            }

            return count;
        }

        private int CountPending() {
            return CountInState(StreamedCellState.Queued) + CountInState(StreamedCellState.Preparing)
                + CountInState(StreamedCellState.Prepared) + CountInState(StreamedCellState.Instantiating);
        }

        private static int ToWholeCells(double metres, double size, string parameterName) {
            double cells = Math.Round(metres / size);
            if (Math.Abs(cells * size - metres) > OriginShiftToleranceMetres) {
                throw new ArgumentException($"Origin shift {metres} m is not a whole number of {size} m cells.", parameterName);
            }

            return checked((int)cells);
        }
    }
}
