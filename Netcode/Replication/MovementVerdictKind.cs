namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// What the server decided about one reported move in <see cref="AuthorityMode.OwnerClient"/>.
    /// </summary>
    /// <remarks>
    /// The middle value is the point of the enum. A hard accept/reject split makes every hitch — a
    /// dropped frame, a slope assist, a quantization edge — look like cheating, so a move that is only a
    /// little too fast is pulled back to the legal distance and allowed through, and only a move that is
    /// wildly impossible is thrown away entirely.
    /// </remarks>
    public enum MovementVerdictKind : byte {
        /// <summary>Within the gait's envelope. Take the client's state as reported.</summary>
        Accepted = 0,

        /// <summary>Slightly over. Pull the move back to the legal distance and correct the client.</summary>
        Clamped = 1,

        /// <summary>Impossible. Discard it, keep the previous state and correct the client.</summary>
        Rejected = 2
    }
}
