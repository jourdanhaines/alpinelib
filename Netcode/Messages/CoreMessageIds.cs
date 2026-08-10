namespace AlpineLib.Netcode.Messages {
    /// <summary>
    /// The wire ids of the connection-level messages, band 0-63 of the protocol id map.
    /// </summary>
    /// <remarks>
    /// These are the only messages the facades themselves speak. Everything else — session, replication,
    /// chat — is registered by the layer that owns it. As with every other band, an id here may be
    /// retired but never repurposed: a shipped build that still speaks it would decode a different
    /// payload into the same handler.
    /// </remarks>
    public static class CoreMessageIds {
        /// <summary>Server to client: the authoritative tick counter, broadcast once a second.</summary>
        public const ushort ClockSync = 1;

        /// <summary>Server to client: why this connection is about to be closed.</summary>
        public const ushort DisconnectNotice = 2;
    }
}
