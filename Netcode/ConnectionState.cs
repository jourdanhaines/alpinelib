namespace AlpineLib.Netcode {
    /// <summary>
    /// Where a <see cref="NetClient"/> sits in its connection lifecycle.
    /// </summary>
    /// <remarks>
    /// This is the transport-level state only — it says a socket link exists, not that the player has
    /// been authenticated or attached to a session. Those live above the facade, in the session layer,
    /// precisely so a reconnect can reuse this same ladder without the session having to guess.
    /// </remarks>
    public enum ConnectionState : byte {
        /// <summary>No link and no attempt in flight. The starting and the terminal state.</summary>
        Disconnected = 0,

        /// <summary>A dial is in flight; it will resolve to connected or disconnected during a poll.</summary>
        Connecting = 1,

        /// <summary>The link is up and messages may be sent.</summary>
        Connected = 2,

        /// <summary>
        /// A graceful close was asked for locally. The state falls back to
        /// <see cref="Disconnected"/> once the transport confirms it during a poll.
        /// </summary>
        Disconnecting = 3
    }
}
