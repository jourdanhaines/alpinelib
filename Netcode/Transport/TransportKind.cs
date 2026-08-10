namespace AlpineLib.Netcode.Transport {
    /// <summary>
    /// Which transport a <see cref="NetEndpoint"/> addresses.
    /// </summary>
    /// <remarks>
    /// <see cref="Steam"/> is a seam: v1 ships only the direct UDP transport, and a future
    /// <c>SteamRelayTransport</c> would consume endpoints carrying a Steam identity instead of a
    /// host and port.
    /// </remarks>
    public enum TransportKind : byte {
        /// <summary>Direct UDP to a host and port.</summary>
        Direct = 0,

        /// <summary>Steam Datagram Relay to a Steam identity.</summary>
        Steam = 1
    }
}
