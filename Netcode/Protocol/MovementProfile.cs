using System;

namespace AlpineLib.Netcode.Protocol {
    /// <summary>
    /// Per-pawn-archetype movement envelope: the top speed of each of the six gaits the locomotion system
    /// exposes. Authored in Unity on the prefab registry, exported to the server as JSON, and consumed by
    /// the shared motor and validator — which is why it lives here in engine-free code rather than beside
    /// the ScriptableObject that authors it.
    ///
    /// Gait indices are the wire order (WalkSlow, Walk, Jog, Sprint, Crouch, CrouchFast) and are encoded
    /// in the low three bits of a pawn's flags byte. Index order is therefore a wire contract: reorder it
    /// and every client reads the wrong speed.
    /// </summary>
    public sealed class MovementProfile {
        /// <summary>Number of gaits the locomotion system defines; matches the three-bit wire field.</summary>
        public const int GaitCount = 6;

        /// <summary>Human-readable label, for editor and diagnostics only. Never on the wire.</summary>
        public string DisplayName { get; set; } = string.Empty;

        public float WalkSlowSpeed { get; set; } = 1.0f;

        public float WalkSpeed { get; set; } = 2.0f;

        public float JogSpeed { get; set; } = 3.5f;

        public float SprintSpeed { get; set; } = 5.5f;

        public float CrouchSpeed { get; set; } = 1.2f;

        public float CrouchFastSpeed { get; set; } = 2.2f;

        /// <summary>
        /// Downward acceleration in metres per second squared, as a negative number. It lives on the
        /// profile rather than as a constant because the shared motor has no engine physics to inherit it
        /// from, and both ends of the wire must integrate with the identical value or prediction and
        /// authority drift apart on every airborne tick.
        /// </summary>
        public float Gravity { get; set; } = -20f;

        /// <summary>Upward speed a grounded pawn is given on the tick it jumps, in metres per second.</summary>
        public float JumpVelocity { get; set; } = 6f;

        /// <summary>
        /// Horizontal steering acceleration while airborne, in metres per second squared. Airborne
        /// velocity moves toward the commanded direction at this rate instead of snapping, mirroring the
        /// engine-side actor's air model — the two must integrate identically or every jump diverges.
        /// </summary>
        public float AirAcceleration { get; set; } = 16f;

        /// <summary>
        /// Exponential decay per second applied to horizontal air velocity while no input is held. Zero
        /// carries momentum through the whole arc.
        /// </summary>
        public float AirDrag { get; set; }

        /// <summary>Fastest gait in the profile — the ceiling the validator uses when a gait is unknown.</summary>
        public float MaxSpeed {
            get {
                float fastest = WalkSlowSpeed;
                for (int gaitIndex = 1; gaitIndex < GaitCount; gaitIndex++) {
                    fastest = Math.Max(fastest, GetSpeedForGait(gaitIndex));
                }

                return fastest;
            }
        }

        /// <summary>
        /// Top speed for a gait index taken off the wire. Out-of-range indices resolve to
        /// <see cref="MaxSpeed"/> rather than throwing: a garbage index from a hostile client must be
        /// permissive-but-bounded here and get rejected by the validator, not crash the tick.
        /// </summary>
        public float GetSpeedForGait(int gaitIndex) {
            switch (gaitIndex) {
                case 0: return WalkSlowSpeed;
                case 1: return WalkSpeed;
                case 2: return JogSpeed;
                case 3: return SprintSpeed;
                case 4: return CrouchSpeed;
                case 5: return CrouchFastSpeed;
                default: return MaxSpeed;
            }
        }
    }
}
