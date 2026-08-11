namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// What sort of thing a replicated entity is, so a client can tell a pawn from a platform before it
    /// decides what to attach to it.
    /// </summary>
    /// <remarks>
    /// The kind rides on the spawn and on every keyframe record, next to the authority mode and for the
    /// same reason: it decides which components the spawner builds. A pawn gets possession, a controller
    /// and — when owned — a prediction buffer; a mover gets none of those and is driven from the shared
    /// path instead. Guessing wrong once at spawn leaves an entity permanently mis-wired, which is not
    /// something a later snapshot can repair.
    ///
    /// Values are a wire contract. Append new kinds; never renumber.
    /// </remarks>
    public enum EntityKind : byte {
        /// <summary>A character: input-driven, possessable, predicted when owned. The default.</summary>
        Pawn = 0,

        /// <summary>A moving platform, posed from its scene-authored path rather than from input.</summary>
        Mover = 1
    }
}
