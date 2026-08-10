namespace AlpineLib.Netcode.Transport {
    /// <summary>
    /// The delivery guarantee a caller asks the transport for, expressed in terms the game layer cares
    /// about rather than in the underlying library's vocabulary.
    /// </summary>
    /// <remarks>
    /// Each class also picks the channel the payload rides on: everything gameplay uses channel 0, chat
    /// uses channel 1. Separating chat onto its own channel means a burst of chat backlog can never
    /// head-of-line block the reliable gameplay stream (or the reverse), which is the whole reason
    /// <see cref="ReliableChat"/> exists as a distinct class instead of reusing
    /// <see cref="ReliableOrdered"/>.
    /// </remarks>
    public enum DeliveryClass : byte {
        /// <summary>Gameplay traffic that must arrive, in order. Channel 0.</summary>
        ReliableOrdered = 0,

        /// <summary>Chat traffic that must arrive, in order, on its own channel. Channel 1.</summary>
        ReliableChat = 1,

        /// <summary>
        /// Snapshots and other state that is worthless once superseded: drops are fine, but a late
        /// packet must never overwrite a newer one. Channel 0.
        /// </summary>
        UnreliableSequenced = 2,

        /// <summary>Fire and forget, no ordering, no retransmission. Channel 0.</summary>
        Unreliable = 3
    }
}
