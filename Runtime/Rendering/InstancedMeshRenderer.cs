using System;
using AlpineLib.Procedural.Fading;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Rendering {
    /// <summary>
    /// Draws many copies of one shared mesh GPU-instanced (a cell's sleepers or props) through an
    /// <see cref="InstancedBatch"/>: matrices are kept relative to this transform and rebuilt only when it moves
    /// (origin shifts), then submitted per camera. Works in edit mode and play mode; culled as one box. The material
    /// must allow instancing. Can fade in with the stylized screen-door (<see cref="HoldFade"/>).
    /// </summary>
    [ExecuteAlways]
    public sealed class InstancedMeshRenderer : MonoBehaviour {
        /// <summary>Most instances one draw call takes.</summary>
        public const int BatchSize = InstancedBatch.BatchSize;

        private static readonly Matrix4x4[] NoMatrices = Array.Empty<Matrix4x4>();

        private InstancedBatch _batch;

        public int InstanceCount => _batch != null ? _batch.Count : 0;
        public Mesh Mesh => _batch?.Mesh;
        public Material Material => _batch?.Material;
        public int Submesh => _batch != null ? _batch.Submesh : 0;

        /// <summary>The instances and their fade, or null before <see cref="Initialize"/>.</summary>
        public InstancedBatch Batch => _batch;

        /// <summary>How the instances cast shadows (on by default).</summary>
        public ShadowCastingMode ShadowCasting { get; set; } = ShadowCastingMode.On;

        /// <summary>World-space bounds of the last placement.</summary>
        public Bounds WorldBounds => _batch != null ? _batch.WorldBounds(transform.localToWorldMatrix) : default;

        /// <summary>World transforms as last drawn (rebuilt if the cell moved).</summary>
        public Matrix4x4[] WorldMatrices => _batch != null ? _batch.WorldMatrices(transform.localToWorldMatrix) : NoMatrices;

        /// <summary>Sets what to draw (submesh <paramref name="submeshIndex"/> of the mesh); <paramref name="localMatrices"/> are relative to this transform.</summary>
        public void Initialize(Mesh mesh, Material material, Matrix4x4[] localMatrices, Bounds localBounds, int submeshIndex = 0) {
            _batch = new InstancedBatch(mesh, material, submeshIndex, localMatrices, localBounds);
        }

        /// <summary>Holds the instances hidden until the returned schedule starts, then fades them in (see <see cref="InstancedBatch.HoldFade"/>).</summary>
        public FadeInSchedule HoldFade(Material fadeMaterial, double durationSeconds, float maxOffsetSeconds, Func<double> clock) {
            if (_batch == null) throw new InvalidOperationException("Initialize the renderer before fading it.");

            return _batch.HoldFade(fadeMaterial, durationSeconds, maxOffsetSeconds, clock);
        }

        private void OnEnable() {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable() {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera) {
            Draw(camera);
        }

        /// <summary>Submits the instances for <paramref name="camera"/> this frame.</summary>
        public void Draw(Camera camera) {
            if (_batch == null) return;
            // A camera whose mask leaves this layer out (e.g. a map capture) must not get the instances.
            if ((camera.cullingMask & (1 << gameObject.layer)) == 0) return;

            _batch.Draw(camera, gameObject.layer, transform.localToWorldMatrix, ShadowCasting, true);
        }
    }
}
