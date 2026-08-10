using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication.Messages {
    /// <summary>
    /// Server to client: an entity is gone. Sent reliably, because a client that misses a despawn is left
    /// with a pawn frozen in the world forever.
    /// </summary>
    public struct DespawnEntity : INetMessage {
        /// <summary>Creates the despawn broadcast for one entity.</summary>
        public DespawnEntity(uint entityId) {
            EntityId = entityId;
        }

        /// <summary>The entity that no longer exists.</summary>
        public uint EntityId { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteUInt(EntityId);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            EntityId = reader.ReadUInt();
        }
    }
}
