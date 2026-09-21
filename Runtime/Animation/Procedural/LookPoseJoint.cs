using UnityEngine;

namespace AlpineLib.Animation.Procedural {
    /// <summary>
    /// One joint of a look chain as <see cref="LookPoseSolver"/> sees it: where it pivots, and how much
    /// of the look it takes.
    /// </summary>
    public readonly struct LookPoseJoint {
        /// <summary>Pivot, in the actor's local space, in the resting pose.</summary>
        public readonly Vector3 Pivot;

        /// <summary>Fraction of a downward look this joint bends by.</summary>
        public readonly float ShareDown;

        /// <summary>Fraction of an upward look this joint bends by.</summary>
        public readonly float ShareUp;

        public LookPoseJoint(Vector3 pivot, float shareDown, float shareUp) {
            Pivot = pivot;
            ShareDown = shareDown;
            ShareUp = shareUp;
        }
    }
}
