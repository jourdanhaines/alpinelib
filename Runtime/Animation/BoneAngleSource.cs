using System;
using UnityEngine;

namespace AlpineLib.Animation {
    /// <summary>A bone whose turn about one local axis, away from its rest rotation, drives a blendshape.</summary>
    [Serializable]
    public struct BoneAngleSource {
        [Tooltip("Bone whose local rotation is measured.")]
        public Transform bone;

        [Tooltip("Axis, in the bone's rest local frame, the turn is measured about.")]
        public Vector3 localAxis;

        [Tooltip("Local rotation the angle is measured from.")]
        public Quaternion restLocalRotation;

        public BoneAngleSource(Transform bone, Vector3 localAxis, Quaternion restLocalRotation) {
            this.bone = bone;
            this.localAxis = localAxis;
            this.restLocalRotation = restLocalRotation;
        }
    }
}
