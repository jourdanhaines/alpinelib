using UnityEngine;

namespace AlpineLib.Actors.Locomotion {
    /// <summary>
    /// The simulated state of a capsule motor, kept in the frame of the carrier it stands on.
    /// </summary>
    /// <remarks>
    /// Everything positional is relative to <see cref="CarrierRoot"/> — world space when it is null — so a
    /// pawn riding a train stores the metre it walked, never the forty it was carried. Yaw is relative to
    /// the carrier's heading; the planar velocity is in the carrier's yaw frame with no vertical part.
    /// <see cref="LocalPosition"/> is the capsule's foot point.
    /// </remarks>
    public struct MotorState {
        public Vector3 LocalPosition;
        public float LocalYaw;
        public Vector3 LocalPlanarVelocity;
        public float VerticalVelocity;
        public bool Grounded;
        public Vector3 GroundNormal;
        public Transform CarrierRoot;
        public bool JumpedThisStep;

        /// <summary>A state standing still at a world pose, airborne until the first probe says otherwise.</summary>
        public static MotorState AtWorld(Vector3 position, float yawDegrees) {
            return new MotorState {
                LocalPosition = position,
                LocalYaw = yawDegrees,
                GroundNormal = Vector3.up
            };
        }
    }
}
