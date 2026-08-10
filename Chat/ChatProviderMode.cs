namespace AlpineLib.Chat {
    /// <summary>
    /// Which provider implementation the game should install. Configured, not detected, so a build can
    /// be pointed at a hosted chat service without touching composition code.
    /// </summary>
    public enum ChatProviderMode : byte {
        /// <summary>Chat rides the game's own transport and is served by the game server.</summary>
        BuiltIn = 0,

        /// <summary>Chat is delegated to an external service behind <see cref="IChatProvider"/>.</summary>
        External = 1
    }
}
