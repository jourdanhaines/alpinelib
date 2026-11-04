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
    /// cameras. Matrices are rebuilt only when the camera moved more than <see cref="RebuildDistance"/>.
    /// </para>
    /// </remarks>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BackdropRing : MonoBehaviour {
        /// <summary>Camera motion, metres, before its instances are rebuilt.</summary>
        public const float RebuildDistance = 1f;

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
        private int _layer;
        private int _revision;
        private bool _isSubscribed;

        /// <summary>The layout the ring draws, or null before <see cref="Configure"/>.</summary>
        public BackdropLayout Layout => _layout;

        /// <summary>The ring's own material (a clone of the template); destroyed with the ring.</summary>
        public Material Material => _material;

        /// <summary>The layer every instance draws on.</summary>
        public int Layer => _layer;

        /// <summary>
        /// Draws <paramref name="layout"/> with <paramref name="meshes"/> (not owned) through a clone of
        /// <paramref name="materialTemplate"/> fogged at <paramref name="fogScale"/> of the scene's distance, on
        /// <paramref name="layer"/>, placed through <paramref name="frame"/>.
        /// </summary>
        public void Configure(BackdropLayout layout, SkyMeshVariants meshes, Material materialTemplate, float fogScale, int layer, IAbsoluteFrame frame) {
            if (materialTemplate == null) throw new ArgumentNullException(nameof(materialTemplate));

            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
            _frame = frame;
            _layer = layer;
            DestroyMaterial();
            _material = CreateMaterial(materialTemplate, fogScale);
            _sectors.Clear();
            _revision++;
        }

        /// <summary>Sets the fog distance scale of the ring's material (1 = fogged like the world).</summary>
        public void SetFogScale(float fogScale) {
            if (_material != null) _material.SetFloat(StylizedLitShader.FogScale, fogScale);
        }

        /// <summary>
        /// Queues the ring for <paramref name="camera"/>'s next render and returns the instanced draws issued: 0 when
        /// the camera is filtered out (<see cref="SkyCameraFilter"/>) or nothing is in its band.
        /// </summary>
        public int Submit(Camera camera) {
            if (!IsReady || !SkyCameraFilter.ShouldDraw(camera, _layer)) return 0;

            WarnFarClip(camera);
            BackdropBatch batch = BatchFor(camera);
            int draws = 0;
            for (int meshIndex = 0; meshIndex < BackdropSettings.MeshCount; meshIndex++) {
                List<Matrix4x4> matrices = batch.MatricesOf(meshIndex);
                if (matrices.Count == 0) continue;

                RenderParams parameters = CreateParams(camera, batch.Bounds);
                Graphics.RenderMeshInstanced(parameters, _meshes.Backdrop(meshIndex), 0, matrices);
                draws++;
            }

            return draws;
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

        private bool IsReady => _layout != null && _meshes != null && !_meshes.IsDisposed && _material != null;

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
            DestroyMaterial();
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
            double reach = settings.RequiredFarClip;
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
            batch.Bounds = new Bounds(new Vector3(world.x, groundY + height * 0.5f, world.z), new Vector3((float)reach * 2f, height * 2f, (float)reach * 2f));
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

                batch.Add(feature.MeshIndex, InstanceMatrix(feature, settings, presence, world.x + (float)offsetX, world.z + (float)offsetZ, groundY));
            }
        }

        // Rock sinks into the ground as it fades; a cloud bank shrinks about its base.
        private static Matrix4x4 InstanceMatrix(BackdropFeature feature, BackdropSettings settings, double presence, float x, float z, float groundY) {
            Quaternion yaw = Quaternion.Euler(0f, (float)(feature.Yaw * (180.0 / Math.PI)), 0f);
            float width = (float)feature.Width;
            float height = (float)feature.Height;
            if (feature.Kind == BackdropFeatureKind.CloudBank) {
                float scale = (float)presence;
                var position = new Vector3(x, groundY + (float)feature.Altitude, z);
                return Matrix4x4.TRS(position, yaw, new Vector3(width * scale, height * scale, width * scale));
            }

            float sink = (float)(settings.BaseSink + (1.0 - presence) * (1.0 + settings.BaseSink)) * height;
            return Matrix4x4.TRS(new Vector3(x, groundY - sink, z), yaw, new Vector3(width, height, width));
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
            double required = _layout.Settings.RequiredFarClip;
            if (camera.farClipPlane >= required || !_warnedFarClip.Add(camera.GetInstanceID())) return;

            Debug.LogWarning($"BackdropRing::Submit->camera '{camera.name}' clips at {camera.farClipPlane:0} m; the ring needs {required:0} m.", camera);
        }

        private static Material CreateMaterial(Material template, float fogScale) {
            var material = new Material(template) { name = $"{template.name} (Backdrop)", hideFlags = HideFlags.DontSave, enableInstancing = true };
            material.SetFloat(StylizedLitShader.FogScale, fogScale);
            StylizedLitShader.SetKeyword(material, StylizedLitShader.FlatShadingKeyword, true);
            StylizedLitShader.SetKeyword(material, StylizedLitShader.NoLightCookieKeyword, true);
            StylizedLitShader.SetKeyword(material, StylizedLitShader.ReceiveShadowsOffKeyword, true);
            return material;
        }

        private void DestroyMaterial() {
            if (_material == null) return;

            if (Application.isPlaying) Destroy(_material);
            else DestroyImmediate(_material);
            _material = null;
        }
    }
}
