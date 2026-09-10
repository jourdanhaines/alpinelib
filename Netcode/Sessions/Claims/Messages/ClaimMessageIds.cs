namespace AlpineLib.Netcode.Sessions.Claims.Messages {
    /// <summary>
    /// The wire ids of the claim messages, taken from the free tail of the session band.
    /// </summary>
    /// <remarks>
    /// Claims own ids 84-87 — the claim band — because a claim is a session-scoped right rather than
    /// anything the replicated world knows about: the holder is a peer id on a roster, not an entity.
    /// <c>MessageIdBudget</c> is the authority on where that band starts and ends; the session band
    /// proper is 64-83 and 88-119 is free.
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

        /// <summary>Server to one requester: that request is refused. ReliableOrdered.</summary>
        public const ushort ClaimDenied = 87;
    }
}
