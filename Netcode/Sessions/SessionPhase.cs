namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// The state machine a session walks through. A match is a phase plus a scene change inside the
    /// same session — the peer set never moves between sessions.
    /// </summary>
    /// <remarks>
    /// Wire byte; append only. Normal cycle is
    /// Lobby -&gt; MatchLoading -&gt; MatchActive -&gt; MatchResults -&gt; Lobby.
    /// </remarks>
    public enum SessionPhase : byte {
        /// <summary>Members are in the igloo; the owner may launch a match.</summary>
        Lobby = 0,

        /// <summary>Match scene is loading on every participant; the ready barrier is open.</summary>
        MatchLoading = 1,

        /// <summary>Match is simulating.</summary>
        MatchActive = 2,

        /// <summary>Match ended; results are held before returning to the lobby.</summary>
        MatchResults = 3,

        /// <summary>Session is shutting down; no further joins are accepted.</summary>
        Closing = 4
    }
}
