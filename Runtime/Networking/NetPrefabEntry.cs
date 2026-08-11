using System;
using AlpineLib.Netcode.Protocol;
using UnityEngine;

namespace AlpineLib.Networking {
    /// <summary>
    /// One row of a <see cref="NetPrefabRegistry"/>: the prefab a spawn instantiates, and the movement
    /// tuning the shared motor steps it with.
    /// </summary>
    /// <remarks>
    /// There is a single prefab field rather than an owned/proxy pair. Possession is the seam that
    /// separates a locally driven pawn from a remote one — the same prefab is instantiated either way
    /// and a different controller takes it — so a second prefab would only be a second thing to keep in
    /// sync.
    ///
    /// The gait speeds are authored here rather than read from the actor's stat sheet because the
    /// dedicated server has no stat sheets: these numbers are what the server simulates with, and the
    /// stat assets must be kept to match (the session config validator checks that parity).
    /// </remarks>
    [Serializable]
    public class NetPrefabEntry {
        [Tooltip("Editor-facing label. Never travels on the wire; the row's index is the prefab id.")]
        public string displayName;

        [Tooltip("Prefab instantiated for every spawn of this prefab id, owned or remote.")]
        public GameObject prefab;

        [Header("Gait Speeds (metres/second)")]
        [Tooltip("Slowest forward gait.")]
        public float walkSlowSpeed = 1.0f;
        [Tooltip("Standard walk.")]
        public float walkSpeed = 2.0f;
        [Tooltip("Jog.")]
        public float jogSpeed = 3.5f;
        [Tooltip("Sprint.")]
        public float sprintSpeed = 5.5f;
        [Tooltip("Crouched movement.")]
        public float crouchSpeed = 1.2f;
        [Tooltip("Fast crouched movement.")]
        public float crouchFastSpeed = 2.2f;

        [Header("Vertical")]
        [Tooltip("Downward acceleration in metres per second squared. Negative points at the floor.")]
        public float gravity = -20f;
        [Tooltip("Upward speed in metres per second applied on the tick a jump starts.")]
        public float jumpVelocity = 6f;

        [Header("Air")]
        [Tooltip("Horizontal steering acceleration while airborne, in metres per second squared. Must match the actor's air acceleration or every jump diverges.")]
        public float airAcceleration = 16f;
        [Tooltip("Exponential decay per second applied to horizontal air velocity while no input is held. Must match the actor's air drag.")]
        public float airDrag;

        /// <summary>Builds the shared movement profile this row describes.</summary>
        public MovementProfile ToProfile() {
            return new MovementProfile {
                DisplayName = displayName ?? string.Empty,
                WalkSlowSpeed = walkSlowSpeed,
                WalkSpeed = walkSpeed,
                JogSpeed = jogSpeed,
                SprintSpeed = sprintSpeed,
                CrouchSpeed = crouchSpeed,
                CrouchFastSpeed = crouchFastSpeed,
                Gravity = gravity,
                JumpVelocity = jumpVelocity,
                AirAcceleration = airAcceleration,
                AirDrag = airDrag
            };
        }
    }
}
