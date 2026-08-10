namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// Decides when a session tears itself down.
    /// </summary>
    /// <remarks>Wire byte; append only.</remarks>
    public enum SessionLifetimeMode : byte {
        /// <summary>Session exists only for one match and closes once results are done.</summary>
        MatchScoped = 0,

        /// <summary>Session lives as long as the lobby has members (Penguin igloos).</summary>
        LobbyScoped = 1,

        /// <summary>Session survives an empty roster until an operator closes it.</summary>
        LongLived = 2
    }
}
