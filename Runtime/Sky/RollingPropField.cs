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
    /// ready drift; after drawing nothing while waiting the props scale in over <see cref="FadeSeconds"/>. Whenever the
    /// drift lands more than <see cref="JumpMetres"/> past what the wind could blow in the real time since the last
    /// update (the first ready drift after a hold, a clock resync), the props glide from where they were drawn to the
    /// live drift over <see cref="FadeSeconds"/> instead of popping, steps clamped to <see cref="MaxStepSeconds"/> (as
    /// <see cref="CloudField"/> does). Drawn per camera from <c>beginCameraRendering</c> through
    /// <c>Graphics.RenderMeshInstanced</c> with shadows cast and received, on one layer, never for capture cameras
    /// (<see cref="SkyCameraFilter"/>), only within the draw distance of the camera; a root whose props' box is out
    /// of reach is skipped before posing. Roots destroyed without <see cref="Unregister"/> are dropped at the next draw.
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

        // Headroom on the wind's speed × real time a drift step may cover before it counts as a jump (gusts between updates).
        private const double TravelSlack = 2.0;

        private const int MaxInstancesPerDraw = 1023;

        private readonly List<Mesh> _meshes = new List<Mesh>();
        private readonly List<Material[]> _materials = new List<Material[]>();
        private readonly List<Transform> _roots = new List<Transform>();
        private readonly List<RollingProp[]> _props = new List<RollingProp[]>();
        private readonly List<Vector4> _rootReach = new List<Vector4>();
        private readonly RollingPropBatch _batch = new RollingPropBatch();
        private IWindService _wind;
        private int _layer = -1;
        private float _drawDistance = 200f;
        private float _reach = 1f;
        private bool _isConfigured;
        private bool _isSubscribed;
        private WindVector _displacement;
        private WindVector _target;
        private WindVector _glide;
        private bool _hasDisplacement;
        private bool _wasReady;
        private bool _hasWaited;
        private float _fade = 1f;
        private float _glideShare = 1f;
        private double _lastStep;

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

        /// <summary>True while the props glide from a jumped-over drift to the live one.</summary>
        public bool IsGliding => _glideShare < 1f;

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
            _rootReach.Clear();
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

            Vector4 reach = ReachOf(copy);
            int existing = _roots.IndexOf(root);
            if (existing >= 0) {
                _props[existing] = copy;
                _rootReach[existing] = reach;
                return;
            }

            _roots.Add(root);
            _props.Add(copy);
            _rootReach.Add(reach);
        }

        /// <summary>Drops <paramref name="root"/>'s props; false when it was not registered.</summary>
        public bool Unregister(Transform root) {
            int index = _roots.IndexOf(root);
            if (index < 0) return false;

            RemoveRootAt(index);
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
            _target = WindVector.Zero;
            _glide = WindVector.Zero;
            _hasDisplacement = false;
            _wasReady = false;
            _hasWaited = false;
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

        private void Rebuild(Vector3 camera) {
            _batch.Clear(_meshes.Count);
            float reachSquared = _drawDistance * _drawDistance;
            for (int index = _roots.Count - 1; index >= 0; index--) {
                if (_roots[index] == null) RemoveRootAt(index);
            }

            for (int index = 0; index < _roots.Count; index++) {
                Transform root = _roots[index];
                if (!root.gameObject.activeInHierarchy || !IsInReach(root, _rootReach[index], camera)) continue;

                AddRoot(root.localToWorldMatrix, _props[index], camera, reachSquared);
            }

            _batch.CloseBounds(_reach * 2f);
        }

        private void RemoveRootAt(int index) {
            _roots.RemoveAt(index);
            _props.RemoveAt(index);
            _rootReach.RemoveAt(index);
        }

        // The props' box centre in the root's plane (xyz) and its half-diagonal plus the widest mesh at the largest scale (w).
        private Vector4 ReachOf(RollingProp[] props) {
            if (props.Length == 0) return Vector4.zero;

            var box = new Bounds();
            float largest = 0f;
            for (int index = 0; index < props.Length; index++) {
                largest = Mathf.Max(largest, props[index].Scale);
                props[index].Path.Bounds(0.0, out double minX, out double minY, out double minZ, out double maxX, out double maxY, out double maxZ);
                var part = new Bounds();
                part.SetMinMax(new Vector3((float)minX, (float)minY, (float)minZ), new Vector3((float)maxX, (float)maxY, (float)maxZ));
                if (index == 0) box = part;
                else box.Encapsulate(part);
            }
            Vector3 centre = box.center;
            return new Vector4(centre.x, centre.y, centre.z, box.extents.magnitude + _reach * largest);
        }

        // A root is posed only when its props' box can reach within the draw distance of the camera.
        private bool IsInReach(Transform root, Vector4 reach, Vector3 camera) {
            Vector3 centre = root.TransformPoint(new Vector3(reach.x, reach.y, reach.z));
            float limit = _drawDistance + reach.w;
            return (centre - camera).sqrMagnitude <= limit * limit;
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
