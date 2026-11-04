using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Sky;
using AlpineLib.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Sky {
    /// <summary>
    /// Draws the backdrop ring: seeded silhouettes laid out in a parallax frame that follows each camera at
    /// <see cref="BackdropSettings.ParallaxFactor"/> of its absolute motion, shown <see cref="BackdropSettings.InnerRadius"/>
    /// to <see cref="BackdropSettings.OuterRadius"/> from it, sinking in and rising out across the fade bands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A silhouette at backdrop-frame F is drawn at the camera plus (F − parallax × camera absolute xz), so two
    /// clients at the same absolute position see the same skyline, and a floating-origin shift (which moves the
    /// camera's world position but not its absolute one) leaves the skyline where it was on screen.
    /// </para>
    /// <para>
    /// Instanced through <c>Graphics.RenderMeshInstanced</c> per camera from <c>beginCameraRendering</c>, on one
    /// layer, with shadows off both ways and no colliders; <see cref="SkyCameraFilter"/> keeps it out of capture
    /// cameras. Matrices are rebuilt whenever the camera moved more than <see cref="RebuildDistance"/> (about every
    /// moving frame: cached world-space matrices would otherwise lag and jump). One flat annulus, fogged like the
    /// world, is drawn under the ring about the camera to hide the edge of the streamed world; rock's foot is
    /// fogged like the world too (<see cref="BackdropSilhouette.FootFogWeight"/> in vertex alpha), so a silhouette
    /// rises out of that ground haze instead of standing vivid on a pale band.
    /// </para>
    /// <para>
    /// Rock draws through the template material itself when its fog scales already match (the template asset
    /// must carry <c>_FLAT_SHADING</c>, <c>_NO_LIGHT_COOKIE</c>, <c>_RECEIVE_SHADOWS_OFF</c> and <c>_FOG_SCALE_BLEND</c>,
    /// see <see cref="HasSkyKeywords"/>: keywords switched on at runtime are stripped from player builds, so the
    /// template's <c>_FogScale</c> is rock's and its <c>_FogScaleAlpha</c> the foot's, <see cref="GroundFogScale"/>);
    /// cloud banks and the annulus draw through clones differing only in those two floats (set equal, so their
    /// vertex alpha is moot). In edit mode clones are freed on disable and rebuilt on the next draw.
    /// </para>
    /// </remarks>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BackdropRing : MonoBehaviour {
        /// <summary>Camera motion, metres, before its instances are rebuilt.</summary>
        public const float RebuildDistance = 0.01f;

        /// <summary>Fog distance scale of the ground annulus and of rock's foot: fogged exactly like the world they continue.</summary>
        public const float GroundFogScale = 1f;

        /// <summary>Most a cloud bank's yaw strays from broadside to the camera, degrees.</summary>
        public const float CloudYawJitterDegrees = 20f;

        private const int StaleBatchFrames = 120;
        private const int SectorCacheLimit = 2048;

        private readonly Dictionary<int, BackdropBatch> _batches = new Dictionary<int, BackdropBatch>();
        private readonly Dictionary<long, BackdropFeature[]> _sectors = new Dictionary<long, BackdropFeature[]>();
        private readonly List<BackdropFeature> _scratch = new List<BackdropFeature>();
        private readonly List<int> _staleCameras = new List<int>();
        private readonly HashSet<int> _warnedFarClip = new HashSet<int>();
        private BackdropLayout _layout;
        private SkyMeshVariants _meshes;
        private IAbsoluteFrame _frame;
        private Material _material;
        private Material _cloudMaterial;
        private Material _groundMaterial;
        private Material _template;
        private float _fogScale;
        private float _cloudFogScale;
        private int _layer;
        private int _revision;
        private bool _isSubscribed;

        /// <summary>The layout the ring draws, or null before <see cref="Configure"/>.</summary>
        public BackdropLayout Layout => _layout;

        /// <summary>
        /// The material rock draws through: the template, or a clone of it when the fog scale differs. Often the
        /// shared template asset itself: read it, never mutate it (use <see cref="SetFogScale"/>).
        /// </summary>
        public Material Material => _material;

        /// <summary>The cloud banks' material (a clone, or the template when the fog scales match: do not mutate).</summary>
        public Material CloudMaterial => _cloudMaterial;

        /// <summary>The ground annulus's clone of the template, fogged at <see cref="GroundFogScale"/>.</summary>
        public Material GroundMaterial => _groundMaterial;

        /// <summary>The layer every instance draws on.</summary>
        public int Layer => _layer;

        /// <summary>
        /// Draws <paramref name="layout"/> with <paramref name="meshes"/> (not owned) through <paramref name="materialTemplate"/>,
        /// rock fogged at <paramref name="fogScale"/> and cloud banks at <paramref name="cloudFogScale"/> of the scene's
        /// distance, on <paramref name="layer"/>, placed through <paramref name="frame"/>.
        /// </summary>
        public void Configure(BackdropLayout layout, SkyMeshVariants meshes, Material materialTemplate, float fogScale, float cloudFogScale, int layer, IAbsoluteFrame frame) {
            if (materialTemplate == null) throw new ArgumentNullException(nameof(materialTemplate));

            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
            _frame = frame;
            _layer = layer;
            DestroyMaterials();
            _template = materialTemplate;
            _fogScale = fogScale;
            _cloudFogScale = cloudFogScale;
            if (!HasSkyKeywords(materialTemplate)) Debug.LogWarning($"BackdropRing::Configure->'{materialTemplate.name}' lacks the sky keywords; player builds strip variants switched on at runtime.", this);
            EnsureMaterials();
            _sectors.Clear();
            _revision++;
        }

        /// <summary>Sets the fog distance scale rock draws with above its foot (1 = fogged like the world), cloning the template first if needed.</summary>
        public void SetFogScale(float fogScale) {
            _fogScale = fogScale;
            if (_material == null) return;

            if (_material == _template) _material = MaterialFor(fogScale, GroundFogScale);
            else _material.SetFloat(StylizedLitShader.FogScale, fogScale);
        }

        /// <summary>True when <paramref name="material"/> is instanced and carries every keyword a sky draw needs.</summary>
        public static bool HasSkyKeywords(Material material) {
            if (material == null || !material.enableInstancing) return false;

            return material.IsKeywordEnabled(StylizedLitShader.FlatShadingKeyword)
                && material.IsKeywordEnabled(StylizedLitShader.NoLightCookieKeyword)
                && material.IsKeywordEnabled(StylizedLitShader.ReceiveShadowsOffKeyword)
                && material.IsKeywordEnabled(StylizedLitShader.FogScaleBlendKeyword);
        }

        /// <summary>
        /// Queues the ring for <paramref name="camera"/>'s next render and returns the instanced draws issued: 0 when
        /// the camera is filtered out (<see cref="SkyCameraFilter"/>) or nothing is in its band.
        /// </summary>
        public int Submit(Camera camera) {
            if (!IsReady || !SkyCameraFilter.ShouldDraw(camera, _layer)) return 0;

            EnsureMaterials();
            WarnFarClip(camera);
            BackdropBatch batch = BatchFor(camera);
            int draws = 0;
            for (int meshIndex = 0; meshIndex < BackdropSettings.MeshCount; meshIndex++) {
                List<Matrix4x4> matrices = batch.MatricesOf(meshIndex);
                if (matrices.Count == 0) continue;

                Material material = BackdropSettings.KindOf(meshIndex) == BackdropFeatureKind.CloudBank ? _cloudMaterial : _material;
                Graphics.RenderMeshInstanced(CreateParams(material, camera, batch.Bounds), _meshes.Backdrop(meshIndex), 0, matrices);
                draws++;
            }

            Graphics.RenderMesh(CreateParams(_groundMaterial, camera, batch.GroundBounds), _meshes.Ground, 0, batch.GroundMatrix);
            return draws + 1;
        }

        /// <summary>The instances <paramref name="camera"/> would draw now (rebuilt if it moved), or null when it is filtered out.</summary>
        public BackdropBatch Peek(Camera camera) {
            if (!IsReady || !SkyCameraFilter.ShouldDraw(camera, _layer)) return null;

            return BatchFor(camera);
        }

        /// <summary>Drops every cached sector and camera batch, so the next draw rebuilds.</summary>
        public void Invalidate() {
            _sectors.Clear();
            _batches.Clear();
            _revision++;
        }

        private bool IsReady => _layout != null && _meshes != null && !_meshes.IsDisposed && _template != null;

        private void OnEnable() {
            if (_isSubscribed) return;

            RenderPipelineManager.beginCameraRendering += HandleBeginCamera;
            _isSubscribed = true;
        }

        // Edit mode frees its clones here (domain reloads and scene saves skip OnDestroy); the next draw rebuilds them.
        private void OnDisable() {
            if (!Application.isPlaying) DestroyClones();
            if (!_isSubscribed) return;

            RenderPipelineManager.beginCameraRendering -= HandleBeginCamera;
            _isSubscribed = false;
        }

        private void OnDestroy() {
            DestroyMaterials();
            _batches.Clear();
            _sectors.Clear();
        }

        private void HandleBeginCamera(ScriptableRenderContext context, Camera camera) {
            Submit(camera);
        }

        private BackdropBatch BatchFor(Camera camera) {
            int id = camera.GetInstanceID();
            if (!_batches.TryGetValue(id, out BackdropBatch batch)) {
                batch = new BackdropBatch();
                _batches.Add(id, batch);
            }

            batch.LastUsedFrame = Time.frameCount;
            Vector3 world = camera.transform.position;
            ResolveAbsolute(world, out double absoluteX, out double absoluteZ);
            if (!NeedsRebuild(batch, world, absoluteX, absoluteZ)) return batch;

            Rebuild(batch, world, absoluteX, absoluteZ);
            DropStaleBatches();
            return batch;
        }

        private void ResolveAbsolute(Vector3 world, out double absoluteX, out double absoluteZ) {
            if (_frame != null && _frame.TryToAbsolute(world, out absoluteX, out absoluteZ)) return;

            absoluteX = world.x;
            absoluteZ = world.z;
        }

        private bool NeedsRebuild(BackdropBatch batch, Vector3 world, double absoluteX, double absoluteZ) {
            if (!batch.IsBuilt || batch.Revision != _revision) return true;
            if ((batch.CameraWorld - world).sqrMagnitude > RebuildDistance * RebuildDistance) return true;

            double deltaX = batch.AbsoluteX - absoluteX;
            double deltaZ = batch.AbsoluteZ - absoluteZ;
            return deltaX * deltaX + deltaZ * deltaZ > RebuildDistance * RebuildDistance;
        }

        private void Rebuild(BackdropBatch batch, Vector3 world, double absoluteX, double absoluteZ) {
            BackdropSettings settings = _layout.Settings;
            double centreX = absoluteX * settings.ParallaxFactor;
            double centreZ = absoluteZ * settings.ParallaxFactor;
            double reach = settings.MaxBandRadius;
            float groundY = _frame != null ? _frame.GroundY : 0f;

            batch.Clear();
            _layout.SectorOf(centreX - reach, centreZ - reach, out int minX, out int minZ);
            _layout.SectorOf(centreX + reach, centreZ + reach, out int maxX, out int maxZ);
            if (_sectors.Count > SectorCacheLimit) _sectors.Clear();
            for (int sectorZ = minZ; sectorZ <= maxZ; sectorZ++) {
                for (int sectorX = minX; sectorX <= maxX; sectorX++) {
                    AddSector(batch, SectorOf(sectorX, sectorZ), settings, world, centreX, centreZ, groundY);
                }
            }

            float height = (float)(settings.CloudBankMaxAltitude + settings.CloudBankMaxHeight + settings.MaxHeight);
            float extent = (float)settings.RequiredFarClip * 2f;
            batch.Bounds = new Bounds(new Vector3(world.x, groundY + height * 0.5f, world.z), new Vector3(extent, height * 2f, extent));
            float groundAt = groundY - (float)settings.GroundDrop;
            float groundExtent = (float)settings.GroundOuterRadius * 2f;
            batch.GroundMatrix = Matrix4x4.Translate(new Vector3(world.x, groundAt, world.z));
            batch.GroundBounds = new Bounds(new Vector3(world.x, groundAt, world.z), new Vector3(groundExtent, 1f, groundExtent));
            batch.CameraWorld = world;
            batch.AbsoluteX = absoluteX;
            batch.AbsoluteZ = absoluteZ;
            batch.Revision = _revision;
            batch.IsBuilt = true;
        }

        private void AddSector(BackdropBatch batch, BackdropFeature[] features, BackdropSettings settings, Vector3 world, double centreX, double centreZ, float groundY) {
            foreach (BackdropFeature feature in features) {
                double offsetX = feature.FrameX(settings.SectorSize) - centreX;
                double offsetZ = feature.FrameZ(settings.SectorSize) - centreZ;
                double presence = settings.Presence(feature.Kind, Math.Sqrt(offsetX * offsetX + offsetZ * offsetZ));
                if (presence <= 0.0) continue;

                float x = (float)(world.x + offsetX);
                float z = (float)(world.z + offsetZ);
                batch.Add(feature.MeshIndex, InstanceMatrix(feature, settings, presence, x, z, offsetX, offsetZ, groundY));
            }
        }

        // Rock sinks into the ground as it fades; a cloud bank turns broadside to the camera and shrinks about its base.
        private static Matrix4x4 InstanceMatrix(BackdropFeature feature, BackdropSettings settings, double presence, float x, float z, double offsetX, double offsetZ, float groundY) {
            float width = (float)feature.Width;
            float height = (float)feature.Height;
            if (feature.Kind == BackdropFeatureKind.CloudBank) {
                float scale = (float)presence;
                var position = new Vector3(x, groundY + (float)feature.Altitude, z);
                return Matrix4x4.TRS(position, CloudYaw(feature, offsetX, offsetZ), new Vector3(width, height, (float)feature.Depth) * scale);
            }

            Quaternion yaw = Quaternion.Euler(0f, (float)(feature.Yaw * (180.0 / Math.PI)), 0f);
            float sink = (float)(settings.BaseSink + (1.0 - presence) * (1.0 + settings.BaseSink)) * height;
            return Matrix4x4.TRS(new Vector3(x, groundY - sink, z), yaw, new Vector3(width, height, width));
        }

        // The bank's long (x) axis across the line of sight, give or take the feature's own jitter.
        private static Quaternion CloudYaw(BackdropFeature feature, double offsetX, double offsetZ) {
            double bearing = Math.Atan2(offsetX, offsetZ) * (180.0 / Math.PI);
            double jitter = (feature.Yaw / Math.PI - 1.0) * CloudYawJitterDegrees;
            return Quaternion.Euler(0f, (float)(bearing + jitter), 0f);
        }

        private BackdropFeature[] SectorOf(int sectorX, int sectorZ) {
            long key = ((long)sectorX << 32) | (uint)sectorZ;
            if (_sectors.TryGetValue(key, out BackdropFeature[] features)) return features;

            _scratch.Clear();
            _layout.AppendSector(sectorX, sectorZ, _scratch);
            features = _scratch.ToArray();
            _sectors.Add(key, features);
            return features;
        }

        private void DropStaleBatches() {
            _staleCameras.Clear();
            foreach (KeyValuePair<int, BackdropBatch> entry in _batches) {
                if (Time.frameCount - entry.Value.LastUsedFrame > StaleBatchFrames) _staleCameras.Add(entry.Key);
            }
            foreach (int id in _staleCameras) _batches.Remove(id);
        }

        private RenderParams CreateParams(Material material, Camera camera, Bounds bounds) {
            return new RenderParams(material) {
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
            double required = _layout.Settings.RequiredFarClip;
            if (camera.farClipPlane >= required || !_warnedFarClip.Add(camera.GetInstanceID())) return;

            Debug.LogWarning($"BackdropRing::Submit->camera '{camera.name}' clips at {camera.farClipPlane:0} m; the ring needs {required:0} m.", camera);
        }

        private void EnsureMaterials() {
            if (_template == null) return;

            if (_material == null) _material = MaterialFor(_fogScale, GroundFogScale);
            if (_cloudMaterial == null) _cloudMaterial = MaterialFor(_cloudFogScale, _cloudFogScale);
            if (_groundMaterial == null) _groundMaterial = MaterialFor(GroundFogScale, GroundFogScale);
        }

        // The template itself when both fog scales already match, else a clone differing only in them (same variant).
        private Material MaterialFor(float fogScale, float footFogScale) {
            bool isMatched = Mathf.Approximately(_template.GetFloat(StylizedLitShader.FogScale), fogScale)
                && Mathf.Approximately(_template.GetFloat(StylizedLitShader.FogScaleAlpha), footFogScale);
            if (isMatched) return _template;

            var material = new Material(_template) { name = $"{_template.name} (fog {fogScale:0.00}/{footFogScale:0.00})", hideFlags = HideFlags.DontSave };
            material.SetFloat(StylizedLitShader.FogScale, fogScale);
            material.SetFloat(StylizedLitShader.FogScaleAlpha, footFogScale);
            return material;
        }

        private void DestroyMaterials() {
            DestroyClones();
            _template = null;
        }

        private void DestroyClones() {
            DestroyClone(_material);
            DestroyClone(_cloudMaterial);
            DestroyClone(_groundMaterial);
            _material = null;
            _cloudMaterial = null;
            _groundMaterial = null;
        }

        private void DestroyClone(Material material) {
            if (material == null || material == _template) return;

            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }
    }
}
