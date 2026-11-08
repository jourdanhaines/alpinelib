using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Sky {
    /// <summary>
    /// Ground past the streamed world's edge: per camera, a world-anchored <see cref="FarGroundLattice"/> coloured like
    /// the consumer's ground (<see cref="SetGroundSource"/>), holed about the camera and reaching
    /// <see cref="FarGroundSettings.OuterRadius"/>, so the edge does not show through translucent fog.
    /// </summary>
    /// <remarks>
    /// Placed through an <see cref="IAbsoluteFrame"/> (the lattice anchor snaps in absolute xz, so floating-origin
    /// shifts recolour nothing) whenever the camera moves <see cref="RebuildDistance"/>. Drawn per camera from
    /// <c>beginCameraRendering</c> on one layer through <see cref="SkyCameraFilter"/>, shadows off both ways and no
    /// probes. Without a ground source nothing draws. Edit mode frees the lattices on disable.
    /// </remarks>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class FarGround : MonoBehaviour {
        /// <summary>Camera motion, metres, before the lattice is placed again.</summary>
        public const float RebuildDistance = 0.01f;

        /// <summary>Fog distance scale the ground material should carry: fogged exactly like the world it continues.</summary>
        public const float GroundFogScale = 1f;

        private const int StaleViewFrames = 120;

        private readonly Dictionary<int, FarGroundView> _views = new Dictionary<int, FarGroundView>();
        private readonly List<int> _stale = new List<int>();
        private FarGroundSettings _settings = FarGroundSettings.Default;
        private IAbsoluteFrame _frame;
        private Material _material;
        private Func<double, double, Color32> _colourAt;
        private int _layer = -1;
        private int _revision;
        private bool _isSubscribed;

        /// <summary>The settings of the last <see cref="Configure"/> (defaults before).</summary>
        public FarGroundSettings Settings => _settings;

        /// <summary>The layer the lattice draws on (-1 before <see cref="Configure"/>).</summary>
        public int Layer => _layer;

        public bool IsConfigured => _layer >= 0;

        /// <summary>The material the lattice draws through (not owned), or null without a ground source.</summary>
        public Material GroundSourceMaterial => _material;

        /// <summary>True when a material and colour source are set, so the lattice draws.</summary>
        public bool HasGroundSource => _material != null && _colourAt != null;

        /// <summary>
        /// Draws on <paramref name="layer"/>, placed through <paramref name="frame"/> (null: world xz is absolute),
        /// with <paramref name="settings"/> (null: defaults; validated, so a bad value throws here).
        /// </summary>
        public void Configure(int layer, IAbsoluteFrame frame, FarGroundSettings settings = null) {
            if (layer < 0 || layer > 31) throw new ArgumentOutOfRangeException(nameof(layer), layer, "Layer must be 0..31.");

            FarGroundSettings chosen = settings ?? FarGroundSettings.Default;
            chosen.Validate();
            _settings = chosen;
            _frame = frame;
            _layer = layer;
            DisposeViews();
            _revision++;
        }

        /// <summary>
        /// Draws the lattice through <paramref name="material"/> (not owned; vertex-coloured, fogged like the world's
        /// ground) with vertex colours (sRGB) from <paramref name="colourAt"/>(absolute x, absolute z). Null for either
        /// draws nothing. Survives <see cref="Configure"/>.
        /// </summary>
        public void SetGroundSource(Material material, Func<double, double, Color32> colourAt) {
            bool isSet = material != null && colourAt != null;
            _material = isSet ? material : null;
            _colourAt = isSet ? colourAt : null;
            DisposeViews();
            _revision++;
        }

        /// <summary>The lattice <paramref name="camera"/> draws, or null (no ground source, or not placed yet).</summary>
        public FarGroundLattice LatticeOf(Camera camera) {
            return TryGetView(camera, out FarGroundView view) ? view.Lattice : null;
        }

        /// <summary>Where the lattice was last placed for <paramref name="camera"/> (world matrix and bounds).</summary>
        public bool TryGetPlacement(Camera camera, out Matrix4x4 matrix, out Bounds bounds) {
            matrix = Matrix4x4.identity;
            bounds = default;
            if (!TryGetView(camera, out FarGroundView view) || !view.IsPlaced || view.Lattice == null) return false;

            matrix = view.Matrix;
            bounds = view.Bounds;
            return true;
        }

        /// <summary>
        /// Places the lattice for <paramref name="camera"/> as its next draw would and returns it, or null when the
        /// camera is filtered out (<see cref="SkyCameraFilter"/>) or nothing draws.
        /// </summary>
        public FarGroundLattice Place(Camera camera) {
            if (!IsReady || !SkyCameraFilter.ShouldDraw(camera, _layer)) return null;

            return ViewFor(camera).Lattice;
        }

        /// <summary>Queues the lattice for <paramref name="camera"/>'s next render; returns the draws issued (0 or 1).</summary>
        public int Submit(Camera camera) {
            if (!IsReady || !SkyCameraFilter.ShouldDraw(camera, _layer)) return 0;

            FarGroundView view = ViewFor(camera);
            if (view.Lattice == null || view.Lattice.Mesh == null) return 0;

            Graphics.RenderMesh(CreateParams(camera, view.Bounds), view.Lattice.Mesh, 0, view.Matrix);
            return 1;
        }

        private bool IsReady => IsConfigured && HasGroundSource;

        private void OnEnable() {
            if (_isSubscribed) return;

            RenderPipelineManager.beginCameraRendering += HandleBeginCamera;
            _isSubscribed = true;
        }

        // Edit mode frees its lattices here (domain reloads and scene saves skip OnDestroy); the next draw rebuilds them.
        private void OnDisable() {
            if (!Application.isPlaying) DisposeViews();
            if (!_isSubscribed) return;

            RenderPipelineManager.beginCameraRendering -= HandleBeginCamera;
            _isSubscribed = false;
        }

        private void OnDestroy() {
            DisposeViews();
        }

        private void HandleBeginCamera(ScriptableRenderContext context, Camera camera) {
            Submit(camera);
        }

        private bool TryGetView(Camera camera, out FarGroundView view) {
            view = null;
            return camera != null && _views.TryGetValue(camera.GetInstanceID(), out view);
        }

        private FarGroundView ViewFor(Camera camera) {
            int id = camera.GetInstanceID();
            if (!_views.TryGetValue(id, out FarGroundView view)) {
                view = new FarGroundView();
                _views.Add(id, view);
            }

            view.LastUsedFrame = Time.frameCount;
            Vector3 world = camera.transform.position;
            ResolveAbsolute(world, out double absoluteX, out double absoluteZ);
            if (!NeedsPlacing(view, world, absoluteX, absoluteZ)) return view;

            PlaceView(view, world, absoluteX, absoluteZ);
            DropStaleViews();
            return view;
        }

        private void ResolveAbsolute(Vector3 world, out double absoluteX, out double absoluteZ) {
            if (_frame != null && _frame.TryToAbsolute(world, out absoluteX, out absoluteZ)) return;

            absoluteX = world.x;
            absoluteZ = world.z;
        }

        private bool NeedsPlacing(FarGroundView view, Vector3 world, double absoluteX, double absoluteZ) {
            if (!view.IsPlaced || view.Revision != _revision || view.Lattice == null || view.Lattice.Mesh == null) return true;
            if ((view.CameraWorld - world).sqrMagnitude > RebuildDistance * RebuildDistance) return true;

            double deltaX = view.AbsoluteX - absoluteX;
            double deltaZ = view.AbsoluteZ - absoluteZ;
            return deltaX * deltaX + deltaZ * deltaZ > RebuildDistance * RebuildDistance;
        }

        // The camera's lattice is recoloured about the snapped anchor and drawn there, below the frame's ground height.
        private void PlaceView(FarGroundView view, Vector3 world, double absoluteX, double absoluteZ) {
            if (view.Lattice == null || view.Lattice.Mesh == null) {
                view.Lattice?.Dispose();
                view.Lattice = new FarGroundLattice(_settings.Spacing, _settings.Snap, _settings.HoleRadius, _settings.OuterRadius, _colourAt);
            }

            view.Lattice.AnchorFor(absoluteX, absoluteZ, out double anchorX, out double anchorZ);
            view.Lattice.Recolour(anchorX, anchorZ);
            float groundAt = (_frame != null ? _frame.GroundY : 0f) - (float)_settings.Drop;
            var centre = new Vector3(world.x + (float)(anchorX - absoluteX), groundAt, world.z + (float)(anchorZ - absoluteZ));
            float extent = (float)_settings.OuterRadius * 2f;
            view.Matrix = Matrix4x4.Translate(centre);
            view.Bounds = new Bounds(centre, new Vector3(extent, 1f, extent));
            view.CameraWorld = world;
            view.AbsoluteX = absoluteX;
            view.AbsoluteZ = absoluteZ;
            view.Revision = _revision;
            view.IsPlaced = true;
        }

        private void DropStaleViews() {
            _stale.Clear();
            foreach (KeyValuePair<int, FarGroundView> entry in _views) {
                if (Time.frameCount - entry.Value.LastUsedFrame > StaleViewFrames) _stale.Add(entry.Key);
            }
            foreach (int id in _stale) {
                _views[id].Lattice?.Dispose();
                _views.Remove(id);
            }
        }

        private void DisposeViews() {
            foreach (FarGroundView view in _views.Values) view.Lattice?.Dispose();
            _views.Clear();
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
    }
}
