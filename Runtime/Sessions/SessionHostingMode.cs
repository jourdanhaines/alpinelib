namespace AlpineLib.Sessions {
    /// <summary>
    /// Where the server behind a hosted session lives.
    /// </summary>
    /// <remarks>
    /// The three modes differ only in how the host's endpoint is arrived at — everything after the
    /// connect is the same create-session handshake, and nothing downstream of
    /// <c>SessionService</c> can tell them apart. That is deliberate: a bug that only shows up
    /// against a real socket must not be able to hide behind an in-process shortcut.
    /// </remarks>
    public enum SessionHostingMode {
        /// <summary>Ask the server the matchmaking config points at for a session. What a shipped build does.</summary>
        RemoteServer = 0,

        /// <summary>Stand a server up inside this process and dial loopback. Cheapest to iterate on.</summary>
        ListenHost = 1,

        /// <summary>Launch the dedicated server executable beside this build and dial the port it reports.</summary>
        LocalServerProcess = 2
    }
}
