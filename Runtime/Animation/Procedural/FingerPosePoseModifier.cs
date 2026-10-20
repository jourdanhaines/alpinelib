using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Animation.Procedural {
    /// <summary>
    /// Holds a set of bones at fixed local rotations, weighted: a grip the humanoid retarget cannot carry
    /// (finger twist, a distal joint bent off its curl axis) written straight onto the skeleton.
    /// </summary>
    /// <remarks>
    /// Poses run in their listed order, parents before children, each turned to its parent's rotation as
    /// it now stands times the stored local rotation and blended from the animated pose by the effective
    /// weight: <see cref="PoseModifier.Weight"/> times the optional follow layer's (as
    /// <see cref="ArmReachPoseModifier"/> does, so a hand and its fingers ramp together). Ordered at
    /// <see cref="DefaultOrder"/> (20), after the arm reach, so the fingers ride the solved hands.
    /// Stateless and allocation free per frame.
    /// </remarks>
    public class FingerPosePoseModifier : PoseModifier {
        /// <summary>Stack order new components get: after <see cref="ArmReachPoseModifier"/> at 10.</summary>
        public const int DefaultOrder = 20;

        /// <summary>One bone and the local rotation (parent-relative) it is held at.</summary>
        [Serializable]
        public struct BonePose {
            public Transform bone;
            public Quaternion localRotation;

            public BonePose(Transform bone, Quaternion localRotation) {
                this.bone = bone;
                this.localRotation = localRotation;
            }
        }

        [Tooltip("Optional layer blend whose weight scales this layer, e.g. the upper-body hold layer.")]
        [SerializeField] private AnimatorLayerBlend followLayer;

        [Tooltip("Bones and the local rotations they are held at, parents before children.")]
        [SerializeField] private BonePose[] poses = Array.Empty<BonePose>();

        public FingerPosePoseModifier() : base(DefaultOrder) {
        }

        /// <summary>Effective weight this frame: the layer's own weight times the follow layer's.</summary>
        public float EffectiveWeight => followLayer != null ? Weight * followLayer.Weight : Weight;

        /// <summary>The held bones, in apply order.</summary>
        public IReadOnlyList<BonePose> Poses => poses;

        /// <summary>Replaces the held bones; list parents before children.</summary>
        public void SetPoses(IReadOnlyList<BonePose> bonePoses) {
            poses = new BonePose[bonePoses.Count];
            for (int index = 0; index < poses.Length; index++) poses[index] = bonePoses[index];
        }

        /// <inheritdoc />
        public override void Bind(Transform actorRoot, PoseBones bones) {
        }

        /// <inheritdoc />
        public override void Apply(PoseBones bones, float deltaTime) {
            float weight = EffectiveWeight;
            if (weight <= 0f) return;

            foreach (BonePose pose in poses) {
                if (pose.bone == null || pose.bone.parent == null) continue;

                Quaternion target = pose.bone.parent.rotation * pose.localRotation;
                bones.RotateWorldTo(pose.bone, weight >= 1f ? target : Quaternion.Slerp(pose.bone.rotation, target, weight));
            }
        }
    }
}
