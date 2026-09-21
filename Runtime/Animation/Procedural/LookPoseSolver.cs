using UnityEngine;

namespace AlpineLib.Animation.Procedural {
    /// <summary>
    /// The arithmetic of a look spread along a spine: how far each joint bends, and where that carries
    /// a point riding the end of the chain.
    /// </summary>
    /// <remarks>
    /// Pure, so the bones and the first-person eye can both be derived from the same numbers and agree
    /// by construction — and so a gate can check it without a scene. Pitch is positive down, about the
    /// actor's right axis; joints run from the root of the chain to its tip.
    /// </remarks>
    public static class LookPoseSolver {
        /// <summary>Degrees one joint bends for a look of the given pitch.</summary>
        public static float ResolveBend(in LookPoseJoint joint, float pitchDegrees, float weight) {
            float share = pitchDegrees >= 0f ? joint.ShareDown : joint.ShareUp;

            return pitchDegrees * share * weight;
        }

        /// <summary>
        /// Where a point carried by the tip of the chain ends up, less where it rested: each joint's bend
        /// swings everything above it about that joint's pivot.
        /// </summary>
        public static Vector3 ResolveTipOffset(LookPoseJoint[] joints, Vector3 restingPoint, float pitchDegrees, float weight) {
            Vector3 point = restingPoint;

            // Tip first: a joint's swing must already include what the joints above it did.
            for (int index = joints.Length - 1; index >= 0; index--) {
                Quaternion bend = Quaternion.Euler(ResolveBend(in joints[index], pitchDegrees, weight), 0f, 0f);
                point = joints[index].Pivot + bend * (point - joints[index].Pivot);
            }

            return point - restingPoint;
        }
    }
}
