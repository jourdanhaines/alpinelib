using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Sky;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Sky {
    /// <summary>
    /// Draws wind-rolled props (tumbleweeds): streamed cells register their part of the world (<see cref="IRollerCell"/>),
    /// a <see cref="RollerSpawner"/> says which props are out at the wind's drift, each is rolled once across every
    /// registered cell (<see cref="RollerPath"/>, on a worker) and posed from the drift (<see cref="RollerPose"/>) under
    /// the root of the cell it is in now, instanced per kind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Client-side and deterministic: spawns, rolls and poses are pure functions of the world seed, the registered cells
    /// and the drift, so every client with the same session time draws the same rolls; a floating-origin shift moves the
    /// props with their cell roots. Props are kept by spawn id, not by cell, so one rolls on over cell borders and its
    /// spawn cell may unload behind it; it is dropped once the cell it is in is no longer registered (it left the loaded
    /// window), past its whole life, or once a moving blocker (<see cref="MovingBlocker"/>, a train) has faded it out over
    /// <see cref="BlockedFadeSeconds"/>. A roll cut short by the window's edge is rebuilt when a cell registers.
    /// </para>
    /// <para>
    /// At most <see cref="RollerSpawnSettings.MaxLive"/> props are drawn, the earliest spawned first, only within the draw
    /// distance of the camera; a prop first drawn when already full size (it entered the cap or the window mid-roll)
    /// ramps in over <see cref="ShowSeconds"/>. While the wind is not ready (a cold join replaying) the last drift is held
    /// and nothing is drawn before the first ready drift; after drawing nothing while waiting the props scale in over
    /// <see cref="FadeSeconds"/>. Whenever the drift lands more than <see cref="JumpMetres"/> past what the wind could blow
    /// in the real time since the last update, the props glide to the live drift over <see cref="FadeSeconds"/>. Drawn
    /// per camera from <c>beginCameraRendering</c> through <c>Graphics.RenderMeshInstanced</c> with shadows cast and
    /// received, on one layer, never for capture cameras (<see cref="SkyCameraFilter"/>). Nothing collides or replicates.
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

        /// <summary>Session seconds a prop met by a moving blocker takes to fade out.</summary>
        public const float BlockedFadeSeconds = 1f;

        /// <summary>Session seconds a prop first drawn at full size takes to ramp in.</summary>
        public const float ShowSeconds = 1f;

        // Headroom on the wind's speed × real time a drift step may cover before it counts as a jump (gusts between updates).
        private const double TravelSlack = 2.0;

        // Plane metres past the draw distance a prop is posed before its exact distance is taken (height, root rounding).
        private const double CullSlack = 50.0;

        // Drift metres between spawn refreshes (a prop's first few centimetres are scaled to nothing anyway).
        private const double RefreshDrift = 0.25;

        private const int MaxInstancesPerDraw = 1023;

        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<Material[]> _materials = new List<Material[]>();
        private readonly Dictionary<(long X, long Z), IRollerCell> _cells = new Dictionary<(long X, long Z), IRollerCell>();
        private readonly Dictionary<(long X, long Z), Transform> _roots = new Dictionary<(long X, long Z), Transform>();
        private readonly Dictionary<ulong, RollingPropEntry> _entries = new Dictionary<ulong, RollingPropEntry>();
        private readonly List<RollerSpawn> _alive = new List<RollerSpawn>();
        private readonly List<ulong> _dropped = new List<ulong>();
        private readonly List<RollingPropBuildResult> _results = new List<RollingPropBuildResult>();
        private readonly List<RollingPropDrawn> _candidates = new List<RollingPropDrawn>();
        private readonly HashSet<ulong> _drawn = new HashSet<ulong>();
        private readonly RollingPropBuilds _builds = new RollingPropBuilds();
        private readonly RollingPropBatch _batch = new RollingPropBatch();
        private IWindService _wind;
        private RollerSpawner _spawner;
        private IRollerLooks _looks;
        private RollerCellWorld _world;
        private double _blockReach;
        private int _layer = -1;
        private float _drawDistance = 300f;
        private float _reach = 1f;
        private bool _isConfigured;
        private bool _isSubscribed;
        private WindVector _displacement;
        private WindVector _target;
        private WindVector _glide;
        private bool _hasDisplacement;
        private bool _wasReady;
        private bool _hasWaited;
        private bool _isFresh = true;
        private float _fade = 1f;
        private float _glideShare = 1f;
        private double _lastStep;
        private double _refreshedDrift = double.NaN;
        private int _version;
        private int _refreshedVersion = -1;
        private int _generation;

        /// <summary>The layer every instance draws on.</summary>
        public int Layer => _layer;

        /// <summary>Farthest a prop is drawn from the camera, metres.</summary>
        public float DrawDistance => _drawDistance;

        /// <summary>Kinds added so far.</summary>
        public int KindCount => _meshes.Count;

        /// <summary>Cells registered.</summary>
        public int CellCount => _cells.Count;

        /// <summary>Spawns kept (built, waiting for a build, or found blocked).</summary>
        public int EntryCount => _entries.Count;

        /// <summary>Builds queued or running.</summary>
        public int PendingBuilds => _builds.Pending;

        /// <summary>The spawner, or null before <see cref="ConfigureSpawns"/>.</summary>
        public RollerSpawner Spawner => _spawner;

        /// <summary>Scale-in share applied to every prop: below 1 while they fade in after a cold join.</summary>
        public float Fade => _fade;

        /// <summary>The drift the props are posed with (held while the wind is not ready).</summary>
        public WindVector Displacement => _displacement;

        /// <summary>Drift metres along the prevailing wind the props are posed at.</summary>
        public double Drift => _spawner != null ? _spawner.DriftOf(_displacement) : 0.0;

        /// <summary>True while the props glide from a jumped-over drift to the live one.</summary>
        public bool IsGliding => _glideShare < 1f;

        /// <summary>Rolls builds inline on the calling thread instead of on a worker (gates and captures).</summary>
        public bool SynchronousBuilds {
            get => _builds.IsSynchronous;
            set => _builds.IsSynchronous = value;
        }

        /// <summary>
        /// Moving things a prop breaks up against (a train): true when a ball of the radius at the world point would touch
        /// one. Asked for every drawn prop, at its centre and a fade's roll ahead; null for none.
        /// </summary>
        public Func<Vector3, float, bool> MovingBlocker { get; set; }

        /// <summary>
        /// Poses props on <paramref name="wind"/>'s drift (still props when null) and draws them on <paramref name="layer"/>
        /// within <paramref name="drawDistance"/> metres of each camera. Keeps kinds, cells and spawns.
        /// </summary>
        public void Configure(IWindService wind, int layer, float drawDistance) {
            if (layer < 0 || layer > 31) throw new ArgumentOutOfRangeException(nameof(layer), layer, "Must be a layer index.");
            if (!(drawDistance > 0f)) throw new ArgumentOutOfRangeException(nameof(drawDistance), drawDistance, "Must be positive.");

            _wind = wind;
            _layer = layer;
            _drawDistance = drawDistance;
            _isConfigured = true;
            ResetWind();
        }

        /// <summary>
        /// Spawns props from <paramref name="spawner"/> drawn as <paramref name="looks"/> says (kinds of <see cref="AddKind"/>),
        /// the cells' blocks reaching up to <paramref name="blockReach"/> metres past their borders; null stops spawning.
        /// </summary>
        public void ConfigureSpawns(RollerSpawner spawner, IRollerLooks looks, double blockReach = 0.0) {
            _spawner = spawner;
            _looks = looks;
            _blockReach = Math.Max(0.0, blockReach);
            ForgetSpawns();
            _builds.Configure(spawner, looks);
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
            _roots.Clear();
            _reach = 1f;
            _spawner = null;
            _looks = null;
            ForgetSpawns();
            _builds.Configure(null, null);
        }

        /// <summary>Registers (or replaces) cell (<paramref name="cellX"/>, <paramref name="cellZ"/>): its root (local origin at the cell's centre) and its world part.</summary>
        public void RegisterCell(long cellX, long cellZ, Transform root, IRollerCell cell) {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (cell == null) throw new ArgumentNullException(nameof(cell));

            _cells[(cellX, cellZ)] = cell;
            _roots[(cellX, cellZ)] = root;
            _version++;
        }

        /// <summary>Drops a cell; false when it was not registered. Props in it are dropped at the next refresh.</summary>
        public bool UnregisterCell(long cellX, long cellZ) {
            if (!_cells.Remove((cellX, cellZ))) return false;

            _roots.Remove((cellX, cellZ));
            _version++;
            if (_cells.Count == 0) ForgetSpawns();
            return true;
        }

        public bool IsCellRegistered(long cellX, long cellZ) {
            return _cells.ContainsKey((cellX, cellZ));
        }

        /// <summary>Waits for every queued build and takes the results in (gates).</summary>
        public void CompleteBuilds() {
            _builds.Complete();
            TakeResults();
        }

        /// <summary>True when the last <see cref="Peek"/> drew the spawn <paramref name="id"/>.</summary>
        public bool WasDrawn(ulong id) {
            return _drawn.Contains(id);
        }

        /// <summary>The built rolls kept now, in spawn order (copied).</summary>
        public List<RollingProp> CopyRolls() {
            var rolls = new List<RollingProp>();
            foreach (RollingPropEntry entry in _entries.Values) {
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

            UpdateWind();
            if (!_hasDisplacement) return null;

            Refresh();
            Rebuild(camera.transform.position);
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

        private void ForgetSpawns() {
            _entries.Clear();
            _builds.Clear();
            _generation++;
            _refreshedDrift = double.NaN;
            _refreshedVersion = -1;
            _world = null;
            _isFresh = true;
        }

        private void ResetWind() {
            _displacement = WindVector.Zero;
            _target = WindVector.Zero;
            _glide = WindVector.Zero;
            _hasDisplacement = false;
            _wasReady = false;
            _hasWaited = false;
            _isFresh = true;
            _fade = 1f;
            _glideShare = 1f;
            _lastStep = Time.realtimeSinceStartupAsDouble;
        }

        // Holds the last drift while the wind replays; a cold join scales the props in, any jump glides them over.
        private void UpdateWind() {
            if (_wind == null) {
                _hasDisplacement = true;
                return;
            }

            if (!_wind.IsReady) {
                _wasReady = false;
                _hasWaited |= !_hasDisplacement;
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            double elapsed = Math.Max(0.0, now - _lastStep);
            WindVector target = _wind.Displacement;
            if (!BeginTransition(target, elapsed)) AdvanceTransition(elapsed);
            _lastStep = now;
            _target = target;
            _displacement = target + _glide * (1.0 - SmoothShare(_glideShare));
            _hasDisplacement = true;
            _wasReady = true;
        }

        // A step past the wind's own travel since the last ready update (any after a hold) glides from what was drawn;
        // true when a fade or glide starts here (it advances from the next update on).
        private bool BeginTransition(WindVector target, double elapsed) {
            if (!_hasDisplacement) {
                if (!_hasWaited) return false;

                _fade = 0f;
                return true;
            }

            double travel = _wasReady ? _wind.Velocity.magnitude * elapsed * TravelSlack : 0.0;
            if ((target - _target).Length <= JumpMetres + travel) return false;

            _glide = _displacement - target;
            _glideShare = 0f;
            return true;
        }

        private void AdvanceTransition(double elapsed) {
            float step = (float)Math.Min(MaxStepSeconds, elapsed);
            if (_fade < 1f) _fade = Mathf.Min(1f, _fade + step / FadeSeconds);
            if (_glideShare >= 1f) return;

            _glideShare = Mathf.Min(1f, _glideShare + step / FadeSeconds);
            if (_glideShare >= 1f) _glide = WindVector.Zero;
        }

        private static double SmoothShare(float share) {
            return share * share * (3.0 - 2.0 * share);
        }

        // Takes finished builds in, then (once the drift moved on or the cells changed) adds the props out now, drops the
        // spent ones and queues the builds still needed.
        private void Refresh() {
            TakeResults();
            if (_spawner == null || _looks == null) return;

            double drift = Drift;
            bool cellsChanged = _version != _refreshedVersion;
            if (!cellsChanged && !double.IsNaN(_refreshedDrift) && Math.Abs(drift - _refreshedDrift) < RefreshDrift) return;

            if (cellsChanged || _world == null) _world = new RollerCellWorld(_spawner.CellSize, _cells, _blockReach);
            AddAlive(drift);
            DropSpent(drift);
            QueueBuilds(cellsChanged);
            _refreshedDrift = drift;
            _refreshedVersion = _version;
            _builds.Kick();
            TakeResults();
        }

        private void AddAlive(double drift) {
            _alive.Clear();
            foreach ((long X, long Z) cell in _cells.Keys) _spawner.AliveAt(cell.X, cell.Z, drift, _alive);

            foreach (RollerSpawn spawn in _alive) {
                if (!_entries.ContainsKey(spawn.Id)) _entries.Add(spawn.Id, new RollingPropEntry(spawn));
            }
        }

        // Past its whole life, waiting on an unloaded spawn cell, or out of the loaded window; one spent by a moving
        // blocker is kept (never drawn) until its life is over, so its spawn does not come back.
        private void DropSpent(double drift) {
            _dropped.Clear();
            foreach (KeyValuePair<ulong, RollingPropEntry> pair in _entries) {
                if (IsGone(pair.Value, drift)) _dropped.Add(pair.Key);
            }
            foreach (ulong id in _dropped) _entries.Remove(id);
        }

        private bool IsGone(RollingPropEntry entry, double drift) {
            RollerSpawn spawn = entry.Spawn;
            if (drift >= spawn.EndDrift || drift < spawn.Drift) return true;
            if (entry.IsSpent) return false;
            if (!entry.Prop.HasValue) return !entry.IsBuilt && !_cells.ContainsKey((spawn.CellX, spawn.CellZ));

            RollerPath path = entry.Prop.Value.Path;
            path.GroundPoint(spawn.RolledAt(drift), out double x, out double z);
            RollerCellWorld.CellOf(x, z, _spawner.CellSize, out long cellX, out long cellZ);
            return !_cells.ContainsKey((cellX, cellZ));
        }

        // Unbuilt spawns, and rolls cut by the window's edge once the cells changed.
        private void QueueBuilds(bool cellsChanged) {
            foreach (RollingPropEntry entry in _entries.Values) {
                if (entry.IsQueued) continue;

                bool needsBuild = !entry.IsBuilt || (cellsChanged && entry.Prop.HasValue && entry.Prop.Value.Path.End == RollerEnd.Edge && entry.BuiltVersion != _version);
                if (!needsBuild) continue;

                entry.IsQueued = true;
                _builds.Enqueue(new RollingPropBuildRequest(entry.Spawn, _world, _version, _generation));
            }
        }

        private void TakeResults() {
            _results.Clear();
            _builds.Drain(_results);
            foreach (RollingPropBuildResult result in _results) {
                if (result.Generation != _generation || !_entries.TryGetValue(result.Id, out RollingPropEntry entry)) continue;

                entry.IsQueued = false;
                entry.IsBuilt = true;
                entry.BuiltVersion = result.Version;
                if (result.Prop.HasValue || !entry.Prop.HasValue) entry.Prop = result.Prop;
            }
        }

        private void Rebuild(Vector3 camera) {
            _batch.Clear(_meshes.Count);
            _drawn.Clear();
            CollectCandidates(camera);
            _candidates.Sort((first, second) => RollerSpawner.CompareSpawnOrder(first.Entry.Spawn, second.Entry.Spawn));
            int most = _spawner != null ? _spawner.Spawn.MaxLive : 0;
            double now = Now();
            foreach (RollingPropDrawn candidate in _candidates) {
                if (_drawn.Count >= most) break;
                if (AddCandidate(candidate, now)) _drawn.Add(candidate.Entry.Spawn.Id);
            }
            _batch.CloseBounds(_reach * 2f);
        }

        // Session seconds of the wind (real seconds without one): the runtime fades follow the clock the drift does.
        private double Now() {
            return _wind != null ? _wind.Time : Time.realtimeSinceStartupAsDouble;
        }

        // Every built prop out now whose cell is loaded and that lies within the draw distance, posed.
        private void CollectCandidates(Vector3 camera) {
            _candidates.Clear();
            if (_spawner == null) return;

            double drift = Drift;
            float reachSquared = _drawDistance * _drawDistance;
            if (!TryCameraPlane(camera, out double cameraX, out double cameraZ)) return;

            double cull = (_drawDistance + CullSlack) * (_drawDistance + CullSlack);
            foreach (RollingPropEntry entry in _entries.Values) {
                if (!entry.Prop.HasValue || entry.IsSpent) continue;

                RollingProp prop = entry.Prop.Value;
                double rolled = entry.Spawn.RolledAt(drift);
                if (rolled < 0.0 || rolled > prop.Path.Length) continue;

                prop.Path.GroundPoint(rolled, out double planeX, out double planeZ);
                double dx = planeX - cameraX, dz = planeZ - cameraZ;
                if (dx * dx + dz * dz > cull || !TryLocal(prop.Path, rolled, out Transform root, out Vector3 local)) continue;

                Vector3 world = root.TransformPoint(local);
                if ((world - camera).sqrMagnitude > reachSquared) continue;

                RollerPose pose = RollerPose.At(prop.Path, rolled);
                if (pose.Scale <= 0.0) continue;

                _candidates.Add(new RollingPropDrawn(entry, pose, root));
            }
        }

        // The camera's position in the absolute plane, through any live cell root (roots only translate).
        private bool TryCameraPlane(Vector3 camera, out double x, out double z) {
            foreach (KeyValuePair<(long X, long Z), Transform> pair in _roots) {
                if (pair.Value == null) continue;

                Vector3 origin = pair.Value.position;
                x = RollerCellWorld.CentreOf(pair.Key.X, _spawner.CellSize) + (camera.x - origin.x);
                z = RollerCellWorld.CentreOf(pair.Key.Z, _spawner.CellSize) + (camera.z - origin.z);
                return true;
            }
            x = z = 0.0;
            return false;
        }

        // The registered root of the cell the roll is in after `rolled` metres and the centre in its local plane.
        private bool TryLocal(RollerPath path, double rolled, out Transform root, out Vector3 local) {
            path.GroundPoint(rolled, out double x, out double z);
            RollerCellWorld.CellOf(x, z, _spawner.CellSize, out long cellX, out long cellZ);
            local = new Vector3((float)(x - RollerCellWorld.CentreOf(cellX, _spawner.CellSize)), 0f, (float)(z - RollerCellWorld.CentreOf(cellZ, _spawner.CellSize)));
            return _roots.TryGetValue((cellX, cellZ), out root) && root != null && root.gameObject.activeInHierarchy;
        }

        // Adds one prop under its cell root, faded by a moving blocker and ramped in when first drawn at full size; false when not drawn.
        private bool AddCandidate(RollingPropDrawn candidate, double now) {
            RollingPropEntry entry = candidate.Entry;
            RollingProp prop = entry.Prop.Value;
            RollerPose pose = candidate.Pose;
            Matrix4x4 toWorld = candidate.Root.localToWorldMatrix;
            RollerCellWorld.CellOf(pose.X, pose.Z, _spawner.CellSize, out long cellX, out long cellZ);
            var local = new Vector3((float)(pose.X - RollerCellWorld.CentreOf(cellX, _spawner.CellSize)), (float)pose.Y,
                (float)(pose.Z - RollerCellWorld.CentreOf(cellZ, _spawner.CellSize)));
            float blocked = BlockedShare(entry, prop, toWorld.MultiplyPoint3x4(local), now);
            if (blocked <= 0f) return false;

            float scale = (float)pose.Scale * _fade * prop.Scale * blocked * ShownShare(entry, pose, now);
            if (scale <= 0f) return true;

            var rotation = new Quaternion((float)pose.RotationX, (float)pose.RotationY, (float)pose.RotationZ, (float)pose.RotationW);
            RollerPath path = prop.Path;
            var size = new Vector3(scale * (float)path.SquashX, scale * (float)path.SquashY, scale * (float)path.SquashZ);
            _batch.Add(prop.Kind, toWorld * Matrix4x4.TRS(local, rotation, size));
            return true;
        }

        // 1 until a moving blocker meets the prop (at its centre or a fade's roll ahead), then down to 0 over the blocked
        // fade, after which the prop is spent.
        private float BlockedShare(RollingPropEntry entry, RollingProp prop, Vector3 world, double now) {
            if (entry.BlockedAt < 0.0 && MovingBlocker != null && IsMetByBlocker(entry, prop, world)) entry.BlockedAt = now;
            if (entry.BlockedAt < 0.0) return 1f;

            float share = 1f - (float)((now - entry.BlockedAt) / BlockedFadeSeconds);
            if (share > 0f) return Mathf.Min(1f, share);

            entry.IsSpent = true;
            return 0f;
        }

        private bool IsMetByBlocker(RollingPropEntry entry, RollingProp prop, Vector3 world) {
            float radius = (float)prop.Path.Radius;
            if (MovingBlocker(world, radius)) return true;

            double ahead = entry.Spawn.RolledAt(Drift) + prop.Path.GroundSpeedShare * _spawner.Paths.FadeDrift;
            if (ahead > prop.Path.Length || !TryLocal(prop.Path, ahead, out Transform root, out Vector3 local)) return false;

            local.y = world.y - root.position.y;
            return MovingBlocker(root.TransformPoint(local), radius);
        }

        // A prop first drawn while still scaling in, or on the first draw after a (re)configure, needs no ramp.
        private float ShownShare(RollingPropEntry entry, RollerPose pose, double now) {
            if (double.IsNaN(entry.ShownAt)) entry.ShownAt = _isFresh || pose.Scale < 1.0 ? double.NegativeInfinity : now;
            if (double.IsNegativeInfinity(entry.ShownAt)) return 1f;

            return Mathf.Clamp01((float)((now - entry.ShownAt) / ShowSeconds));
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
