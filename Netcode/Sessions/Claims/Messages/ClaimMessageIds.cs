namespace AlpineLib.Netcode.Sessions.Claims.Messages {
    /// <summary>
    /// The wire ids of the claim messages, taken from the free tail of the session band.
    /// </summary>
    /// <remarks>
    /// Claims ride in the session block (64-127) because a claim is a session-scoped right rather than
    /// anything the replicated world knows about: the holder is a peer id on a roster, not an entity.
    /// Like every other id in this protocol these are a compatibility contract with shipped builds — an
    /// id may be retired but never repurposed, because an older client would decode a different payload
    /// through the same handler and corrupt itself silently.
    /// </remarks>
    public static class ClaimMessageIds {
        /// <summary>Client to server: I would like to hold this slot. ReliableOrdered.</summary>
        public const ushort ClaimRequest = 84;

        /// <summary>Client to server: I am done with this slot. ReliableOrdered.</summary>
        public const ushort ClaimRelease = 85;

        /// <summary>Server to session: who holds this slot now, or that it is free. ReliableOrdered.</summary>
        public const ushort ClaimChanged = 86;
    }
}
