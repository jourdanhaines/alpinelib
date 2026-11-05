using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Sky;
using AlpineLib.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Sky {
    /// <summary>
    /// Draws the backdrop ring: seeded silhouettes fixed at absolute world positions, drawn while clear of the streamed
    /// window about the camera (<see cref="BackdropLayout.IsDrawn"/>) and out to <see cref="BackdropSettings.OuterRadius"/>,
    /// never grown, sunk or faded: radial fog toward the sky behind them is all that reveals them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A silhouette at absolute A is drawn at A − (camera absolute − camera world), so two clients see the same skyline
    /// in the same place and a floating-origin shift (which moves the camera's world position but not its absolute
    /// one) leaves it where it was on screen. A feature only joins or leaves the drawn set when the camera crosses a
    /// keep-out cell (it is then at least <see cref="BackdropSettings.ClearDistance"/> away) or at the outer radius,
    /// where the fog must hide it whole: the template fogs radially toward the sky (<c>_FOG_RADIAL</c>, <c>_FOG_SKY</c>).
    /// </para>
    /// <para>
    /// Instanced through <c>Graphics.RenderMeshInstanced</c> per camera from <c>beginCameraRendering</c>, on one
    /// layer, with shadows off both ways and no colliders; <see cref="SkyCameraFilter"/> keeps it out of capture
    /// cameras. Feature matrices are rebuilt only when the layout, the camera's keep-out cell or its world-to-absolute
    /// offset changes, or after <see cref="FeatureRebuildDistance"/> of motion (for the outer edge); the ground under
    /// the ring follows the camera every <see cref="RebuildDistance"/>. That ground is one flat annulus fogged like the
    /// world about the camera, or with <see cref="SetGroundSource"/> a world-anchored lattice coloured like the
    /// consumer's ground; rock's foot fog scale rides in vertex alpha (<see cref="BackdropSilhouette.FootFogWeight"/>).
    /// </para>
    /// <para>
    /// Rock draws through the template material itself when its fog scales already match (the template asset
    /// must carry the keywords of <see cref="HasSkyKeywords"/>: keywords switched on at runtime are stripped from
    /// player builds, so the template's <c>_FogScale</c> is rock's and its <c>_FogScaleAlpha</c> the foot's,
    /// <see cref="GroundFogScale"/>); cloud banks and the annulus draw through clones differing only in those two
    /// floats, or the template itself when they match. In edit mode clones are freed on disable and rebuilt on the next draw.
    /// </para>
    /// </remarks>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BackdropRing : MonoBehaviour {
        /// <summary>Camera motion, metres, before the ground under the ring is placed again.</summary>
        public const float RebuildDistance = 0.01f;

        /// <summary>Camera motion, metres, before the drawn features are re-picked for the outer radius (fully fogged there).</summary>
        public const float FeatureRebuildDistance = 8f;

        /// <summary>Change of the camera's world-to-absolute offset, metres, that re-places every feature (an origin shift).</summary>
        public const double OffsetTolerance = 0.001;

        /// <summary>Fog distance scale of the ground annulus and of rock's foot: fogged exactly like the world they continue.</summary>
        public const float GroundFogScale = 1f;

        private const int StaleBatchFrames = 120;
        private const int SectorCacheLimit = 2048;

        private readonly Dictionary<int, BackdropBatch> _batches = new Dictionary<int, BackdropBatch>();
        private readonly Dictionary<long, BackdropFeature[]> _sectors = new Dictionary<long, BackdropFeature[]>();
        private readonly List<BackdropFeature> _scratch = new List<BackdropFeature>();
        private readonly List<int> _staleCameras = new List<int>();
        private readonly HashSet<int> _warnedFarClip = new HashSet<int>();
        private readonly Dictionary<int, BackdropGroundLattice> _lattices = new Dictionary<int, BackdropGroundLattice>();
        private Func<double, double, Color32> _groundColourAt;
        private Material _groundSourceMaterial;
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

        /// <summary>The material the ground lattice draws through (<see cref="SetGroundSource"/>), or null for the flat annulus.</summary>
        public Material GroundSourceMaterial => _groundSourceMaterial;

        /// <summary>The layer every instance draws on.</summary>
        public int Layer => _layer;

        /// <summary>False draws only the ground under the ring (review renders isolate the silhouettes' own pixels).</summary>
        public bool DrawsFeatures { get; set; } = true;

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
            DisposeLattices();
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

        /// <summary>
        /// Replaces the flat annulus with a world-anchored lattice drawn through <paramref name="material"/> (not owned;
        /// vertex-coloured like the world's ground, fogged like it) whose vertex colours (sRGB) come from
        /// <paramref name="colourAt"/>(absolute x, absolute z), so the streamed world's edge does not show through
        /// translucent fog. Null for either restores the annulus. Survives <see cref="Configure"/>.
        /// </summary>
        public void SetGroundSource(Material material, Func<double, double, Color32> colourAt) {
            bool isSet = material != null && colourAt != null;
            _groundSourceMaterial = isSet ? material : null;
            _groundColourAt = isSet ? colourAt : null;
            DisposeLattices();
            _revision++;
        }

        /// <summary>The ground lattice <paramref name="camera"/> draws, or null (no ground source, or not drawn yet).</summary>
        public BackdropGroundLattice GroundLatticeOf(Camera camera) {
            return camera != null && _lattices.TryGetValue(camera.GetInstanceID(), out BackdropGroundLattice lattice) ? lattice : null;
        }

        /// <summary>True when <paramref name="material"/> is instanced and carries every keyword a sky draw needs.</summary>
        public static bool HasSkyKeywords(Material material) {
            if (material == null || !material.enableInstancing) return false;

            return material.IsKeywordEnabled(StylizedLitShader.FlatShadingKeyword)
                && material.IsKeywordEnabled(StylizedLitShader.NoLightCookieKeyword)
                && material.IsKeywordEnabled(StylizedLitShader.ReceiveShadowsOffKeyword)
                && material.IsKeywordEnabled(StylizedLitShader.FogScaleBlendKeyword)
                && material.IsKeywordEnabled(StylizedLitShader.FogRadialKeyword)
                && material.IsKeywordEnabled(StylizedLitShader.FogSkyKeyword);
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
            for (int meshIndex = 0; meshIndex < BackdropSettings.MeshCount && DrawsFeatures; meshIndex++) {
                List<Matrix4x4> matrices = batch.MatricesOf(meshIndex);
                if (matrices.Count == 0) continue;

                Material material = BackdropSettings.KindOf(meshIndex) == BackdropFeatureKind.CloudBank ? _cloudMaterial : _material;
                Graphics.RenderMeshInstanced(CreateParams(material, camera, batch.Bounds), _meshes.Backdrop(meshIndex), 0, matrices);
                draws++;
            }

            BackdropGroundLattice lattice = GroundLatticeOf(camera);
            if (lattice != null && _groundSourceMaterial != null) Graphics.RenderMesh(CreateParams(_groundSourceMaterial, camera, batch.GroundBounds), lattice.Mesh, 0, batch.GroundMatrix);
            else Graphics.RenderMesh(CreateParams(_groundMaterial, camera, batch.GroundBounds), _meshes.Ground, 0, batch.GroundMatrix);
            return draws + 1;
        }

        /// <summary>The instances <paramref name="camera"/> would draw now (re-picked if needed), or null when it is filtered out.</summary>
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

        private bool IsReady => _layout != null && _meshes != null && !_meshes.IsDisposed && _meshes.HasBackdrop && _template != null;

        private void OnEnable() {
            if (_isSubscribed) return;

            RenderPipelineManager.beginCameraRendering += HandleBeginCamera;
            _isSubscribed = true;
        }

        // Edit mode frees its clones here (domain reloads and scene saves skip OnDestroy); the next draw rebuilds them.
        private void OnDisable() {
            if (!Application.isPlaying) {
                DestroyClones();
                DisposeLattices();
            }
            if (!_isSubscribed) return;

            RenderPipelineManager.beginCameraRendering -= HandleBeginCamera;
            _isSubscribed = false;
        }

        private void OnDestroy() {
            DestroyMaterials();
            DisposeLattices();
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
            if (NeedsFeatures(batch, world, absoluteX, absoluteZ)) RebuildFeatures(batch, world, absoluteX, absoluteZ);
            if (!NeedsGround(batch, world, absoluteX, absoluteZ)) return batch;

            PlaceGround(batch, world, absoluteX, absoluteZ);
            PlaceGroundLattice(id, batch, world, absoluteX, absoluteZ);
            DropStaleBatches();
            return batch;
        }

        private void ResolveAbsolute(Vector3 world, out double absoluteX, out double absoluteZ) {
            if (_frame != null && _frame.TryToAbsolute(world, out absoluteX, out absoluteZ)) return;

            absoluteX = world.x;
            absoluteZ = world.z;
        }

        private bool NeedsFeatures(BackdropBatch batch, Vector3 world, double absoluteX, double absoluteZ) {
            if (!batch.IsBuilt || batch.Revision != _revision) return true;

            _layout.KeepOutCellOf(absoluteX, absoluteZ, out long cellX, out long cellZ);
            if (cellX != batch.CellX || cellZ != batch.CellZ) return true;

            double offsetX = absoluteX - world.x;
            double offsetZ = absoluteZ - world.z;
            if (Math.Abs(offsetX - batch.OffsetX) > OffsetTolerance || Math.Abs(offsetZ - batch.OffsetZ) > OffsetTolerance) return true;

            double movedX = absoluteX - batch.FeaturesX;
            double movedZ = absoluteZ - batch.FeaturesZ;
            return movedX * movedX + movedZ * movedZ > FeatureRebuildDistance * FeatureRebuildDistance;
        }

        private bool NeedsGround(BackdropBatch batch, Vector3 world, double absoluteX, double absoluteZ) {
            if (!batch.IsGroundPlaced || batch.GroundRevision != _revision) return true;
            if ((batch.CameraWorld - world).sqrMagnitude > RebuildDistance * RebuildDistance) return true;

            double deltaX = batch.AbsoluteX - absoluteX;
            double deltaZ = batch.AbsoluteZ - absoluteZ;
            return deltaX * deltaX + deltaZ * deltaZ > RebuildDistance * RebuildDistance;
        }

        // Every feature the camera draws, at its absolute position less the world-to-absolute offset.
        private void RebuildFeatures(BackdropBatch batch, Vector3 world, double absoluteX, double absoluteZ) {
            BackdropSettings settings = _layout.Settings;
            double reach = settings.OuterRadius;
            float groundY = _frame != null ? _frame.GroundY : 0f;
            double offsetX = absoluteX - world.x;
            double offsetZ = absoluteZ - world.z;
            _layout.KeepOutCellOf(absoluteX, absoluteZ, out long cellX, out long cellZ);

            batch.Clear();
            _layout.SectorOf(absoluteX - reach, absoluteZ - reach, out int minX, out int minZ);
            _layout.SectorOf(absoluteX + reach, absoluteZ + reach, out int maxX, out int maxZ);
            if (_sectors.Count > SectorCacheLimit) _sectors.Clear();
            for (int sectorZ = minZ; sectorZ <= maxZ; sectorZ++) {
                for (int sectorX = minX; sectorX <= maxX; sectorX++) {
                    AddSector(batch, SectorOf(sectorX, sectorZ), settings, absoluteX, absoluteZ, cellX, cellZ, offsetX, offsetZ, groundY);
                }
            }

            float height = (float)Math.Max(settings.CloudBankMaxAltitude + settings.CloudBankMaxHeight, settings.MaxHeight);
            float extent = (float)(settings.OuterRadius + settings.MaxReach + FeatureRebuildDistance) * 2f;
            batch.Bounds = new Bounds(new Vector3(world.x, groundY + height * 0.5f, world.z), new Vector3(extent, height * 2f, extent));
            batch.CellX = cellX;
            batch.CellZ = cellZ;
            batch.OffsetX = offsetX;
            batch.OffsetZ = offsetZ;
            batch.FeaturesX = absoluteX;
            batch.FeaturesZ = absoluteZ;
            batch.Revision = _revision;
            batch.IsBuilt = true;
        }

        private void PlaceGround(BackdropBatch batch, Vector3 world, double absoluteX, double absoluteZ) {
            BackdropSettings settings = _layout.Settings;
            float groundAt = (_frame != null ? _frame.GroundY : 0f) - (float)settings.GroundDrop;
            float groundExtent = (float)settings.GroundOuterRadius * 2f;
            batch.GroundMatrix = Matrix4x4.Translate(new Vector3(world.x, groundAt, world.z));
            batch.GroundBounds = new Bounds(new Vector3(world.x, groundAt, world.z), new Vector3(groundExtent, 1f, groundExtent));
            batch.CameraWorld = world;
            batch.AbsoluteX = absoluteX;
            batch.AbsoluteZ = absoluteZ;
            batch.GroundRevision = _revision;
            batch.IsGroundPlaced = true;
        }

        private void AddSector(BackdropBatch batch, BackdropFeature[] features, BackdropSettings settings, double absoluteX, double absoluteZ,
            long cellX, long cellZ, double offsetX, double offsetZ, float groundY) {
            foreach (BackdropFeature feature in features) {
                if (!_layout.IsDrawn(feature, absoluteX, absoluteZ, cellX, cellZ)) continue;

                float x = (float)(feature.AbsoluteX(settings.SectorSize) - offsetX);
                float z = (float)(feature.AbsoluteZ(settings.SectorSize) - offsetZ);
                batch.Add(feature.MeshIndex, InstanceMatrix(feature, settings, x, z, groundY));
            }
        }

        // Fixed by the feature alone: rock stands sunk by its base share, a cloud bank floats at its altitude, both at their own yaw.
        private static Matrix4x4 InstanceMatrix(in BackdropFeature feature, BackdropSettings settings, float x, float z, float groundY) {
            var width = (float)feature.Width;
            var height = (float)feature.Height;
            Quaternion yaw = Quaternion.Euler(0f, (float)(feature.Yaw * (180.0 / Math.PI)), 0f);
            if (feature.Kind == BackdropFeatureKind.CloudBank) {
                var position = new Vector3(x, groundY + (float)feature.Altitude, z);
                return Matrix4x4.TRS(position, yaw, new Vector3(width, height, (float)feature.Depth));
            }

            float sink = (float)settings.BaseSink * height;
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
            foreach (int id in _staleCameras) {
                _batches.Remove(id);
                if (!_lattices.TryGetValue(id, out BackdropGroundLattice lattice)) continue;

                lattice.Dispose();
                _lattices.Remove(id);
            }
        }

        // With a ground source, the camera's lattice is recoloured about the snapped anchor and drawn there instead of the annulus.
        private void PlaceGroundLattice(int id, BackdropBatch batch, Vector3 world, double absoluteX, double absoluteZ) {
            if (_groundColourAt == null) return;

            BackdropSettings settings = _layout.Settings;
            if (!_lattices.TryGetValue(id, out BackdropGroundLattice lattice) || lattice.Mesh == null) {
                lattice = new BackdropGroundLattice(settings.GroundLatticeSpacing, settings.GroundLatticeSnap, settings.GroundLatticeHoleRadius, settings.GroundOuterRadius, _groundColourAt);
                _lattices[id] = lattice;
            }

            lattice.AnchorFor(absoluteX, absoluteZ, out double anchorX, out double anchorZ);
            lattice.Recolour(anchorX, anchorZ);
            float groundAt = (_frame != null ? _frame.GroundY : 0f) - (float)settings.GroundDrop;
            var centre = new Vector3(world.x + (float)(anchorX - absoluteX), groundAt, world.z + (float)(anchorZ - absoluteZ));
            float extent = (float)settings.GroundOuterRadius * 2f;
            batch.GroundMatrix = Matrix4x4.Translate(centre);
            batch.GroundBounds = new Bounds(centre, new Vector3(extent, 1f, extent));
        }

        private void DisposeLattices() {
            foreach (BackdropGroundLattice lattice in _lattices.Values) lattice.Dispose();
            _lattices.Clear();
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
