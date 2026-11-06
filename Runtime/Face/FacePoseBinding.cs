using System;
using AlpineLib.Procedural.Expressions;
using UnityEngine;

namespace AlpineLib.Face {
    /// <summary>Which blendshape plays a face channel, and the shape weight (0..1) the channel's full strength maps to.</summary>
    [Serializable]
    public struct FacePoseBinding {
        [SerializeField] private FaceChannel channel;
        [SerializeField] private string blendShape;
        [Range(0f, 1f)]
        [SerializeField] private float fullWeight;

        public FacePoseBinding(FaceChannel channel, string blendShape, float fullWeight = 1f) {
            this.channel = channel;
            this.blendShape = blendShape;
            this.fullWeight = Mathf.Clamp01(fullWeight);
        }

        /// <summary>The channel played.</summary>
        public FaceChannel Channel => channel;

        /// <summary>Name of the blendshape on the pose set's renderer.</summary>
        public string BlendShape => blendShape;

        /// <summary>Shape weight, 0..1, at the channel's full strength.</summary>
        public float FullWeight => fullWeight;
    }
}
