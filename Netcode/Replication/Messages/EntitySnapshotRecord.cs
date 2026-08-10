using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication.Messages {
    /// <summary>
    /// One entity's line in a <see cref="Snapshot"/>: an id and its whole state.
    /// </summary>
    /// <remarks>
    /// Full states rather than field deltas. Snapshots ride an unreliable channel where any single packet
    /// may vanish, and a delta is only decodable against the exact baseline it was built from — so a
    /// delta scheme has to carry acknowledgement and per-client baselines to be correct at all. At the
    /// handful of pawns a session holds, sending the whole state costs less than that machinery.
    /// </remarks>
    public struct EntitySnapshotRecord : INetMessage {
        /// <summary>Creates a record for one entity.</summary>
        public EntitySnapshotRecord(uint entityId, in PawnState state) {
            EntityId = entityId;
            State = state;
        }

        /// <summary>Which entity this line describes.</summary>
        public uint EntityId { get; set; }

        /// <summary>Its state as of the snapshot's tick.</summary>
        public PawnState State { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteUInt(EntityId);
            writer.WriteMessage(State);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            EntityId = reader.ReadUInt();
            State = reader.ReadMessage<PawnState>();
        }
    }
}
