namespace AlpineLib.Netcode.Messages {
    /// <summary>
    /// The wire ids of the connection-level messages: ids 0-2 of the protocol id map.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the only messages the facades themselves speak. Everything else — session, replication,
    /// chat — is registered by the layer that owns it. As with every other band, an id here may be
    /// retired but never repurposed: a shipped build that still speaks it would decode a different
    /// payload into the same handler.
    /// </para>
    /// <para>
    /// Only 0-2 are reserved; 3-63 are free.
    /// <see cref="AlpineLib.Netcode.Protocol.MessageIdBudget"/> is the single authority on the whole
    /// map — check an id there rather than inferring a range from this class.
    /// </para>
    /// </remarks>
    public static class CoreMessageIds {
        /// <summary>Server to client: the authoritative tick counter, broadcast once a second.</summary>
        public const ushort ClockSync = 1;

        /// <summary>Server to client: why this connection is about to be closed.</summary>
        public const ushort DisconnectNotice = 2;
    }
}
