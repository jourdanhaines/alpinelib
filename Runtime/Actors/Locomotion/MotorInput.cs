using UnityEngine;

namespace AlpineLib.Actors.Locomotion {
    /// <summary>
    /// One step's worth of intent for the motor.
    /// </summary>
    public struct MotorInput {
        /// <summary>World-space planar direction of travel, at most unit length.</summary>
        public Vector3 MoveDirection;

        /// <summary>Full speed of the current gait in metres per second.</summary>
        public float MoveSpeed;

        /// <summary>Whether a jump is asked for this step; taken only from the ground.</summary>
        public bool Jump;

        /// <summary>
        /// World-space displacement to sweep in on top of the walk — a correction being paid back, a frame
        /// of root motion — which can be stopped by a wall like any other movement.
        /// </summary>
        public Vector3 PendingDisplacement;
    }
}
