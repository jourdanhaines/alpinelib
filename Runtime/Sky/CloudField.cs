using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Meshing;
using AlpineLib.Procedural.Sky;
using AlpineLib.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Sky {
    /// <summary>
    /// Draws the drifting cloud field: the clouds of <see cref="CloudFieldScatter"/> on one wind-frame lattice per class,
    /// each sliding over the world by its class's share of the wind's drift (big clouds slower), each cloud wandering
    /// and surging a little about its anchor, in a window of field cells around each camera, condensing in and
    /// evaporating out only past <see cref="CloudFieldSettings.FullRadius"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every client with the same seed, drift and session time draws the same sky: a cloud's absolute position is
    /// its anchor plus its class's share of the drift plus its wander and surge, and its world position is the camera's plus that minus the
    /// camera's absolute position (<see cref="CloudFieldFrame"/>, in double), so a floating-origin shift moves nothing.
    /// </para>
    /// <para>
    /// While the wind is not ready (a cold join replaying) the last drift is held; when the first ready drift lands
    /// more than <see cref="JumpMetres"/> from it the clouds glide from the held drift to the live one over
    /// <see cref="FadeSeconds"/>, and after drawing nothing while waiting they condense in over it instead of popping
    /// (steps clamped to <see cref="MaxStepSeconds"/>, so a hitch cannot skip either). Per-cell clouds are pure data,
    /// built on demand into pooled cells (with their offsets at the current and next motion tick) and released once no
    /// camera's window holds the cell; cells wholly past the window's radius are never built.
    /// </para>
    /// <para>
    /// Shadow footprints are one disc per puff, or one disc enclosing every puff for classes that shadow as one
    /// (<see cref="CloudClassSettings.OneShadowDisc"/>).
    /// </para>
    /// <para>
    /// Past <see cref="CloudFieldSettings.FarMeshRadius"/> a cloud draws its coarse mesh (same puffs, same matrix); each
    /// camera's batch keeps every cloud's choice while it is inside the band about that radius, so none flickers.
    /// </para>
    /// <para>
    /// Instanced through <c>Graphics.RenderMeshInstanced</c> per camera from <c>beginCameraRendering</c>, on one layer,
    /// shadows off both ways, no probes, no motion vectors; <see cref="SkyCameraFilter"/> keeps it out of capture
    /// cameras. Draws through the material asset itself when its <c>_FogScale</c> already matches (the asset must
    /// carry the sky keywords, <see cref="HasCloudKeywords"/>), else through a clone differing only in it.
    /// </para>
    /// </remarks>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class CloudField : MonoBehaviour {
        /// <summary>Most the drift may jump when the wind turns ready before clouds glide to it rather than snap.</summary>
        public const double JumpMetres = 1.0;

        /// <summary>Seconds clouds take to glide to the live drift after a jump, or to condense in after a cold join.</summary>
        public const float FadeSeconds = 0.5f;

        /// <summary>Longest step one update advances a glide or fade by, seconds.</summary>
        public const float MaxStepSeconds = 0.1f;

        private const int StaleBatchFrames = 120;

        // The tick after next is sampled over this many frames (a 0.25 s tick lasts 15 at 60 fps), at least this many clouds a frame.
        private const int AheadFrames = 8;
        private const int AheadMinClouds = 64;
        private const int LookaheadCellsPerFrame = 1;

        private readonly Dictionary<int, CloudBatch> _batches = new Dictionary<int, CloudBatch>();
        private readonly Dictionary<(CloudClass Class, long X, long Z), CloudCell> _cells = new Dictionary<(CloudClass Class, long X, long Z), CloudCell>();
        private readonly Stack<CloudCell> _cellPool = new Stack<CloudCell>();
        private readonly Vector3[][] _shadowCentres = new Vector3[CloudFieldSettings.MeshCount][];
        private readonly float[][] _shadowRadii = new float[CloudFieldSettings.MeshCount][];
        private readonly float[] _shadowSpan = new float[CloudFieldSettings.MeshCount];
        private readonly float[] _shadowRadius = new float[CloudFieldSettings.MeshCount];
        private readonly List<(CloudClass Class, long X, long Z)> _releasable = new List<(CloudClass Class, long X, long Z)>();
        private readonly List<int> _staleCameras = new List<int>();
        private readonly HashSet<int> _warnedFarClip = new HashSet<int>();
        private CloudFieldScatter _scatter;
        private SkyMeshVariants _meshes;
        private IAbsoluteFrame _frame;
        private IWindService _wind;
        private Material _template;
        private Material _material;
        private float _fogScale;
        private int _layer;
        private int _revision;
        private float _reach;
        private bool _isSubscribed;
        private WindVector _displacement;
        private WindVector _glide;
        private double _time;
        private long _tick = long.MinValue;
        private WindVector _tickHeading;
        private WindVector _nextHeading;
        private WindVector _aheadHeading;
        private readonly List<CloudCell> _aheadQueue = new List<CloudCell>();
        private long _aheadTick = long.MinValue;
        private int _aheadBudget;
        private readonly Queue<(CloudClass Class, long X, long Z)> _lookahead = new Queue<(CloudClass Class, long X, long Z)>();
        private double _fullRadius;
        private double _lastStep;
        private bool _hasDisplacement;
        private bool _wasReady;
        private bool _hasWaited;
        private float _fade = 1f;
        private float _glideShare = 1f;
        private int _lastSweepFrame;

        /// <summary>The scatter the field draws, or null before <see cref="Configure"/>.</summary>
        public CloudFieldScatter Scatter => _scatter;

        /// <summary>The material clouds draw through: the asset itself or a clone (do not mutate either).</summary>
        public Material Material => _material;

        /// <summary>The layer every instance draws on.</summary>
        public int Layer => _layer;

        /// <summary>Field cells whose clouds are cached, across every class's lattice.</summary>
        public int CachedCellCount => _cells.Count;

        /// <summary>Condensing share applied to every cloud: below 1 while the clouds fade in after a wind jump.</summary>
        public float Fade => _fade;

        /// <summary>The frame clouds are placed through (absolute positions), or null for world positions.</summary>
        public IAbsoluteFrame Frame => _frame;

        /// <summary>World y of the ground clouds float over.</summary>
        public float GroundY => _frame != null ? _frame.GroundY : 0f;

        /// <summary>The drift the clouds are placed with (held while the wind is not ready, gliding after a jump).</summary>
        public WindVector Displacement => _displacement;

        /// <summary>True while the clouds glide from a held drift to the live one.</summary>
        public bool IsGliding => _glideShare < 1f;

        /// <summary>
        /// Draws <paramref name="scatter"/> with the cloud meshes of <paramref name="meshes"/> (not owned) through
        /// <paramref name="material"/> fogged at <paramref name="fogScale"/>, on <paramref name="layer"/>, placed through
        /// <paramref name="frame"/> and drifting on <paramref name="wind"/> (a still sky when null).
        /// </summary>
        public void Configure(CloudFieldScatter scatter, SkyMeshVariants meshes, Material material, float fogScale, int layer, IAbsoluteFrame frame, IWindService wind) {
            if (material == null) throw new ArgumentNullException(nameof(material));

            _scatter = scatter ?? throw new ArgumentNullException(nameof(scatter));
            _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
            if (!meshes.HasClouds) throw new ArgumentException("The sky meshes have no clouds built.", nameof(meshes));

            _frame = frame;
            _wind = wind;
            _layer = layer;
            DestroyClone();
            _template = material;
            _fogScale = fogScale;
            if (!HasCloudKeywords(material)) Debug.LogWarning($"CloudField::Configure->'{material.name}' lacks the sky keywords; player builds strip variants switched on at runtime.", this);
            EnsureMaterial();
            _reach = (float)LargestExtent(meshes, scatter.Settings);
            BuildShadowDiscs(meshes, scatter.Settings);
            ResetWind();
            Invalidate();
        }

        /// <summary>
        /// True when <paramref name="material"/> is instanced and carries <c>_FLAT_SHADING</c>, <c>_NO_LIGHT_COOKIE</c>,
        /// <c>_RECEIVE_SHADOWS_OFF</c>, <c>_FOG_RADIAL</c> (so a cloud is as fogged at the screen's edge as dead ahead) and
        /// <c>_FOG_SKY</c> (so a fogged cloud melts into the sky behind it at any height, not into the horizon haze).
        /// </summary>
        public static bool HasCloudKeywords(Material material) {
            if (material == null || !material.enableInstancing) return false;

            return material.IsKeywordEnabled(StylizedLitShader.FlatShadingKeyword)
                && material.IsKeywordEnabled(StylizedLitShader.NoLightCookieKeyword)
                && material.IsKeywordEnabled(StylizedLitShader.ReceiveShadowsOffKeyword)
                && material.IsKeywordEnabled(StylizedLitShader.FogRadialKeyword)
                && material.IsKeywordEnabled(StylizedLitShader.FogSkyKeyword);
        }

        /// <summary>Sets the fog distance scale the clouds draw with (reviews), through a clone unless the asset already matches.</summary>
        public void SetFogScale(float fogScale) {
            _fogScale = fogScale;
            DestroyClone();
            EnsureMaterial();
        }

        /// <summary>
        /// Queues the clouds for <paramref name="camera"/>'s next render and returns the instanced draws issued: 0 when
        /// the camera is filtered out (<see cref="SkyCameraFilter"/>) or no cloud is in its window.
        /// </summary>
        public int Submit(Camera camera) {
            CloudBatch batch = Peek(camera);
            if (batch == null) return 0;

            EnsureMaterial();
            WarnFarClip(camera);
            int draws = 0;
            for (int slot = 0; slot < CloudBatch.SlotCount; slot++) {
                List<Matrix4x4> matrices = batch.MatricesOf(slot);
                if (matrices.Count == 0) continue;

                Graphics.RenderMeshInstanced(CreateParams(camera, batch.Bounds), MeshOf(slot), 0, matrices);
                draws++;
            }

            return draws;
        }

        /// <summary>The mesh draw slot <paramref name="slot"/> of a <see cref="CloudBatch"/> draws: a cloud's fine mesh or its coarse far one.</summary>
        public Mesh MeshOf(int slot) {
            int meshIndex = CloudBatch.MeshOfSlot(slot);
            return CloudBatch.IsFarSlot(slot) ? _meshes.FarCloud(meshIndex) : _meshes.Cloud(meshIndex);
        }

        /// <summary>The instances <paramref name="camera"/> would draw now (rebuilt when anything moved), or null when it is filtered out or nothing is ready.</summary>
        public CloudBatch Peek(Camera camera) {
            if (!IsReady || !SkyCameraFilter.ShouldDraw(camera, _layer)) return null;

            UpdateWind();
            if (!_hasDisplacement) return null;

            CloudBatch batch = BatchFor(camera);
            BuildLookahead();
            SweepStaleBatches();
            return batch;
        }

        /// <summary>
        /// Fills <paramref name="footprints"/> (cleared first, no allocation once it has grown) with the shadow footprints
        /// of every cloud <paramref name="camera"/> draws now: world centre and horizontal radius, scaled with the cloud.
        /// Empty when the camera is filtered out.
        /// </summary>
        public void CopyFootprints(Camera camera, List<CloudFootprint> footprints) {
            CopyFootprints(camera, footprints, Matrix4x4.identity, Vector2.zero, float.PositiveInfinity, 1f);
        }

        /// <summary>
        /// Like <see cref="CopyFootprints(Camera, List{CloudFootprint})"/>, but with centres in the space of the rigid
        /// <paramref name="worldToSpace"/> and only for clouds whose footprints, radii grown by <paramref name="radiusScale"/>,
        /// can reach the square of half-size <paramref name="halfSize"/> about <paramref name="centre"/> (that space's x, y):
        /// one transform per cloud, and no puff of a cloud wholly outside is expanded.
        /// </summary>
        public void CopyFootprints(Camera camera, List<CloudFootprint> footprints, Matrix4x4 worldToSpace, Vector2 centre, float halfSize, float radiusScale) {
            if (footprints == null) throw new ArgumentNullException(nameof(footprints));

            footprints.Clear();
            CloudBatch batch = Peek(camera);
            if (batch == null) return;

            for (int slot = 0; slot < CloudBatch.SlotCount; slot++) {
                int meshIndex = CloudBatch.MeshOfSlot(slot);
                float span = _shadowSpan[meshIndex] + _shadowRadius[meshIndex] * radiusScale;
                List<Vector4> centres = batch.CentresOf(slot);
                for (int index = 0; index < centres.Count; index++) {
                    Vector4 cloud = centres[index];
                    Vector3 at = worldToSpace.MultiplyPoint3x4(new Vector3(cloud.x, cloud.y, cloud.z));
                    float reach = halfSize + span * cloud.w;
                    if (Mathf.Abs(at.x - centre.x) > reach || Mathf.Abs(at.y - centre.y) > reach) continue;

                    AppendFootprints(worldToSpace * batch.MatricesOf(slot)[index], cloud.w, meshIndex, footprints);
                }
            }
        }

        private void AppendFootprints(Matrix4x4 toSpace, float scale, int meshIndex, List<CloudFootprint> footprints) {
            Vector3[] centres = _shadowCentres[meshIndex];
            float[] radii = _shadowRadii[meshIndex];
            for (int index = 0; index < centres.Length; index++) footprints.Add(new CloudFootprint(toSpace.MultiplyPoint3x4(centres[index]), radii[index] * scale));
        }

        /// <summary>Drops every cached cell and camera batch, so the next draw rebuilds.</summary>
        public void Invalidate() {
            foreach (CloudCell cell in _cells.Values) RecycleCell(cell);
            _cells.Clear();
            _batches.Clear();
            _lookahead.Clear();
            _aheadQueue.Clear();
            _aheadTick = long.MinValue;
            _revision++;
        }

        private bool IsReady => _scatter != null && _meshes != null && !_meshes.IsDisposed && _meshes.HasClouds && _template != null;

        private void OnEnable() {
            if (_isSubscribed) return;

            RenderPipelineManager.beginCameraRendering += HandleBeginCamera;
            _isSubscribed = true;
        }

        // Edit mode frees its clone here (domain reloads and scene saves skip OnDestroy); the next draw rebuilds it.
        private void OnDisable() {
            if (!Application.isPlaying) DestroyClone();
            if (!_isSubscribed) return;

            RenderPipelineManager.beginCameraRendering -= HandleBeginCamera;
            _isSubscribed = false;
        }

        private void OnDestroy() {
            DestroyClone();
            _template = null;
            _batches.Clear();
            _cells.Clear();
            _cellPool.Clear();
        }

        private void HandleBeginCamera(ScriptableRenderContext context, Camera camera) {
            Submit(camera);
        }

        private void ResetWind() {
            _displacement = WindVector.Zero;
            _glide = WindVector.Zero;
            _time = 0.0;
            _tick = long.MinValue;
            _hasDisplacement = false;
            _wasReady = false;
            _hasWaited = false;
            _fade = 1f;
            _glideShare = 1f;
        }

        // Holds the last drift while the wind replays; the first ready drift after a wait or a jump glides or condenses the clouds in.
        private void UpdateWind() {
            if (_wind == null) {
                _hasDisplacement = true;
                _time = 0.0;
                UpdateTick(null);
                return;
            }

            _time = _wind.Time;
            UpdateTick(_wind.Field);
            if (!_wind.IsReady) {
                _wasReady = false;
                _hasWaited |= !_hasDisplacement;
                return;
            }

            WindVector target = _wind.Displacement;
            if (!_wasReady) BeginTransition(target);
            _wasReady = true;
            AdvanceTransition();
            _displacement = target + _glide * (1.0 - SmoothShare(_glideShare));
            _hasDisplacement = true;
        }

        // The wind headings of the motion tick the time falls in and the next, which the cells' offsets are sampled under.
        private void UpdateTick(WindField field) {
            long tick = CloudFieldScatter.MotionTick(_time);
            if (tick == _tick) return;

            bool isStep = tick == _tick + 1;
            _tickHeading = isStep ? _nextHeading : CloudFieldScatter.HeadingAt(field, tick);
            _nextHeading = isStep ? _aheadHeading : CloudFieldScatter.HeadingAt(field, tick + 1);
            _aheadHeading = CloudFieldScatter.HeadingAt(field, tick + 2);
            _tick = tick;
        }

        // A cold join that drew nothing condenses in; a drawn sky further than the jump limit away glides from where it is.
        private void BeginTransition(WindVector target) {
            _lastStep = Time.realtimeSinceStartupAsDouble;
            if (!_hasDisplacement) {
                if (_hasWaited) _fade = 0f;
                return;
            }

            WindVector jump = _displacement - target;
            if (jump.Length <= JumpMetres) return;

            _glide = jump;
            _glideShare = 0f;
        }

        // Several cameras in one frame step by the time between them (about nothing), so neither runs ahead.
        private void AdvanceTransition() {
            double now = Time.realtimeSinceStartupAsDouble;
            float step = (float)Math.Min(MaxStepSeconds, Math.Max(0.0, now - _lastStep));
            _lastStep = now;
            if (_fade < 1f) _fade = Mathf.Min(1f, _fade + step / FadeSeconds);
            if (_glideShare >= 1f) return;

            _glideShare = Mathf.Min(1f, _glideShare + step / FadeSeconds);
            if (_glideShare >= 1f) _glide = WindVector.Zero;
        }

        private static double SmoothShare(float share) {
            return share * share * (3.0 - 2.0 * share);
        }

        private CloudBatch BatchFor(Camera camera) {
            int id = camera.GetInstanceID();
            if (!_batches.TryGetValue(id, out CloudBatch batch)) {
                batch = new CloudBatch();
                _batches.Add(id, batch);
            }

            batch.LastUsedFrame = Time.frameCount;
            Vector3 world = camera.transform.position;
            ResolveAbsolute(world, out double absoluteX, out double absoluteZ);
            if (!NeedsRebuild(batch, world, absoluteX, absoluteZ)) return batch;

            Rebuild(batch, world, absoluteX, absoluteZ);
            return batch;
        }

        private void ResolveAbsolute(Vector3 world, out double absoluteX, out double absoluteZ) {
            if (_frame != null && _frame.TryToAbsolute(world, out absoluteX, out absoluteZ)) return;

            absoluteX = world.x;
            absoluteZ = world.z;
        }

        private bool NeedsRebuild(CloudBatch batch, Vector3 world, double absoluteX, double absoluteZ) {
            if (!batch.IsBuilt || batch.Revision != _revision || batch.CameraWorld != world) return true;
            if (batch.AbsoluteX != absoluteX || batch.AbsoluteZ != absoluteZ) return true;

            return !batch.Displacement.Equals(_displacement) || batch.Time != _time || batch.Fade != _fade;
        }

        private void Rebuild(CloudBatch batch, Vector3 world, double absoluteX, double absoluteZ) {
            bool hasMoved = !batch.IsBuilt || batch.Revision != _revision;
            float groundY = _frame != null ? _frame.GroundY : 0f;
            batch.Clear();
            batch.WindowCells = _scatter.Settings.WindowCells;
            _fullRadius = _scatter.Settings.FullRadius;
            for (int classIndex = 0; classIndex < CloudFieldSettings.ClassCount; classIndex++) {
                var cloudClass = (CloudClass)classIndex;
                CloudFieldFrame frame = CloudFieldFrame.Locate(absoluteX, absoluteZ, _scatter.DriftOf(cloudClass, _displacement), _scatter.Settings.CellSize);
                bool hasClassMoved = hasMoved || batch.CellXOf(cloudClass) != frame.CellX || batch.CellZOf(cloudClass) != frame.CellZ;
                hasMoved |= hasClassMoved;
                batch.SetCell(cloudClass, frame.CellX, frame.CellZ);
                AddWindow(batch, cloudClass, frame, world, groundY);
                if (hasClassMoved) QueueLookahead(cloudClass, frame);
            }

            PrepareAhead();
            batch.CloseBounds(new Vector3(world.x, groundY, world.z), _reach);
            batch.CameraWorld = world;
            batch.AbsoluteX = absoluteX;
            batch.AbsoluteZ = absoluteZ;
            batch.Displacement = _displacement;
            batch.Time = _time;
            batch.Fade = _fade;
            batch.Revision = _revision;
            batch.IsBuilt = true;
            if (!hasMoved) return;

            batch.DropBandsOutside();
            ReleaseCells();
        }

        // Cells of the ring just past the window (within one cell of its radius) that are not built yet: built a few a
        // frame, so a cell is ready before the camera's drift brings it into the window and no frame builds a whole row.
        private void QueueLookahead(CloudClass cloudClass, CloudFieldFrame frame) {
            int ring = _scatter.Settings.WindowCells + CloudBatch.LookaheadCells;
            double inner = _scatter.Settings.WindowRadius;
            double outer = inner + _scatter.Settings.CellSize;
            for (long cellZ = frame.CellZ - ring; cellZ <= frame.CellZ + ring; cellZ++) {
                double nearZ = NearestAcross(frame.OffsetZ(cellZ, 0.0));
                for (long cellX = frame.CellX - ring; cellX <= frame.CellX + ring; cellX++) {
                    double nearX = NearestAcross(frame.OffsetX(cellX, 0.0));
                    double near = nearX * nearX + nearZ * nearZ;
                    if (near < inner * inner || near >= outer * outer || _cells.ContainsKey((cloudClass, cellX, cellZ))) continue;

                    _lookahead.Enqueue((cloudClass, cellX, cellZ));
                }
            }
        }

        private void BuildLookahead() {
            int built = 0;
            while (_lookahead.Count > 0 && built < LookaheadCellsPerFrame) {
                (CloudClass Class, long X, long Z) key = _lookahead.Dequeue();
                if (_cells.ContainsKey(key) || !IsHeld(key)) continue;

                CellOf(key.Class, key.X, key.Z);
                built++;
            }
        }

        /// <summary>Lookahead cells queued but not built yet.</summary>
        public int PendingCellCount => _lookahead.Count;

        // Samples the tick after next for a share of the cached cells each frame, so a tick change costs a shift, not a
        // sample of every cloud; cells left over when the tick turns sample in Prepare as before (same values either way).
        private void PrepareAhead() {
            if (_aheadTick != _tick) {
                _aheadTick = _tick;
                _aheadQueue.Clear();
                int clouds = 0;
                foreach (CloudCell cell in _cells.Values) {
                    _aheadQueue.Add(cell);
                    clouds += cell.Clouds.Count;
                }
                _aheadBudget = Math.Max(AheadMinClouds, clouds / AheadFrames + 1);
            }

            int sampled = 0;
            while (_aheadQueue.Count > 0 && sampled < _aheadBudget) {
                CloudCell cell = _aheadQueue[_aheadQueue.Count - 1];
                _aheadQueue.RemoveAt(_aheadQueue.Count - 1);
                sampled += cell.PrepareAhead(_scatter, _tick + 2, _aheadHeading);
            }
        }

        // Every cell of the class's window that comes within the window's radius (its clouds never leave it).
        private void AddWindow(CloudBatch batch, CloudClass cloudClass, CloudFieldFrame frame, Vector3 world, float groundY) {
            int window = _scatter.Settings.WindowCells;
            double share = CloudFieldScatter.TickShare(_time, _tick);
            for (long cellZ = frame.CellZ - window; cellZ <= frame.CellZ + window; cellZ++) {
                double nearZ = NearestAcross(frame.OffsetZ(cellZ, 0.0));
                for (long cellX = frame.CellX - window; cellX <= frame.CellX + window; cellX++) {
                    if (!IsInWindow(NearestAcross(frame.OffsetX(cellX, 0.0)), nearZ)) continue;

                    AddCell(batch, cloudClass, frame, cellX, cellZ, share, world, groundY);
                }
            }
        }

        // Distance from the camera to a cell's span along one axis, given the offset of its minimum edge.
        private double NearestAcross(double edge) {
            if (edge > 0.0) return edge;

            double far = edge + _scatter.Settings.CellSize;
            return far < 0.0 ? -far : 0.0;
        }

        private bool IsInWindow(double nearX, double nearZ) {
            double radius = _scatter.Settings.WindowRadius;
            return nearX * nearX + nearZ * nearZ < radius * radius;
        }

        private void AddCell(CloudBatch batch, CloudClass cloudClass, CloudFieldFrame frame, long cellX, long cellZ, double share, Vector3 world, float groundY) {
            CloudCell cell = CellOf(cloudClass, cellX, cellZ);
            cell.Prepare(_scatter, _tick, _tickHeading, _nextHeading);
            double reach = _scatter.Settings.WindowRadius + _scatter.Settings.ClassOf(cloudClass).MotionReach;
            List<CloudInstance> clouds = cell.Clouds;
            byte[] bands = batch.BandsOf(cloudClass, cellX, cellZ, clouds.Count);
            CloudFieldSettings settings = _scatter.Settings;
            for (int index = 0; index < clouds.Count; index++) {
                CloudInstance cloud = clouds[index];
                double offsetX = frame.OffsetX(cellX, cloud.AnchorX);
                double offsetZ = frame.OffsetZ(cellZ, cloud.AnchorZ);
                if (offsetX * offsetX + offsetZ * offsetZ >= reach * reach) continue;

                cell.OffsetOf(index, share, out double moveX, out double moveZ);
                offsetX += moveX;
                offsetZ += moveZ;
                double distance = Math.Sqrt(offsetX * offsetX + offsetZ * offsetZ);
                bool isFar = CloudBatch.ResolveFar(settings, bands, index, distance);
                AddCloud(batch, cloud, offsetX, offsetZ, distance, isFar, world, groundY);
            }
        }

        // Shrunk about its flat base by its presence and the fade; turned by its cached heading; coarse past the far mesh band.
        private void AddCloud(CloudBatch batch, in CloudInstance cloud, double offsetX, double offsetZ, double distance, bool isFar, Vector3 world, float groundY) {
            double presence = (distance <= _fullRadius ? 1.0 : _scatter.Presence(distance)) * _fade;
            if (presence <= 0.0) return;

            float scale = (float)(cloud.Scale * presence);
            float cos = (float)cloud.YawCos * scale;
            float sin = (float)cloud.YawSin * scale;
            var matrix = new Matrix4x4 {
                m00 = cos, m02 = sin, m11 = scale, m20 = -sin, m22 = cos, m33 = 1f,
                m03 = world.x + (float)offsetX, m13 = groundY + (float)cloud.Altitude, m23 = world.z + (float)offsetZ,
            };
            batch.Add(isFar ? cloud.MeshIndex + CloudFieldSettings.MeshCount : cloud.MeshIndex, matrix, scale);
        }

        private CloudCell CellOf(CloudClass cloudClass, long cellX, long cellZ) {
            if (_cells.TryGetValue((cloudClass, cellX, cellZ), out CloudCell cell)) return cell;

            cell = _cellPool.Count > 0 ? _cellPool.Pop() : new CloudCell();
            _scatter.AppendCell(cloudClass, cellX, cellZ, cell.Clouds);
            _cells.Add((cloudClass, cellX, cellZ), cell);
            return cell;
        }

        private void RecycleCell(CloudCell cell) {
            cell.Clear();
            _cellPool.Push(cell);
        }

        // Cameras that stopped drawing (destroyed, disabled) let go of their cells on a timer too, not only when one moves.
        private void SweepStaleBatches() {
            if (Time.frameCount - _lastSweepFrame <= StaleBatchFrames) return;

            _lastSweepFrame = Time.frameCount;
            ReleaseCells();
        }

        // Drops stale camera batches, then every cell no live batch's window holds.
        private void ReleaseCells() {
            DropStaleBatches();
            _releasable.Clear();
            foreach ((CloudClass Class, long X, long Z) cell in _cells.Keys) {
                if (!IsHeld(cell)) _releasable.Add(cell);
            }
            foreach ((CloudClass Class, long X, long Z) key in _releasable) {
                RecycleCell(_cells[key]);
                _cells.Remove(key);
            }
        }

        private bool IsHeld((CloudClass Class, long X, long Z) cell) {
            foreach (CloudBatch batch in _batches.Values) {
                if (batch.HoldsCell(cell.Class, cell.X, cell.Z)) return true;
            }

            return false;
        }

        private void DropStaleBatches() {
            _staleCameras.Clear();
            foreach (KeyValuePair<int, CloudBatch> entry in _batches) {
                if (Time.frameCount - entry.Value.LastUsedFrame > StaleBatchFrames) _staleCameras.Add(entry.Key);
            }
            foreach (int id in _staleCameras) _batches.Remove(id);
        }

        // Each mesh's shadow discs at scale 1, and how far its farthest disc centre and widest disc reach (for culling).
        private void BuildShadowDiscs(SkyMeshVariants meshes, CloudFieldSettings settings) {
            for (int meshIndex = 0; meshIndex < CloudFieldSettings.MeshCount; meshIndex++) {
                IReadOnlyList<BlobPuff> puffs = meshes.CloudPuffs(meshIndex);
                bool isOne = settings.ClassOf(CloudFieldSettings.ClassOfMesh(meshIndex)).OneShadowDisc;
                if (isOne) BuildOneDisc(meshIndex, puffs);
                else BuildPuffDiscs(meshIndex, puffs);

                _shadowSpan[meshIndex] = 0f;
                _shadowRadius[meshIndex] = 0f;
                for (int index = 0; index < _shadowCentres[meshIndex].Length; index++) {
                    _shadowSpan[meshIndex] = Mathf.Max(_shadowSpan[meshIndex], _shadowCentres[meshIndex][index].magnitude);
                    _shadowRadius[meshIndex] = Mathf.Max(_shadowRadius[meshIndex], _shadowRadii[meshIndex][index]);
                }
            }
        }

        private void BuildPuffDiscs(int meshIndex, IReadOnlyList<BlobPuff> puffs) {
            int count = puffs != null ? puffs.Count : 0;
            _shadowCentres[meshIndex] = new Vector3[count];
            _shadowRadii[meshIndex] = new float[count];
            for (int index = 0; index < count; index++) {
                _shadowCentres[meshIndex][index] = ToUnity(puffs[index].Centre);
                _shadowRadii[meshIndex][index] = puffs[index].Radii.X;
            }
        }

        // One disc about the puffs' mean centre, wide enough to enclose every puff's footprint.
        private void BuildOneDisc(int meshIndex, IReadOnlyList<BlobPuff> puffs) {
            if (puffs == null || puffs.Count == 0) {
                BuildPuffDiscs(meshIndex, puffs);
                return;
            }

            Vector3 mean = Vector3.zero;
            foreach (BlobPuff puff in puffs) mean += ToUnity(puff.Centre) / puffs.Count;
            float radius = 0f;
            foreach (BlobPuff puff in puffs) radius = Mathf.Max(radius, HorizontalDistance(ToUnity(puff.Centre), mean) + puff.Radii.X);
            _shadowCentres[meshIndex] = new[] { mean };
            _shadowRadii[meshIndex] = new[] { radius };
        }

        private static float HorizontalDistance(Vector3 point, Vector3 other) {
            return new Vector2(point.x - other.x, point.z - other.z).magnitude;
        }

        private static Vector3 ToUnity(System.Numerics.Vector3 vector) {
            return new Vector3(vector.X, vector.Y, vector.Z);
        }

        // The largest horizontal half-extent or height of any cloud at the largest scale, for bounds.
        private static double LargestExtent(SkyMeshVariants meshes, CloudFieldSettings settings) {
            double extent = 0.0;
            for (int meshIndex = 0; meshIndex < CloudFieldSettings.MeshCount; meshIndex++) {
                Bounds bounds = meshes.Cloud(meshIndex).bounds;
                extent = Math.Max(extent, Math.Max(bounds.extents.x, Math.Max(bounds.extents.z, bounds.size.y)));
            }

            return extent * settings.MaxScale;
        }

        private RenderParams CreateParams(Camera camera, Bounds bounds) {
            return new RenderParams(_material) {
                camera = camera,
                layer = _layer,
                worldBounds = bounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
            };
        }

        private void WarnFarClip(Camera camera) {
            double required = _scatter.Settings.RequiredFarClip;
            if (camera.farClipPlane >= required || !_warnedFarClip.Add(camera.GetInstanceID())) return;

            Debug.LogWarning($"CloudField::Submit->camera '{camera.name}' clips at {camera.farClipPlane:0} m; the cloud field needs {required:0} m.", camera);
        }

        // The asset itself when its fog scale already matches, else a clone differing only in it (same variant).
        private void EnsureMaterial() {
            if (_template == null || _material != null) return;

            if (Mathf.Approximately(_template.GetFloat(StylizedLitShader.FogScale), _fogScale)) {
                _material = _template;
                return;
            }

            _material = new Material(_template) { name = $"{_template.name} (fog {_fogScale:0.00})", hideFlags = HideFlags.DontSave };
            _material.SetFloat(StylizedLitShader.FogScale, _fogScale);
        }

        private void DestroyClone() {
            Material material = _material;
            _material = null;
            if (material == null || material == _template) return;

            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }
    }
}
