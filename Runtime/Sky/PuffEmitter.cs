using System;
using System.Collections.Generic;
using AlpineLib.Origin;
using AlpineLib.Procedural.Kits;
using AlpineLib.Procedural.Sky;
using AlpineLib.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Sky {
    /// <summary>
    /// Emits faceted smoke puffs from its transform (a locomotive's stack): a pooled <see cref="PuffSimulation"/> fed
    /// the wind relative to the emitter's measured motion, drawn instanced per camera.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Puffs are kept relative to the emitter in world axes and move by rise + (wind − emitter velocity), so in the world
    /// they ride the wind while the emitter leaves them behind. The velocity is estimated from the transform's motion
    /// (<see cref="PuffMotion"/>): a snap keeps the estimate and the live puffs in place, a teleport drops them, and an
    /// origin shift (<see cref="IOriginShiftListener"/>) moves the last position with the world. The wind is held at
    /// zero until it is ready (a cold join replaying).
    /// </para>
    /// <para>
    /// Cosmetic and not replicated: deterministic per seed and steps (<see cref="PuffSimulation"/>), so other players see
    /// similar smoke, not the same puffs. Owns its <see cref="PuffSettings.VariantCount"/> meshes (destroyed with it);
    /// draws through the material asset itself (keywords baked, see <see cref="HasSmokeKeywords"/>) from
    /// <c>beginCameraRendering</c> with shadows off, on one layer, never for capture cameras (<see cref="SkyCameraFilter"/>).
    /// Something else drives it (<see cref="Step"/> once per frame) and sets the emission (<see cref="SetEmission"/>).
    /// </para>
    /// </remarks>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class PuffEmitter : MonoBehaviour, IOriginShiftListener {
        private readonly List<Matrix4x4>[] _matrices = new List<Matrix4x4>[PuffSettings.VariantCount];
        private readonly Mesh[] _meshes = new Mesh[PuffSettings.VariantCount];
        private readonly PuffMotion _motion = new PuffMotion();
        private PuffSimulation _simulation;
        private Material _material;
        private int _layer = -1;
        private int _version;
        private int _builtVersion = -1;
        private int _builtFrame = -1;
        private Vector3 _builtOrigin;
        private double _rate;
        private int _burst = 1;
        private double _effort;
        private bool _isSubscribed;
        private Bounds _bounds;

        /// <summary>The wind puffs ride (still air when null or not ready).</summary>
        public IWindService Wind { get; set; }

        /// <summary>True once <see cref="Configure"/> ran.</summary>
        public bool IsConfigured => _simulation != null;

        /// <summary>The layer puffs draw on.</summary>
        public int Layer => _layer;

        /// <summary>The material asset puffs draw through.</summary>
        public Material Material => _material;

        /// <summary>The puffs, or null before <see cref="Configure"/>.</summary>
        public PuffSimulation Simulation => _simulation;

        /// <summary>Puffs alive now.</summary>
        public int LiveCount => _simulation != null ? _simulation.Count : 0;

        /// <summary>The emitter's estimated velocity, m/s (snaps left out).</summary>
        public Vector3 MeasuredVelocity => ToUnity(_motion.Velocity);

        /// <summary>How the emitter's motion is followed (teleport distance, speed and acceleration limits).</summary>
        public PuffMotion Motion => _motion;

        /// <summary>Steps taken as teleports, and as snaps, so far.</summary>
        public int Teleports => _motion.Teleports;
        public int Snaps => _motion.Snaps;

        /// <summary>Emission now: bursts per second, puffs per burst, effort 0..1.</summary>
        public double Rate => _rate;
        public int Burst => _burst;
        public double Effort => _effort;

        /// <summary>Puff mesh <paramref name="variant"/>, or null before <see cref="Configure"/>.</summary>
        public Mesh MeshOf(int variant) {
            return variant >= 0 && variant < _meshes.Length ? _meshes[variant] : null;
        }

        /// <summary>
        /// Builds the puff meshes of <paramref name="seed"/> painted from <paramref name="light"/> and
        /// <paramref name="dark"/> and starts an empty pool; puffs draw through <paramref name="material"/> (an asset with
        /// the smoke keywords, not owned) on <paramref name="layer"/>.
        /// </summary>
        public void Configure(PuffSettings settings, ulong seed, SkyAtlasBand light, SkyAtlasBand dark, Material material, int layer) {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (layer < 0 || layer > 31) throw new ArgumentOutOfRangeException(nameof(layer), layer, "Must be a layer index.");
            if (!HasSmokeKeywords(material)) Debug.LogWarning($"PuffEmitter::Configure->'{material.name}' lacks instancing or the smoke keywords; player builds strip variants switched on at runtime.", this);

            var simulation = new PuffSimulation(settings, seed);
            DestroyMeshes();
            for (int variant = 0; variant < _meshes.Length; variant++) {
                _meshes[variant] = FacetMeshUnity.ToMesh(PuffShape.Build(variant, seed, light, dark), $"Smoke Puff {variant}");
                _matrices[variant] ??= new List<Matrix4x4>(settings.Capacity);
            }

            _simulation = simulation;
            _material = material;
            _layer = layer;
            _motion.Reset();
            _version++;
        }

        /// <summary>
        /// True when <paramref name="material"/> is instanced and carries <c>_FLAT_SHADING</c>, <c>_NO_LIGHT_COOKIE</c>
        /// and <c>_RECEIVE_SHADOWS_OFF</c>.
        /// </summary>
        public static bool HasSmokeKeywords(Material material) {
            if (material == null || !material.enableInstancing) return false;

            return material.IsKeywordEnabled(StylizedLitShader.FlatShadingKeyword)
                && material.IsKeywordEnabled(StylizedLitShader.NoLightCookieKeyword)
                && material.IsKeywordEnabled(StylizedLitShader.ReceiveShadowsOffKeyword);
        }

        /// <summary>Emits <paramref name="ratePerSecond"/> bursts per second of <paramref name="burst"/> puffs at <paramref name="effort"/> (0..1) from the next step on.</summary>
        public void SetEmission(double ratePerSecond, int burst, double effort) {
            _rate = Math.Max(0.0, ratePerSecond);
            _burst = Math.Max(0, burst);
            _effort = Math.Min(1.0, Math.Max(0.0, effort));
        }

        /// <summary>Measures the emitter's motion over <paramref name="seconds"/> and advances the puffs by it.</summary>
        public void Step(float seconds) {
            if (_simulation == null || !(seconds > 0f)) return;

            if (!_motion.Measure(ToNumerics(transform.position), seconds, out System.Numerics.Vector3 residual)) _simulation.Clear();
            else if (residual != System.Numerics.Vector3.Zero) _simulation.Translate(-residual);

            Vector3 wind = Wind != null && Wind.IsReady ? Wind.Velocity : Vector3.zero;
            _simulation.Step(seconds, ToNumerics(wind) - _motion.Velocity, _rate, _burst, _effort);
            _version++;
        }

        /// <summary>World position of live puff <paramref name="index"/> (0..<see cref="LiveCount"/> − 1).</summary>
        public Vector3 PuffPosition(int index) {
            if (index < 0 || index >= LiveCount) throw new ArgumentOutOfRangeException(nameof(index), index, "No such live puff.");

            return transform.position + ToUnity(_simulation[index].Offset);
        }

        /// <summary>Forgets every live puff.</summary>
        public void Clear() {
            _simulation?.Clear();
            _version++;
        }

        /// <summary>
        /// Queues the puffs for <paramref name="camera"/>'s next render and returns the instanced draws issued: 0 when the
        /// camera is filtered out (<see cref="SkyCameraFilter"/>) or nothing is alive.
        /// </summary>
        public int Submit(Camera camera) {
            if (_simulation == null || _simulation.Count == 0 || _material == null) return 0;
            if (!SkyCameraFilter.ShouldDraw(camera, _layer)) return 0;

            Rebuild();
            var parameters = new RenderParams(_material) {
                camera = camera,
                layer = _layer,
                worldBounds = _bounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
            };
            int draws = 0;
            for (int variant = 0; variant < _meshes.Length; variant++) {
                if (_matrices[variant].Count == 0 || _meshes[variant] == null) continue;

                Graphics.RenderMeshInstanced(parameters, _meshes[variant], 0, _matrices[variant]);
                draws++;
            }

            return draws;
        }

        /// <summary>Copies the world matrix of every live puff into <paramref name="matrices"/> (cleared first).</summary>
        public void CopyMatrices(List<Matrix4x4> matrices) {
            if (matrices == null) throw new ArgumentNullException(nameof(matrices));

            matrices.Clear();
            if (_simulation == null) return;

            Rebuild();
            foreach (List<Matrix4x4> list in _matrices) matrices.AddRange(list);
        }

        void IOriginShiftListener.OnOriginShifted(Vector3 delta) {
            _motion.Shift(ToNumerics(delta));
        }

        // Puffs are emitter-relative: any left from before a disable would jump to wherever it comes back.
        private void OnEnable() {
            Clear();
            _motion.Reset();
            OriginShiftRegistry.RegisterListener(this);
            if (_isSubscribed) return;

            RenderPipelineManager.beginCameraRendering += HandleBeginCamera;
            _isSubscribed = true;
        }

        private void OnDisable() {
            OriginShiftRegistry.UnregisterListener(this);
            _motion.Reset();
            if (!_isSubscribed) return;

            RenderPipelineManager.beginCameraRendering -= HandleBeginCamera;
            _isSubscribed = false;
        }

        private void OnDestroy() {
            DestroyMeshes();
            _simulation = null;
        }

        private void HandleBeginCamera(ScriptableRenderContext context, Camera camera) {
            Submit(camera);
        }

        // Once per frame and state: every camera that frame draws from the same matrices.
        private void Rebuild() {
            Vector3 origin = transform.position;
            if (_builtVersion == _version && _builtFrame == Time.frameCount && _builtOrigin == origin) return;

            _builtVersion = _version;
            _builtFrame = Time.frameCount;
            _builtOrigin = origin;
            foreach (List<Matrix4x4> list in _matrices) list?.Clear();

            Vector3 min = origin;
            Vector3 max = origin;
            for (int index = 0; index < _simulation.Count; index++) {
                Puff puff = _simulation[index];
                float size = (float)_simulation.SizeAt(_simulation.Progress(puff.Age), puff.Effort);
                if (size <= 0f) continue;

                var position = new Vector3(origin.x + puff.Offset.X, origin.y + puff.Offset.Y, origin.z + puff.Offset.Z);
                var rotation = Quaternion.AngleAxis((float)(PuffSimulation.YawOf(puff) * Mathf.Rad2Deg), Vector3.up);
                _matrices[puff.Variant].Add(Matrix4x4.TRS(position, rotation, new Vector3(size, size, size)));
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            float reach = (float)_simulation.Settings.EndSize;
            var grow = new Vector3(reach, reach, reach);
            _bounds = new Bounds((min + max) * 0.5f, max - min + grow * 2f);
        }

        private static System.Numerics.Vector3 ToNumerics(Vector3 value) {
            return new System.Numerics.Vector3(value.x, value.y, value.z);
        }

        private static Vector3 ToUnity(System.Numerics.Vector3 value) {
            return new Vector3(value.X, value.Y, value.Z);
        }

        private void DestroyMeshes() {
            for (int variant = 0; variant < _meshes.Length; variant++) {
                if (_meshes[variant] == null) continue;

                if (Application.isPlaying) Destroy(_meshes[variant]);
                else DestroyImmediate(_meshes[variant]);
                _meshes[variant] = null;
            }
        }
    }
}
