using System;
using AlpineLib.Procedural.Expressions;
using UnityEngine;

namespace AlpineLib.Face {
    /// <summary>
    /// A face piece's expression data, carried on the item prefab beside its skinned renderer: which of the
    /// renderer's blendshapes plays each <see cref="FaceChannel"/>, and how this piece reacts (its tuning).
    /// </summary>
    /// <remarks>
    /// Passive: nothing here runs. A <see cref="FaceExpressionDriver"/> on the wearer finds it when the piece is
    /// built and drives the shapes; without a driver (a portrait copy, a preview) the piece stays at its rest pose.
    /// </remarks>
    [DisallowMultipleComponent]
    public class FacePoseSet : MonoBehaviour {
        [Tooltip("Renderer carrying the shapes; this object's when empty.")]
        [SerializeField] private SkinnedMeshRenderer target;

        [SerializeField] private FacePoseBinding[] bindings = Array.Empty<FacePoseBinding>();

        [SerializeField] private FaceExpressionTuning tuning = new FaceExpressionTuning();

        /// <summary>The renderer the shapes are written to.</summary>
        public SkinnedMeshRenderer Target => target != null ? target : GetComponent<SkinnedMeshRenderer>();

        /// <summary>Channel to blendshape bindings.</summary>
        public FacePoseBinding[] Bindings => bindings;

        /// <summary>How the piece reacts.</summary>
        public FaceExpressionTuning Tuning => tuning;

        /// <summary>Sets everything at once, e.g. from an assembler; a null tuning means the defaults.</summary>
        public void Configure(SkinnedMeshRenderer renderer, FacePoseBinding[] channelBindings, FaceExpressionTuning reactions) {
            target = renderer;
            bindings = channelBindings ?? Array.Empty<FacePoseBinding>();
            tuning = reactions ?? new FaceExpressionTuning();
        }

        /// <summary>
        /// Blendshape index per channel on <see cref="Target"/> (-1 for an unbound channel or a missing shape) and the
        /// shape weight each channel's full strength maps to; missing shapes are listed in <paramref name="missing"/>.
        /// </summary>
        public void Resolve(int[] indices, float[] fullWeights, out string missing) {
            missing = null;
            for (int index = 0; index < indices.Length; index++) {
                indices[index] = -1;
                fullWeights[index] = 0f;
            }

            Mesh mesh = Target != null ? Target.sharedMesh : null;
            foreach (FacePoseBinding binding in bindings) {
                int channel = (int)binding.Channel;
                if (channel < 0 || channel >= indices.Length) continue;

                int shape = mesh != null && !string.IsNullOrEmpty(binding.BlendShape) ? mesh.GetBlendShapeIndex(binding.BlendShape) : -1;
                if (shape < 0) {
                    missing = missing == null ? binding.BlendShape : $"{missing}, {binding.BlendShape}";
                    continue;
                }

                indices[channel] = shape;
                fullWeights[channel] = binding.FullWeight;
            }
        }
    }
}
