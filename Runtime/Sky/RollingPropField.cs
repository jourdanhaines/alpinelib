using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Sky;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Sky {
    /// <summary>
    /// Draws wind-rolled props (tumbleweeds): each streamed cell root registers its <see cref="RollingProp"/>s and the
    /// field poses every one from the wind's drift (<see cref="RollerPose"/>) under its root, instanced per kind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stateless and client-side: a prop's pose is a pure function of its path and the drift, so every client with the
    /// same session time draws the same roll, and a floating-origin shift moves the props with their cell roots. Nothing
    /// collides or replicates.
    /// </para>
    /// <para>
    /// While the wind is not ready (a cold join replaying) the last drift is held and nothing is drawn before the first
    /// ready drift; when it lands more than <see cref="JumpMetres"/> from the held one the props scale in over
    /// <see cref="FadeSeconds"/>. Drawn per camera from <c>beginCameraRendering</c> through
    /// <c>Graphics.RenderMeshInstanced</c> with shadows cast and received, on one layer, never for capture cameras
    /// (<see cref="SkyCameraFilter"/>), only within the draw distance of the camera. Roots destroyed without
    /// <see cref="Unregister"/> are dropped at the next draw.
    /// </para>
    /// </remarks>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class RollingPropField : MonoBehaviour {
        /// <summary>Most the drift may jump when the wind turns ready before props scale in rather than pop.</summary>
        public const double JumpMetres = 1.0;

        /// <summary>Seconds props take to scale in after a jump.</summary>
        public const float FadeSeconds = 0.5f;

        private const int MaxInstancesPerDraw = 1023;

        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<Material[]> _materials = new List<Material[]>();
        private readonly List<Transform> _roots = new List<Transform>();
        private readonly List<RollingProp[]> _props = new List<RollingProp[]>();
        private readonly RollingPropBatch _batch = new RollingPropBatch();
        private IWindService _wind;
        private int _layer = -1;
        private float _drawDistance = 200f;
        private float _reach = 1f;
        private bool _isConfigured;
        private bool _isSubscribed;
        private WindVector _displacement;
        private bool _hasDisplacement;
        private bool _wasReady;
        private bool _hasWaited;
        private float _fade = 1f;
        private int _fadeFrame = -1;

        /// <summary>The layer every instance draws on.</summary>
        public int Layer => _layer;

        /// <summary>Farthest a prop is drawn from the camera, metres.</summary>
        public float DrawDistance => _drawDistance;

        /// <summary>Kinds added so far.</summary>
        public int KindCount => _meshes.Count;

        /// <summary>Cell roots registered.</summary>
        public int RootCount => _roots.Count;

        /// <summary>Props registered across every root.</summary>
        public int PropCount {
            get {
                int count = 0;
                foreach (RollingProp[] props in _props) count += props.Length;

                return count;
            }
        }

        /// <summary>Scale-in share applied to every prop: below 1 while they fade in after a wind jump.</summary>
        public float Fade => _fade;

        /// <summary>The drift the props are posed with (held while the wind is not ready).</summary>
        public WindVector Displacement => _displacement;

        /// <summary>
        /// Poses props on <paramref name="wind"/>'s drift (still props when null) and draws them on <paramref name="layer"/>
        /// within <paramref name="drawDistance"/> metres of each camera. Keeps kinds and registrations.
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
        /// Adds a kind drawn as <paramref name="mesh"/> with one instanced material per submesh (not owned) and returns its
        /// index for <see cref="RollingProp.Kind"/>.
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

        /// <summary>Forgets every kind and registration.</summary>
        public void Clear() {
            _meshes.Clear();
            _materials.Clear();
            _roots.Clear();
            _props.Clear();
            _reach = 1f;
        }

        /// <summary>Registers (or replaces) the props rolling in <paramref name="root"/>'s local plane.</summary>
        public void Register(Transform root, IReadOnlyList<RollingProp> props) {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (props == null) throw new ArgumentNullException(nameof(props));

            var copy = new RollingProp[props.Count];
            for (int index = 0; index < copy.Length; index++) {
                if (props[index].Path == null || props[index].Kind < 0 || props[index].Kind >= _meshes.Count) {
                    throw new ArgumentException($"Prop {index} has no path or an unknown kind {props[index].Kind}.", nameof(props));
                }
                copy[index] = props[index];
            }

            int existing = _roots.IndexOf(root);
            if (existing >= 0) {
                _props[existing] = copy;
                return;
            }

            _roots.Add(root);
            _props.Add(copy);
        }

        /// <summary>Drops <paramref name="root"/>'s props; false when it was not registered.</summary>
        public bool Unregister(Transform root) {
            int index = _roots.IndexOf(root);
            if (index < 0) return false;

            _roots.RemoveAt(index);
            _props.RemoveAt(index);
            return true;
        }

        /// <summary>True when <paramref name="root"/> has props registered.</summary>
        public bool IsRegistered(Transform root) {
            return _roots.IndexOf(root) >= 0;
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

            Rebuild(camera.transform.position);
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

        private void ResetWind() {
            _displacement = WindVector.Zero;
            _hasDisplacement = false;
            _wasReady = false;
            _hasWaited = false;
            _fade = 1f;
            _fadeFrame = -1;
        }

        // Holds the last drift while the wind replays; the first ready drift after a wait or a jump scales the props in.
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

            WindVector next = _wind.Displacement;
            if (!_wasReady && IsJump(next)) {
                _fade = 0f;
                _fadeFrame = Time.frameCount;
            }

            _displacement = next;
            _hasDisplacement = true;
            _wasReady = true;
            AdvanceFade();
        }

        private bool IsJump(WindVector next) {
            if (!_hasDisplacement) return _hasWaited;

            return (next - _displacement).Length > JumpMetres;
        }

        private void AdvanceFade() {
            if (_fade >= 1f || _fadeFrame == Time.frameCount) return;

            _fadeFrame = Time.frameCount;
            _fade = Mathf.Min(1f, _fade + Time.unscaledDeltaTime / FadeSeconds);
        }

        private void Rebuild(Vector3 camera) {
            _batch.Clear(_meshes.Count);
            float reachSquared = _drawDistance * _drawDistance;
            for (int index = _roots.Count - 1; index >= 0; index--) {
                if (_roots[index] != null) continue;

                _roots.RemoveAt(index);
                _props.RemoveAt(index);
            }

            for (int index = 0; index < _roots.Count; index++) {
                Transform root = _roots[index];
                if (!root.gameObject.activeInHierarchy) continue;

                AddRoot(root.localToWorldMatrix, _props[index], camera, reachSquared);
            }

            _batch.CloseBounds(_reach * 2f);
        }

        private void AddRoot(Matrix4x4 toWorld, RollingProp[] props, Vector3 camera, float reachSquared) {
            foreach (RollingProp prop in props) {
                RollerPose pose = RollerPose.Of(prop.Path, _displacement);
                float scale = (float)pose.Scale * _fade * prop.Scale;
                if (scale <= 0f) continue;

                Vector3 world = toWorld.MultiplyPoint3x4(new Vector3((float)pose.X, (float)pose.Y, (float)pose.Z));
                if ((world - camera).sqrMagnitude > reachSquared) continue;

                var rotation = new Quaternion((float)pose.RotationX, (float)pose.RotationY, (float)pose.RotationZ, (float)pose.RotationW);
                var local = Matrix4x4.TRS(new Vector3((float)pose.X, (float)pose.Y, (float)pose.Z), rotation, new Vector3(scale, scale, scale));
                _batch.Add(prop.Kind, toWorld * local);
            }
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
