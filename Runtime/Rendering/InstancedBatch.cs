using System;
using AlpineLib.Procedural.Fading;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Rendering {
    /// <summary>
    /// Many copies of one submesh drawn GPU-instanced: local transforms placed under a parent matrix (rebuilt only
    /// when it changes) and submitted per camera in batches of <see cref="BatchSize"/>. Can fade in with the stylized
    /// screen-door (<see cref="HoldFade"/>): held hidden until started, then drawn with a fading twin and a per-instance
    /// <c>_FadeAmount</c> until every instance is full, then back on the shared material.
    /// </summary>
    public sealed class InstancedBatch {
        /// <summary>Most instances one draw call takes.</summary>
        public const int BatchSize = 1023;

        private readonly Matrix4x4[] _localMatrices;
        private readonly Matrix4x4[] _worldMatrices;
        private readonly Bounds _localBounds;
        private Bounds _worldBounds;
        private Matrix4x4 _placedBy;
        private bool _isPlaced;

        private FadeInSchedule _fade;
        private Material _fadeMaterial;
        private Func<double> _clock;
        private MaterialPropertyBlock[] _blocks;
        private float[][] _amounts;
        private Matrix4x4[][] _chunks;
        private double _filledAt = double.NaN;

        /// <summary>Instances of submesh <paramref name="submesh"/> at <paramref name="localMatrices"/> (kept, not copied) inside <paramref name="localBounds"/>.</summary>
        public InstancedBatch(Mesh mesh, Material material, int submesh, Matrix4x4[] localMatrices, Bounds localBounds) {
            Mesh = mesh;
            Material = material;
            Submesh = submesh;
            _localMatrices = localMatrices ?? Array.Empty<Matrix4x4>();
            _worldMatrices = new Matrix4x4[_localMatrices.Length];
            _localBounds = localBounds;
        }

        public Mesh Mesh { get; }
        public Material Material { get; }
        public int Submesh { get; }
        public int Count => _localMatrices.Length;

        /// <summary>Instance transforms relative to the parent.</summary>
        public Matrix4x4[] LocalMatrices => _localMatrices;

        /// <summary>The fade in progress (held or running), or null.</summary>
        public FadeInSchedule Fade => _fade;

        /// <summary>The material fading draws use, or null when not fading.</summary>
        public Material FadeMaterial => _fadeMaterial;

        /// <summary>
        /// Holds every instance hidden until the returned schedule starts, then fades each in over
        /// <paramref name="durationSeconds"/> after its own delay (stable per local position, below
        /// <paramref name="maxOffsetSeconds"/>), drawn with <paramref name="fadeMaterial"/> on <paramref name="clock"/>.
        /// </summary>
        public FadeInSchedule HoldFade(Material fadeMaterial, double durationSeconds, float maxOffsetSeconds, Func<double> clock) {
            if (fadeMaterial == null) throw new ArgumentNullException(nameof(fadeMaterial));

            var delays = new float[Count];
            for (int index = 0; index < delays.Length; index++) {
                Matrix4x4 local = _localMatrices[index];
                delays[index] = FadeInSchedule.OffsetFor(local.m03, local.m23, maxOffsetSeconds);
            }

            _fade = new FadeInSchedule(durationSeconds, delays);
            _fadeMaterial = fadeMaterial;
            _clock = clock ?? DitherFade.DefaultClock;
            _filledAt = double.NaN;
            CreateFadeBuffers();
            return _fade;
        }

        /// <summary>Stops fading: draws every instance fully with the shared material.</summary>
        public void EndFade() {
            _fade = null;
            _fadeMaterial = null;
            _clock = null;
            _blocks = null;
            _amounts = null;
            _chunks = null;
        }

        /// <summary>True when nothing would draw now: no instances, or a fade still held.</summary>
        public bool IsHidden => Count == 0 || (_fade != null && _fade.IsHeld);

        /// <summary>World-space bounds under <paramref name="toWorld"/>.</summary>
        public Bounds WorldBounds(Matrix4x4 toWorld) {
            Place(toWorld);
            return _worldBounds;
        }

        /// <summary>World transforms under <paramref name="toWorld"/> (the batch's own buffer).</summary>
        public Matrix4x4[] WorldMatrices(Matrix4x4 toWorld) {
            Place(toWorld);
            return _worldMatrices;
        }

        /// <summary>
        /// Submits the instances for <paramref name="camera"/> under <paramref name="toWorld"/>; ends a finished fade first.
        /// Capture cameras (map tiles, portraits) always get every instance in full, so a bake never keeps a fade.
        /// </summary>
        public void Draw(Camera camera, int layer, Matrix4x4 toWorld, ShadowCastingMode shadows, bool receiveShadows) {
            if (Mesh == null || Material == null || Count == 0) return;
            if (_fade != null && CaptureCameraSetup.IsCaptureCamera(camera)) {
                Place(toWorld);
                DrawShared(camera, layer, shadows, receiveShadows);
                return;
            }
            if (_fade != null && _fade.IsHeld) return;

            double now = _fade != null ? _clock() : 0.0;
            if (_fade != null && _fade.IsDone(now)) EndFade();

            Place(toWorld);
            if (_fade == null) {
                DrawShared(camera, layer, shadows, receiveShadows);
                return;
            }

            DrawFading(camera, layer, shadows, receiveShadows, now);
        }

        private void DrawShared(Camera camera, int layer, ShadowCastingMode shadows, bool receiveShadows) {
            RenderParams parameters = CreateParams(Material, camera, layer, shadows, receiveShadows);
            for (int start = 0; start < _worldMatrices.Length; start += BatchSize) {
                int count = Math.Min(BatchSize, _worldMatrices.Length - start);
                Graphics.RenderMeshInstanced(parameters, Mesh, Submesh, _worldMatrices, count, start);
            }
        }

        // One block per chunk, each array starting at its chunk's first instance.
        private void DrawFading(Camera camera, int layer, ShadowCastingMode shadows, bool receiveShadows, double now) {
            bool refill = !now.Equals(_filledAt);
            _filledAt = now;
            for (int chunk = 0; chunk < _blocks.Length; chunk++) {
                int start = chunk * BatchSize;
                int count = Math.Min(BatchSize, Count - start);
                if (refill) {
                    _fade.Fill(_amounts[chunk], start, count, now);
                    _blocks[chunk].SetFloatArray(DitherFade.AmountId, _amounts[chunk]);
                }

                RenderParams parameters = CreateParams(_fadeMaterial, camera, layer, shadows, receiveShadows);
                parameters.matProps = _blocks[chunk];
                Matrix4x4[] matrices = _chunks != null ? _chunks[chunk] : _worldMatrices;
                Graphics.RenderMeshInstanced(parameters, Mesh, Submesh, matrices, count, 0);
            }
        }

        private void CreateFadeBuffers() {
            int chunks = (Count + BatchSize - 1) / BatchSize;
            _blocks = new MaterialPropertyBlock[chunks];
            _amounts = new float[chunks][];
            _chunks = chunks > 1 ? new Matrix4x4[chunks][] : null;
            for (int chunk = 0; chunk < chunks; chunk++) {
                int count = Math.Min(BatchSize, Count - chunk * BatchSize);
                _blocks[chunk] = new MaterialPropertyBlock();
                _amounts[chunk] = new float[count];
                if (_chunks != null) _chunks[chunk] = new Matrix4x4[count];
            }
            _isPlaced = false;
        }

        private RenderParams CreateParams(Material material, Camera camera, int layer, ShadowCastingMode shadows, bool receiveShadows) {
            return new RenderParams(material) {
                camera = camera,
                layer = layer,
                worldBounds = _worldBounds,
                shadowCastingMode = shadows,
                receiveShadows = receiveShadows,
            };
        }

        private void Place(Matrix4x4 toWorld) {
            if (_isPlaced && toWorld.Equals(_placedBy)) return;

            for (int index = 0; index < _localMatrices.Length; index++) {
                _worldMatrices[index] = toWorld * _localMatrices[index];
            }
            if (_chunks != null) CopyChunks();
            _worldBounds = TransformBounds(toWorld, _localBounds);
            _placedBy = toWorld;
            _isPlaced = true;
        }

        private void CopyChunks() {
            for (int chunk = 0; chunk < _chunks.Length; chunk++) {
                Array.Copy(_worldMatrices, chunk * BatchSize, _chunks[chunk], 0, _chunks[chunk].Length);
            }
        }

        /// <summary>Axis-aligned bounds of <paramref name="local"/> under <paramref name="toWorld"/> (rotation, scale, translation).</summary>
        public static Bounds TransformBounds(Matrix4x4 toWorld, Bounds local) {
            Vector3 centre = toWorld.MultiplyPoint3x4(local.center);
            Vector3 extents = local.extents;
            var worldExtents = new Vector3(
                Mathf.Abs(toWorld.m00) * extents.x + Mathf.Abs(toWorld.m01) * extents.y + Mathf.Abs(toWorld.m02) * extents.z,
                Mathf.Abs(toWorld.m10) * extents.x + Mathf.Abs(toWorld.m11) * extents.y + Mathf.Abs(toWorld.m12) * extents.z,
                Mathf.Abs(toWorld.m20) * extents.x + Mathf.Abs(toWorld.m21) * extents.y + Mathf.Abs(toWorld.m22) * extents.z);
            return new Bounds(centre, worldExtents * 2f);
        }
    }
}
