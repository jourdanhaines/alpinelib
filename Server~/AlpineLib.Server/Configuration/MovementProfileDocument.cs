using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Server.Configuration {
    /// <summary>
    /// JSON mirror of one entry's movement envelope: the six gait speeds, the vertical constants and the
    /// collision capsule the shared motor runs by.
    /// </summary>
    /// <remarks>
    /// These numbers are the server's copy of what the Unity locomotion asset says. They are what the
    /// authoritative motor steps with and what the validator judges an owner-authoritative client
    /// against, so a divergence between this export and the client's asset shows up as rubber-banding
    /// rather than as an error — which is exactly why the editor exports them instead of the server
    /// guessing.
    /// </remarks>
    public sealed class MovementProfileDocument {
        public string DisplayName { get; set; } = string.Empty;

        public float WalkSlowSpeed { get; set; } = 1.0f;

        public float WalkSpeed { get; set; } = 2.0f;

        public float JogSpeed { get; set; } = 3.5f;

        public float SprintSpeed { get; set; } = 5.5f;

        public float CrouchSpeed { get; set; } = 1.2f;

        public float CrouchFastSpeed { get; set; } = 2.2f;

        public float Gravity { get; set; } = -20f;

        public float JumpVelocity { get; set; } = 6f;

        public float AirAcceleration { get; set; } = 16f;

        public float AirDrag { get; set; }

        public float CapsuleRadius { get; set; } = 0.35f;

        public float CapsuleHeight { get; set; } = 1.1f;

        public float StepOffset { get; set; } = 0.3f;

        public float SlopeLimitDegrees { get; set; } = 50f;

        /// <summary>Maps this document onto the shared profile the motor and validator read.</summary>
        public MovementProfile ToProfile() {
            return new MovementProfile {
                DisplayName = DisplayName,
                WalkSlowSpeed = WalkSlowSpeed,
                WalkSpeed = WalkSpeed,
                JogSpeed = JogSpeed,
                SprintSpeed = SprintSpeed,
                CrouchSpeed = CrouchSpeed,
                CrouchFastSpeed = CrouchFastSpeed,
                Gravity = Gravity,
                JumpVelocity = JumpVelocity,
                AirAcceleration = AirAcceleration,
                AirDrag = AirDrag,
                CapsuleRadius = CapsuleRadius,
                CapsuleHeight = CapsuleHeight,
                StepOffset = StepOffset,
                SlopeLimitDegrees = SlopeLimitDegrees
            };
        }
    }
}
