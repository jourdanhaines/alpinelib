namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// The six locomotion gaits as they travel on the wire, mirroring alpinelib's engine-side
    /// locomotion states.
    /// </summary>
    /// <remarks>
    /// Packed into bits 0-2 of <c>PawnState.Flags</c>, so only values 0-7 are representable and the
    /// ordering is a compatibility contract. The movement validator keys its per-gait speed caps off
    /// this value, which is why the slowest gait sorts first.
    /// </remarks>
    public enum WireLocomotion : byte {
        /// <summary>Slowest forward gait.</summary>
        WalkSlow = 0,

        /// <summary>Standard walk.</summary>
        Walk = 1,

        /// <summary>Jog.</summary>
        Jog = 2,

        /// <summary>Sprint.</summary>
        Sprint = 3,

        /// <summary>Crouched movement.</summary>
        Crouch = 4,

        /// <summary>Fast crouched movement.</summary>
        CrouchFast = 5
    }
}
