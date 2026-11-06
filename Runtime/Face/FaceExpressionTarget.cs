using AlpineLib.Procedural.Expressions;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Face {
    /// <summary>One worn <see cref="FacePoseSet"/> a <see cref="FaceExpressionDriver"/> drives: its renderer, resolved shapes and mixer.</summary>
    public sealed class FaceExpressionTarget {
        private const float UnityFullWeight = 100f;

        private readonly int[] _indices = new int[FaceExpressionMixer.ChannelCount];
        private readonly float[] _fullWeights = new float[FaceExpressionMixer.ChannelCount];

        public FaceExpressionTarget(FacePoseSet poseSet, uint seed) {
            PoseSet = poseSet;
            Renderer = poseSet.Target;
            Mixer = new FaceExpressionMixer(poseSet.Tuning.Clone(), seed);
            poseSet.Resolve(_indices, _fullWeights, out string missing);
            MissingShapes = missing;
        }

        /// <summary>The pose set driven.</summary>
        public FacePoseSet PoseSet { get; }

        /// <summary>The renderer written to.</summary>
        public SkinnedMeshRenderer Renderer { get; }

        /// <summary>The weights' source.</summary>
        public FaceExpressionMixer Mixer { get; }

        /// <summary>Bound shapes the renderer's mesh lacks, or null.</summary>
        public string MissingShapes { get; }

        /// <summary>True while the piece draws; a first-person shadow-only or switched-off piece is not written.</summary>
        public bool IsDrawn => Renderer != null && Renderer.enabled && !Renderer.forceRenderingOff
            && Renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly && Renderer.gameObject.activeInHierarchy;

        /// <summary>Blendshape index driving <paramref name="channel"/>, or -1.</summary>
        public int IndexOf(FaceChannel channel) {
            return _indices[(int)channel];
        }

        /// <summary>Writes the mixer's weights to the renderer, or rest when <paramref name="atRest"/>.</summary>
        public void Write(bool atRest) {
            if (Renderer == null) return;

            for (int channel = 0; channel < _indices.Length; channel++) {
                if (_indices[channel] < 0) continue;

                float weight = atRest ? 0f : Mixer.Weights[channel] * _fullWeights[channel] * UnityFullWeight;
                Renderer.SetBlendShapeWeight(_indices[channel], weight);
            }
        }
    }
}
