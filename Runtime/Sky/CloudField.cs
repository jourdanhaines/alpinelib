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
    /// built on demand into pooled lists and released once no camera's window holds the cell.
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

        private readonly Dictionary<int, CloudBatch> _batches = new Dictionary<int, CloudBatch>();
        private readonly Dictionary<(CloudClass Class, long X, long Z), List<CloudInstance>> _cells = new Dictionary<(CloudClass Class, long X, long Z), List<CloudInstance>>();
        private readonly Stack<List<CloudInstance>> _cellPool = new Stack<List<CloudInstance>>();
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
        private WindVector _heading;
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
            ResetWind();
            Invalidate();
        }

        /// <summary>
        /// True when <paramref name="material"/> is instanced and carries <c>_FLAT_SHADING</c>, <c>_NO_LIGHT_COOKIE</c>,
        /// <c>_RECEIVE_SHADOWS_OFF</c> and <c>_FOG_RADIAL</c> (so a cloud is as fogged at the screen's edge as dead ahead).
        /// </summary>
        public static bool HasCloudKeywords(Material material) {
            if (material == null || !material.enableInstancing) return false;

            return material.IsKeywordEnabled(StylizedLitShader.FlatShadingKeyword)
                && material.IsKeywordEnabled(StylizedLitShader.NoLightCookieKeyword)
                && material.IsKeywordEnabled(StylizedLitShader.ReceiveShadowsOffKeyword)
                && material.IsKeywordEnabled(StylizedLitShader.FogRadialKeyword);
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
            for (int meshIndex = 0; meshIndex < CloudFieldSettings.MeshCount; meshIndex++) {
                List<Matrix4x4> matrices = batch.MatricesOf(meshIndex);
                if (matrices.Count == 0) continue;

                Graphics.RenderMeshInstanced(CreateParams(camera, batch.Bounds), _meshes.Cloud(meshIndex), 0, matrices);
                draws++;
            }

            return draws;
        }

        /// <summary>The instances <paramref name="camera"/> would draw now (rebuilt when anything moved), or null when it is filtered out or nothing is ready.</summary>
        public CloudBatch Peek(Camera camera) {
            if (!IsReady || !SkyCameraFilter.ShouldDraw(camera, _layer)) return null;

            UpdateWind();
            if (!_hasDisplacement) return null;

            CloudBatch batch = BatchFor(camera);
            SweepStaleBatches();
            return batch;
        }

        /// <summary>
        /// Fills <paramref name="footprints"/> (cleared first, no allocation once it has grown) with one footprint per
        /// puff of every cloud <paramref name="camera"/> draws now: world centre and horizontal radius, scaled with the
        /// cloud. Empty when the camera is filtered out.
        /// </summary>
        public void CopyFootprints(Camera camera, List<CloudFootprint> footprints) {
            if (footprints == null) throw new ArgumentNullException(nameof(footprints));

            footprints.Clear();
            CloudBatch batch = Peek(camera);
            if (batch == null) return;

            for (int meshIndex = 0; meshIndex < CloudFieldSettings.MeshCount; meshIndex++) {
                AppendFootprints(batch.MatricesOf(meshIndex), _meshes.CloudPuffs(meshIndex), footprints);
            }
        }

        private static void AppendFootprints(List<Matrix4x4> matrices, IReadOnlyList<BlobPuff> puffs, List<CloudFootprint> footprints) {
            foreach (Matrix4x4 matrix in matrices) {
                float scale = new Vector3(matrix.m00, matrix.m10, matrix.m20).magnitude;
                for (int index = 0; index < puffs.Count; index++) {
                    System.Numerics.Vector3 centre = puffs[index].Centre;
                    Vector3 world = matrix.MultiplyPoint3x4(new Vector3(centre.X, centre.Y, centre.Z));
                    footprints.Add(new CloudFootprint(world, puffs[index].Radii.X * scale));
                }
            }
        }

        /// <summary>Drops every cached cell and camera batch, so the next draw rebuilds.</summary>
        public void Invalidate() {
            foreach (List<CloudInstance> clouds in _cells.Values) RecycleCell(clouds);
            _cells.Clear();
            _batches.Clear();
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
            _heading = WindVector.Zero;
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
                _heading = WindVector.Zero;
                return;
            }

            _time = _wind.Time;
            _heading = _wind.Field != null ? _wind.Field.Direction(_time) : WindVector.Zero;
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
            for (int classIndex = 0; classIndex < CloudFieldSettings.ClassCount; classIndex++) {
                var cloudClass = (CloudClass)classIndex;
                CloudFieldFrame frame = CloudFieldFrame.Locate(absoluteX, absoluteZ, _scatter.DriftOf(cloudClass, _displacement), _scatter.Settings.CellSize);
                hasMoved |= batch.CellXOf(cloudClass) != frame.CellX || batch.CellZOf(cloudClass) != frame.CellZ;
                batch.SetCell(cloudClass, frame.CellX, frame.CellZ);
                AddWindow(batch, cloudClass, frame, world, groundY);
            }

            batch.CloseBounds(new Vector3(world.x, groundY, world.z), _reach);
            batch.CameraWorld = world;
            batch.AbsoluteX = absoluteX;
            batch.AbsoluteZ = absoluteZ;
            batch.Displacement = _displacement;
            batch.Time = _time;
            batch.Fade = _fade;
            batch.Revision = _revision;
            batch.IsBuilt = true;
            if (hasMoved) ReleaseCells();
        }

        private void AddWindow(CloudBatch batch, CloudClass cloudClass, CloudFieldFrame frame, Vector3 world, float groundY) {
            int window = _scatter.Settings.WindowCells;
            for (long cellZ = frame.CellZ - window; cellZ <= frame.CellZ + window; cellZ++) {
                for (long cellX = frame.CellX - window; cellX <= frame.CellX + window; cellX++) {
                    AddCell(batch, cloudClass, frame, cellX, cellZ, world, groundY);
                }
            }
        }

        private void AddCell(CloudBatch batch, CloudClass cloudClass, CloudFieldFrame frame, long cellX, long cellZ, Vector3 world, float groundY) {
            List<CloudInstance> clouds = CellOf(cloudClass, cellX, cellZ);
            double reach = _scatter.Settings.WindowRadius + _scatter.Settings.ClassOf(cloudClass).MotionReach;
            for (int index = 0; index < clouds.Count; index++) {
                CloudInstance cloud = clouds[index];
                double offsetX = frame.OffsetX(cellX, cloud.AnchorX);
                double offsetZ = frame.OffsetZ(cellZ, cloud.AnchorZ);
                if (offsetX * offsetX + offsetZ * offsetZ >= reach * reach) continue;

                AddCloud(batch, cloud, offsetX, offsetZ, world, groundY);
            }
        }

        // Wandered and surged about its anchor, shrunk about its flat base by its presence and the fade.
        private void AddCloud(CloudBatch batch, in CloudInstance cloud, double offsetX, double offsetZ, Vector3 world, float groundY) {
            _scatter.Offset(cloud, _time, _heading.X, _heading.Z, out double moveX, out double moveZ);
            offsetX += moveX;
            offsetZ += moveZ;
            double presence = _scatter.Presence(Math.Sqrt(offsetX * offsetX + offsetZ * offsetZ)) * _fade;
            if (presence <= 0.0) return;

            var position = new Vector3(world.x + (float)offsetX, groundY + (float)cloud.Altitude, world.z + (float)offsetZ);
            var yaw = Quaternion.Euler(0f, (float)(cloud.Yaw * (180.0 / Math.PI)), 0f);
            float scale = (float)(cloud.Scale * presence);
            batch.Add(cloud.MeshIndex, Matrix4x4.TRS(position, yaw, new Vector3(scale, scale, scale)));
        }

        private List<CloudInstance> CellOf(CloudClass cloudClass, long cellX, long cellZ) {
            if (_cells.TryGetValue((cloudClass, cellX, cellZ), out List<CloudInstance> clouds)) return clouds;

            clouds = _cellPool.Count > 0 ? _cellPool.Pop() : new List<CloudInstance>(_scatter.Settings.ClassOf(cloudClass).CandidatesPerCell);
            _scatter.AppendCell(cloudClass, cellX, cellZ, clouds);
            _cells.Add((cloudClass, cellX, cellZ), clouds);
            return clouds;
        }

        private void RecycleCell(List<CloudInstance> clouds) {
            clouds.Clear();
            _cellPool.Push(clouds);
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
            foreach ((CloudClass Class, long X, long Z) cell in _releasable) {
                RecycleCell(_cells[cell]);
                _cells.Remove(cell);
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
