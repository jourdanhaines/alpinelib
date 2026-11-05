using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Sky;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Sky {
    /// <summary>
    /// Draws wind-rolled props (tumbleweeds): streamed cells register their part of the world (<see cref="IRollerCell"/>),
    /// a <see cref="RollerSpawner"/> says which props are out at the wind's drift, each is rolled once across every
    /// registered cell (<see cref="RollerPath"/>, on a worker, steering round stops and following the live wind) and
    /// posed from the drift (<see cref="RollerPose"/>) under the root of the cell it is in now, instanced per kind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Client-side and deterministic: spawns, rolls and poses are pure functions of the world seed, the registered cells
    /// and the drift, so every client with the same session time draws the same rolls; a floating-origin shift moves the
    /// props with their cell roots. Props are kept by spawn id, not by cell, so one rolls on over cell borders and its
    /// spawn cell may unload behind it; it is dropped once the cell it is in is no longer registered (it left the loaded
    /// window) or past its roll. A roll cut short by the window's edge, or built while a neighbour was not loaded, is
    /// rebuilt once the cells settle, resuming from the prop's current pose and scale.
    /// </para>
    /// <para>
    /// What a viewer sees adds three per-client rules: a spawn born in a cell near the camera (not well upwind of it)
    /// is never drawn, a prop whose life would end within the draw distance rolls on until it is out of it
    /// (<see cref="RollerSpawnSettings.OverrunStep"/>), and a moving blocker (<see cref="MovingBlocker"/>, a train) tested
    /// against every built prop, drawn or not, fades one out over <see cref="BlockedFadeSeconds"/> after which its spawn
    /// stays spent for the session. Two clients at one spot draw alike; trains are the one input not of the clock.
    /// </para>
    /// <para>
    /// At most <see cref="RollerSpawnSettings.MaxLive"/> props are drawn, within the draw distance: one drawn stays drawn
    /// until it fades or leaves the distance, and new ones wait for a free slot (earliest spawned first); a prop first
    /// drawn at full size ramps in over <see cref="ShowSeconds"/>. While the wind is not ready (a cold join replaying) the
    /// last drift is held and nothing is drawn before the first ready drift; after drawing nothing while waiting the props
    /// scale in over <see cref="FadeSeconds"/>. Whenever the drift lands more than <see cref="JumpMetres"/> past what the
    /// wind could blow in the real time since the last update, the props glide to the live drift over
    /// <see cref="FadeSeconds"/>. Drawn per camera from <c>beginCameraRendering</c> through
    /// <c>Graphics.RenderMeshInstanced</c> with shadows cast and received, on one layer, never for capture cameras
    /// (<see cref="SkyCameraFilter"/>). Nothing collides or replicates.
    /// </para>
    /// </remarks>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class RollingPropField : MonoBehaviour {
        /// <summary>Most the drift may jump past the wind's own travel before props glide to it rather than snap.</summary>
        public const double JumpMetres = 1.0;

        /// <summary>Seconds props take to glide to the live drift after a jump, or to scale in after a cold join.</summary>
        public const float FadeSeconds = 0.5f;

        /// <summary>Longest step one update advances a glide or fade by, seconds.</summary>
        public const float MaxStepSeconds = 0.1f;

        /// <summary>Session seconds a prop first drawn at full size takes to ramp in.</summary>
        public const float ShowSeconds = 1f;

        // Plane metres past the draw distance a prop is posed before its exact distance is taken (height, root rounding).
        private const double CullSlack = 50.0;

        private const int MaxInstancesPerDraw = 1023;

        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<Material[]> _materials = new List<Material[]>();
        private readonly RollingPropCells _cells = new RollingPropCells();
        private readonly RollingPropSpawns _spawns = new RollingPropSpawns();
        private readonly RollingPropCap _cap = new RollingPropCap();
        private readonly RollingPropBlocking _blocking = new RollingPropBlocking();
        private readonly RollingPropWind _wind = new RollingPropWind();
        private readonly List<RollingPropDrawn> _candidates = new List<RollingPropDrawn>();
        private readonly RollingPropBatch _batch = new RollingPropBatch();
        private int _layer = -1;
        private float _drawDistance = 300f;
        private float _reach = 1f;
        private bool _isConfigured;
        private bool _isSubscribed;
        private bool _isFresh = true;

        /// <summary>The layer every instance draws on.</summary>
        public int Layer => _layer;

        /// <summary>Farthest a prop is drawn from the camera, metres.</summary>
        public float DrawDistance => _drawDistance;

        /// <summary>Kinds added so far.</summary>
        public int KindCount => _meshes.Count;

        /// <summary>Cells registered.</summary>
        public int CellCount => _cells.Count;

        /// <summary>Spawns kept (built, waiting for a build, or found blocked).</summary>
        public int EntryCount => _spawns.Count;

        /// <summary>Spawn ids a moving blocker spent this session (never drawn again).</summary>
        public int SpentCount => _spawns.SpentCount;

        /// <summary>Props drawn by the last <see cref="Peek"/>.</summary>
        public int DrawnCount => _cap.Count;

        /// <summary>Builds queued or running.</summary>
        public int PendingBuilds => _spawns.Builds.Pending;

        /// <summary>The spawner, or null before <see cref="ConfigureSpawns"/>.</summary>
        public RollerSpawner Spawner => _spawns.Spawner;

        /// <summary>Scale-in share applied to every prop: below 1 while they fade in after a cold join.</summary>
        public float Fade => _wind.Fade;

        /// <summary>The drift the props are posed with (held while the wind is not ready).</summary>
        public WindVector Displacement => _wind.Displacement;

        /// <summary>Drift metres along the prevailing wind the props are posed at.</summary>
        public double Drift => Spawner != null ? Spawner.DriftOf(_wind.Displacement) : 0.0;

        /// <summary>True while the props glide from a jumped-over drift to the live one.</summary>
        public bool IsGliding => _wind.IsGliding;

        /// <summary>Session seconds a prop met by a moving blocker takes to fade out.</summary>
        public float BlockedFadeSeconds { get; set; } = 1f;

        /// <summary>Rolls builds inline on the calling thread instead of on a worker (gates and captures).</summary>
        public bool SynchronousBuilds {
            get => _spawns.Builds.IsSynchronous;
            set => _spawns.Builds.IsSynchronous = value;
        }

        /// <summary>
        /// Moving things a prop breaks up against (a train): true when a ball of the radius at the world point would touch
        /// one. Asked for every built prop once per session time, at its centre and a fade's roll ahead; null for none.
        /// </summary>
        public Func<Vector3, float, bool> MovingBlocker { get; set; }

        /// <summary>
        /// Poses props on <paramref name="wind"/>'s drift (still props when null) and draws them on <paramref name="layer"/>
        /// within <paramref name="drawDistance"/> metres of each camera. Keeps kinds, cells and spawns.
        /// </summary>
        public void Configure(IWindService wind, int layer, float drawDistance) {
            if (layer < 0 || layer > 31) throw new ArgumentOutOfRangeException(nameof(layer), layer, "Must be a layer index.");
            if (!(drawDistance > 0f)) throw new ArgumentOutOfRangeException(nameof(drawDistance), drawDistance, "Must be positive.");

            _layer = layer;
            _drawDistance = drawDistance;
            _isConfigured = true;
            _wind.Reset(wind);
            _blocking.Reset();
            _isFresh = true;
        }

        /// <summary>
        /// Spawns props from <paramref name="spawner"/> drawn as <paramref name="looks"/> says (kinds of <see cref="AddKind"/>),
        /// the cells' blocks reaching up to <paramref name="blockReach"/> metres past their borders; null stops spawning.
        /// Forgets every spawn, spent ones included.
        /// </summary>
        public void ConfigureSpawns(RollerSpawner spawner, IRollerLooks looks, double blockReach = 0.0) {
            _spawns.Configure(spawner, looks, blockReach);
            _cap.Clear();
            _blocking.Reset();
            _isFresh = true;
        }

        /// <summary>
        /// Adds a kind drawn as <paramref name="mesh"/> with one instanced material per submesh (not owned) and returns its
        /// index for <see cref="RollerLook.Kind"/>.
        /// </summary>
        public int AddKind(Mesh mesh, Material[] materials) {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (materials == null || materials.Length < mesh.subMeshCount) throw new ArgumentException($"'{mesh.name}' needs a material per submesh.", nameof(materials));

            for (int index = 0; index < mesh.subMeshCount; index++) {
                if (materials[index] == null) throw new ArgumentException($"'{mesh.name}' submesh {index} has no material.", nameof(materials));
                if (!materials[index].enableInstancing) Debug.LogWarning($"RollingPropField::AddKind->'{materials[index].name}' is not instanced; '{mesh.name}' is skipped.", this);
            }

            _meshes.Add(mesh);
            _materials.Add((Material[])materials.Clone());
            _reach = Mathf.Max(_reach, mesh.bounds.extents.magnitude);
            return _meshes.Count - 1;
        }

        /// <summary>Forgets every kind, cell and spawn (and the spawner).</summary>
        public void Clear() {
            _meshes.Clear();
            _materials.Clear();
            _cells.Clear();
            _reach = 1f;
            _spawns.Configure(null, null, 0.0);
            _cap.Clear();
            _blocking.Reset();
            _isFresh = true;
        }

        /// <summary>Registers (or replaces) cell (<paramref name="cellX"/>, <paramref name="cellZ"/>): its root (local origin at the cell's centre) and its world part.</summary>
        public void RegisterCell(long cellX, long cellZ, Transform root, IRollerCell cell) {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (cell == null) throw new ArgumentNullException(nameof(cell));

            _cells.Register(cellX, cellZ, root, cell);
        }

        /// <summary>Drops a cell; false when it was not registered. Props in it are dropped at the next refresh; the last one forgets every spawn (spent ones stay spent).</summary>
        public bool UnregisterCell(long cellX, long cellZ) {
            if (!_cells.Unregister(cellX, cellZ)) return false;
            if (_cells.Count > 0) return true;

            _spawns.Forget();
            _cap.Clear();
            _isFresh = true;
            return true;
        }

        public bool IsCellRegistered(long cellX, long cellZ) {
            return _cells.Contains(cellX, cellZ);
        }

        /// <summary>Waits for every queued build and takes the results in (gates).</summary>
        public void CompleteBuilds() {
            _spawns.Complete(Drift, _wind.Now, _cap);
        }

        /// <summary>True when the last <see cref="Peek"/> drew the spawn <paramref name="id"/>.</summary>
        public bool WasDrawn(ulong id) {
            return _cap.IsDrawn(id);
        }

        /// <summary>True when a moving blocker spent the spawn <paramref name="id"/> this session.</summary>
        public bool IsSpent(ulong id) {
            return _spawns.IsSpent(id);
        }

        /// <summary>True when the spawn <paramref name="id"/> is kept but never drawn here (born near the camera).</summary>
        public bool IsHidden(ulong id) {
            return _spawns.TryGet(id, out RollingPropEntry entry) && entry.IsHidden;
        }

        /// <summary>Session time a moving blocker met the kept spawn <paramref name="id"/>, negative when none has (or not kept).</summary>
        public double BlockedAt(ulong id) {
            return _spawns.TryGet(id, out RollingPropEntry entry) ? entry.BlockedAt : -1.0;
        }

        /// <summary>The built rolls kept now, in spawn order (copied).</summary>
        public List<RollingProp> CopyRolls() {
            var rolls = new List<RollingProp>();
            foreach (RollingPropEntry entry in _spawns.Entries) {
                if (entry.Prop.HasValue) rolls.Add(entry.Prop.Value);
            }
            rolls.Sort((first, second) => RollerSpawner.CompareSpawnOrder(first.Spawn, second.Spawn));
            return rolls;
        }

        /// <summary>
        /// Queues the props for <paramref name="camera"/>'s next render and returns the instanced draws issued: 0 when the
        /// camera is filtered out (<see cref="SkyCameraFilter"/>) or no prop is within the draw distance.
        /// </summary>
        public int Submit(Camera camera) {
            RollingPropBatch batch = Peek(camera);
            if (batch == null || batch.InstanceCount == 0) return 0;

            int draws = 0;
            for (int kind = 0; kind < _meshes.Count; kind++) {
                List<Matrix4x4> matrices = batch.MatricesOf(kind);
                if (matrices.Count == 0) continue;

                draws += Draw(camera, _meshes[kind], _materials[kind], matrices, batch.Bounds);
            }

            return draws;
        }

        /// <summary>
        /// The instances <paramref name="camera"/> would draw now, rebuilt on every call (shared: copy before the next),
        /// or null when it is filtered out or the wind has given no drift yet.
        /// </summary>
        public RollingPropBatch Peek(Camera camera) {
            if (!_isConfigured || _meshes.Count == 0 || !SkyCameraFilter.ShouldDraw(camera, _layer)) return null;

            _wind.Update();
            if (!_wind.HasDisplacement) return null;

            double drift = Drift;
            double now = _wind.Now;
            Vector3 position = camera.transform.position;
            RollingPropViewer viewer = ViewerOf(position);
            _spawns.Refresh(_cells, drift, viewer, now, _cap);
            if (SynchronousBuilds) _spawns.Kick(drift, now, _cap);
            _blocking.Update(_spawns, _cells, drift, now, BlockedFadeSeconds, MovingBlocker);
            Rebuild(position, viewer, drift, now);
            if (!SynchronousBuilds) _spawns.Kick(drift, now, _cap);
            _isFresh = false;
            return _batch;
        }

        private void OnEnable() {
            if (_isSubscribed) return;

            RenderPipelineManager.beginCameraRendering += HandleBeginCamera;
            _isSubscribed = true;
        }

        private void OnDisable() {
            if (!_isSubscribed) return;

            RenderPipelineManager.beginCameraRendering -= HandleBeginCamera;
            _isSubscribed = false;
        }

        private void OnDestroy() {
            Clear();
        }

        private void HandleBeginCamera(ScriptableRenderContext context, Camera camera) {
            Submit(camera);
        }

        // The camera in the absolute plane and the wind's direction now (the live one the rolls follow, or the prevailing).
        private RollingPropViewer ViewerOf(Vector3 position) {
            RollerSpawner spawner = Spawner;
            if (spawner == null || !_cells.TryPlane(position, spawner.CellSize, out double x, out double z)) return default;

            IWindService service = _wind.Service;
            WindVector wind = service != null && service.Field != null && spawner.Clock != null ? service.Field.Direction(service.Time) : spawner.Prevailing;
            return new RollingPropViewer(true, x, z, _drawDistance, wind.X, wind.Z);
        }

        private void Rebuild(Vector3 camera, RollingPropViewer viewer, double drift, double now) {
            _batch.Clear(_meshes.Count);
            CollectCandidates(camera, viewer, drift, now);
            _cap.Select(_candidates, Spawner != null ? Spawner.Spawn.MaxLive : 0, now, _isFresh);
            foreach (RollingPropDrawn candidate in _cap.Selected) AddInstance(candidate, now);
            _batch.CloseBounds(_reach * 2f);
        }

        // Every built, shown prop out now whose cell is loaded and that lies within the draw distance, posed.
        private void CollectCandidates(Vector3 camera, RollingPropViewer viewer, double drift, double now) {
            _candidates.Clear();
            if (Spawner == null || !viewer.HasPlane) return;

            double cellSize = Spawner.CellSize;
            float reachSquared = _drawDistance * _drawDistance;
            double cull = (_drawDistance + CullSlack) * (_drawDistance + CullSlack);
            foreach (RollingPropEntry entry in _spawns.Entries) {
                if (!entry.Prop.HasValue || entry.IsHidden || RollingPropBlocking.ShareOf(entry, now, BlockedFadeSeconds) <= 0f) continue;

                RollerPath path = entry.Prop.Value.Path;
                double rolled = entry.RolledAt(drift);
                if (rolled < 0.0 || rolled > path.Length) continue;

                path.GroundPoint(rolled, out double planeX, out double planeZ);
                if (viewer.DistanceSquaredTo(planeX, planeZ) > cull || !_cells.TryLocal(planeX, planeZ, cellSize, out Transform root, out Vector3 local)) continue;
                if ((root.TransformPoint(local) - camera).sqrMagnitude > reachSquared) continue;

                RollerPose pose = RollerPose.At(path, rolled);
                if (pose.Scale <= 0.0) continue;

                _candidates.Add(new RollingPropDrawn(entry, pose, root));
            }
        }

        // One drawn prop under its cell root, faded by a moving blocker and its ramp.
        private void AddInstance(RollingPropDrawn candidate, double now) {
            RollingPropEntry entry = candidate.Entry;
            RollingProp prop = entry.Prop.Value;
            RollerPose pose = candidate.Pose;
            double cellSize = Spawner.CellSize;
            RollerCellWorld.CellOf(pose.X, pose.Z, cellSize, out long cellX, out long cellZ);
            var local = new Vector3((float)(pose.X - RollerCellWorld.CentreOf(cellX, cellSize)), (float)pose.Y, (float)(pose.Z - RollerCellWorld.CentreOf(cellZ, cellSize)));
            float blocked = RollingPropBlocking.ShareOf(entry, now, BlockedFadeSeconds);
            float scale = (float)pose.Scale * _wind.Fade * prop.Scale * blocked * RollingPropCap.ShownShare(entry, now);
            if (scale <= 0f) return;

            var rotation = new Quaternion((float)pose.RotationX, (float)pose.RotationY, (float)pose.RotationZ, (float)pose.RotationW);
            RollerPath path = prop.Path;
            var size = new Vector3(scale * (float)path.SquashX, scale * (float)path.SquashY, scale * (float)path.SquashZ);
            _batch.Add(prop.Kind, candidate.Root.localToWorldMatrix * Matrix4x4.TRS(local, rotation, size));
        }

        private int Draw(Camera camera, Mesh mesh, Material[] materials, List<Matrix4x4> matrices, Bounds bounds) {
            int draws = 0;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++) {
                Material material = materials[submesh];
                if (material == null || !material.enableInstancing) continue;

                RenderParams parameters = CreateParams(camera, material, bounds);
                for (int start = 0; start < matrices.Count; start += MaxInstancesPerDraw) {
                    Graphics.RenderMeshInstanced(parameters, mesh, submesh, matrices, Math.Min(MaxInstancesPerDraw, matrices.Count - start), start);
                    draws++;
                }
            }

            return draws;
        }

        private RenderParams CreateParams(Camera camera, Material material, Bounds bounds) {
            return new RenderParams(material) {
                camera = camera,
                layer = _layer,
                worldBounds = bounds,
                shadowCastingMode = ShadowCastingMode.On,
                receiveShadows = true,
            };
        }
    }
}
