namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// How a player proves the identity they claim in <c>AuthRequest</c>.
    /// </summary>
    /// <remarks>
    /// Values travel on the wire as a single byte, so ordering is a compatibility contract: append
    /// only. <see cref="Steam"/> is a design seam — v1 ships no Steamworks dependency, and a future
    /// <c>SteamAuthValidator</c> validates the session ticket server-side.
    /// </remarks>
    public enum AuthMethod : byte {
        /// <summary>No proof required; the claimed identity is trusted as-is.</summary>
        Anonymous = 0,

        /// <summary>Token is a Steam session ticket, validated against the Steamworks Web API.</summary>
        Steam = 1
    }
}
