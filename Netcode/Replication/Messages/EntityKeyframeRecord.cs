using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication.Messages {
    /// <summary>
    /// One entity's line in a <see cref="SnapshotKeyframe"/>: everything a client would have learned from
    /// the spawn message, plus the current state.
    /// </summary>
    /// <remarks>
    /// This is what makes a keyframe self-sufficient. A rejoining client missed every spawn broadcast
    /// that happened while it was away, so a keyframe of bare id-and-state would describe entities it has
    /// no way to instantiate. Carrying the prefab, the owner and the authority mode means one reliable
    /// message can rebuild the world from nothing.
    /// </remarks>
    public struct EntityKeyframeRecord : INetMessage {
        /// <summary>Creates a record describing one entity in full.</summary>
        public EntityKeyframeRecord(uint entityId, ushort prefabId, int ownerPeerId, AuthorityMode authority, in PawnState state) {
            EntityId = entityId;
            PrefabId = prefabId;
            OwnerPeerId = ownerPeerId;
            Authority = authority;
            State = state;
        }

        /// <summary>Which entity this line describes.</summary>
        public uint EntityId { get; set; }

        /// <summary>Index into the prefab registry.</summary>
        public ushort PrefabId { get; set; }

        /// <summary>Peer it belongs to, or a negative value when nobody owns it.</summary>
        public int OwnerPeerId { get; set; }

        /// <summary>Who simulates it.</summary>
        public AuthorityMode Authority { get; set; }

        /// <summary>Its state as of the keyframe's tick.</summary>
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
