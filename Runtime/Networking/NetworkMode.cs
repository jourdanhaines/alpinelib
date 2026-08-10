namespace AlpineLib.Networking {
    /// <summary>
    /// What the local process is doing on the network right now.
    /// </summary>
    /// <remarks>
    /// <see cref="Offline"/> is not an error state and not a fallback: a developer opening the game
    /// scene directly, and a game shipped without a session config, both run in it forever. Every
    /// networking service therefore treats it as a first-class mode where every accessor is null and
    /// every call is a no-op, rather than something to guard against at each call site.
    /// </remarks>
    public enum NetworkMode : byte {
        /// <summary>No transport, no client, no server. Single-player scene execution.</summary>
        Offline = 0,

        /// <summary>A client connected — or connecting — to a remote server.</summary>
        Client = 1,

        /// <summary>A server running in this process, with the local client connected over loopback.</summary>
        ListenServer = 2
    }
}
