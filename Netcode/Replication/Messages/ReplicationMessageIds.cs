namespace AlpineLib.Netcode.Replication.Messages {
    /// <summary>
    /// The replication block of the message id map, 128-191.
    /// </summary>
    /// <remarks>
    /// Ids are a permanent wire contract. A retired message's id stays retired rather than being handed
    /// to something else — a client one build behind would decode the new message with the old reader and
    /// corrupt itself silently, which is the worst failure this protocol can have.
    /// </remarks>
    public static class ReplicationMessageIds {
        /// <summary>Server to client: an entity now exists. ReliableOrdered.</summary>
        public const ushort SpawnEntity = 128;

        /// <summary>Server to client: an entity is gone. ReliableOrdered.</summary>
        public const ushort DespawnEntity = 129;

        /// <summary>Server to client: the entities that changed, at 15 Hz. UnreliableSequenced.</summary>
        public const ushort Snapshot = 130;

        /// <summary>Server to client: every entity in full, at 1 Hz and on join. ReliableOrdered.</summary>
        public const ushort SnapshotKeyframe = 131;

        /// <summary>Client to server: the owner's own simulated state, in OwnerClient mode.</summary>
        public const ushort OwnerPawnUpdate = 132;

        /// <summary>Client to server: the owner's intent, in the default server-authoritative mode.</summary>
        public const ushort InputCommand = 133;

        /// <summary>Either direction: a discrete, non-state event on an entity. ReliableOrdered.</summary>
        public const ushort EntityEvent = 134;

        /// <summary>Server to owner: the authoritative state, stamped with the input it accounts for.</summary>
        public const ushort AuthorityCorrection = 135;
    }
}
