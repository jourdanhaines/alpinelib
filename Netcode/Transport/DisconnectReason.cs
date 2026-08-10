namespace AlpineLib.Netcode.Transport {
    /// <summary>
    /// Why a connection ended, collapsed from the transport library's much longer list into the handful
    /// of cases the layers above actually branch on.
    /// </summary>
    /// <remarks>
    /// The distinction that matters upstream is "did the peer mean to leave" — a graceful leave retires
    /// a session member, while a timeout or transport error leaves a rejoin reservation open per the
    /// session's <c>RejoinPolicy</c>.
    /// </remarks>
    public enum DisconnectReason : byte {
        /// <summary>Either side asked to close the connection and said so on the wire.</summary>
        Graceful = 0,

        /// <summary>The peer stopped answering and the link aged out.</summary>
        Timeout = 1,

        /// <summary>
        /// The connection never came up: the connect key did not match, the server was full, or the
        /// remote refused it outright.
        /// </summary>
        Rejected = 2,

        /// <summary>Socket or routing failure — unreachable host, unresolvable name, dead network.</summary>
        TransportError = 3,

        /// <summary>
        /// Removed by the server for a game-level reason. Never produced by the transport itself: the
        /// session layer sends its own notice and then drops the peer, and reports this reason to its
        /// own listeners.
        /// </summary>
        Kicked = 4
    }
}
