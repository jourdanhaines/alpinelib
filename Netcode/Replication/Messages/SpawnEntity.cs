using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication.Messages {
    /// <summary>
    /// Server to client: an entity has come into existence, here is everything needed to build it.
    /// </summary>
    /// <remarks>
    /// The authority mode rides on the spawn rather than being inferred, because it decides which
    /// controller the client attaches: an owned pawn under server authority predicts and reconciles, an
    /// owned pawn under owner authority simulates outright, and everything else interpolates. Getting
    /// that wrong once at spawn is not recoverable later.
    /// </remarks>
    public struct SpawnEntity : INetMessage {
        /// <summary>Creates the spawn broadcast for one entity.</summary>
        public SpawnEntity(uint entityId, ushort prefabId, int ownerPeerId, AuthorityMode authority, in PawnState state) {
            EntityId = entityId;
            PrefabId = prefabId;
            OwnerPeerId = ownerPeerId;
            Authority = authority;
            State = state;
        }

        /// <summary>Server-assigned identity for the new entity.</summary>
        public uint EntityId { get; set; }

        /// <summary>Index into the prefab registry.</summary>
        public ushort PrefabId { get; set; }

        /// <summary>Peer this entity belongs to, or a negative value when nobody owns it.</summary>
        public int OwnerPeerId { get; set; }

        /// <summary>Who simulates it.</summary>
        public AuthorityMode Authority { get; set; }

        /// <summary>Where it starts.</summary>
        public PawnState State { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteUInt(EntityId);
            writer.WriteUShort(PrefabId);
            writer.WriteInt(OwnerPeerId);
            writer.WriteByte((byte)Authority);
            writer.WriteMessage(State);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            EntityId = reader.ReadUInt();
            PrefabId = reader.ReadUShort();
            OwnerPeerId = reader.ReadInt();
            Authority = (AuthorityMode)reader.ReadByte();
            State = reader.ReadMessage<PawnState>();
        }
    }
}
