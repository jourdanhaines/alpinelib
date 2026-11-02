using System;
using UnityEngine;

namespace AlpineLib.Animation.Bones {
    /// <summary>One humanoid bone slot and the name of the generic-rig transform that fills it.</summary>
    [Serializable]
    public struct BoneMapEntry {
        [Tooltip("Humanoid slot this transform stands in for.")]
        public HumanBodyBones bone;

        [Tooltip("Exact name of the transform under the skeleton root.")]
        public string transformName;

        public BoneMapEntry(HumanBodyBones bone, string transformName) {
            this.bone = bone;
            this.transformName = transformName;
        }
    }
}
