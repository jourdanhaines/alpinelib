namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// Why a client's session ended, or why an attach attempt was refused.
    /// </summary>
    /// <remarks>
    /// Wire byte; append only. Doubles as the <c>JoinSessionDenied</c> reason code so the client has
    /// one enum to map to user-facing copy.
    /// </remarks>
    public enum SessionEndReason : byte {
        /// <summary>The owner closed the session.</summary>
        HostClosed = 0,

        /// <summary>The local player was kicked.</summary>
        Kicked = 1,

        /// <summary>Transport dropped and no rejoin was possible.</summary>
        TransportLost = 2,

        /// <summary>Authentication was refused by the validator.</summary>
        AuthRejected = 3,

        /// <summary>Client and server disagree on <c>NetProtocol.Version</c>.</summary>
        VersionMismatch = 4,

        /// <summary>Session is at its member cap.</summary>
        Full = 5,

        /// <summary>Join arrived mid-match and the profile forbids it.</summary>
        JoinRejectedMatchInProgress = 6,

        /// <summary>The supplied join code matched no live session.</summary>
        SessionNotFound = 7,

        /// <summary>The connection is already attached to a session; one session per connection.</summary>
        AlreadyInSession = 8
    }
}
