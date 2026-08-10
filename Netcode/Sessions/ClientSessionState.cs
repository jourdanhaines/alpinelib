namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// Where the local client sits in the connect -&gt; authenticate -&gt; attach handshake.
    /// </summary>
    /// <remarks>
    /// Purely client-side bookkeeping; it is never serialised. <see cref="Authenticated"/> is the
    /// front-desk state: the connection is trusted but attached to no session yet.
    /// </remarks>
    public enum ClientSessionState : byte {
        /// <summary>No transport connection and none being attempted.</summary>
        Offline = 0,

        /// <summary>Transport connection in flight.</summary>
        Connecting = 1,

        /// <summary>Connected; <c>AuthRequest</c> sent, waiting on the verdict.</summary>
        Authenticating = 2,

        /// <summary>Authenticated but attached to no session — the server front desk.</summary>
        Authenticated = 3,

        /// <summary>A create or join request is in flight.</summary>
        Joining = 4,

        /// <summary>Attached to a session and receiving its broadcasts.</summary>
        InSession = 5,

        /// <summary>A graceful leave is in flight.</summary>
        Leaving = 6,

        /// <summary>The last attempt failed; inspect the reported <see cref="SessionEndReason"/>.</summary>
        Failed = 7
    }
}
