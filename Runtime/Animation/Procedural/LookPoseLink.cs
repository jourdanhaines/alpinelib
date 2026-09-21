using System;
using UnityEngine;

namespace AlpineLib.Animation.Procedural {
    /// <summary>
    /// Authored share of a look one humanoid bone takes; see <see cref="LookPoseModifier"/>.
    /// </summary>
    [Serializable]
    public struct LookPoseLink {
        [Tooltip("Bone that bends. One the avatar does not map hands its share to the next link.")]
        public HumanBodyBones bone;

        [Tooltip("Fraction of a downward look this bone takes.")]
        [Range(0f, 1f)]
        public float shareDown;

        [Tooltip("Fraction of an upward look this bone takes.")]
        [Range(0f, 1f)]
        public float shareUp;

        public LookPoseLink(HumanBodyBones bone, float shareDown, float shareUp) {
            this.bone = bone;
            this.shareDown = shareDown;
            this.shareUp = shareUp;
        }
    }
}
