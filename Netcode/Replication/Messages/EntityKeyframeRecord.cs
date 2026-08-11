using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication.Messages {
    /// <summary>
    /// One entity's line in a <see cref="SnapshotKeyframe"/>: everything a client would have learned from
    /// the spawn message, plus the current state.
    /// </summary>
    /// <remarks>
    /// This is what makes a keyframe self-sufficient. A rejoining client missed every spawn broadcast
    /// that happened while it was away, so a keyframe of bare id-and-state would describe entities it has
    /// no way to instantiate. Carrying the prefab, the owner, the authority mode and the kind means one
    /// reliable message can rebuild the world from nothing — including the movers, which a rejoining
    /// client must be able to tell apart from pawns before it wires anything to them.
    /// </remarks>
    public struct EntityKeyframeRecord : INetMessage {
        /// <summary>Creates a record describing one pawn in full.</summary>
        public EntityKeyframeRecord(uint entityId, ushort prefabId, int ownerPeerId, AuthorityMode authority, in PawnState state)
            : this(entityId, prefabId, ownerPeerId, authority, EntityKind.Pawn, 0, in state) { }

        /// <summary>Creates a record describing one entity of any kind in full.</summary>
        public EntityKeyframeRecord(
            uint entityId,
            ushort prefabId,
            int ownerPeerId,
            AuthorityMode authority,
            EntityKind kind,
            ushort auxId,
            in PawnState state) {
            EntityId = entityId;
            PrefabId = prefabId;
            OwnerPeerId = ownerPeerId;
            Authority = authority;
            Kind = kind;
            AuxId = auxId;
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

        /// <summary>What sort of thing it is, and therefore what the spawner builds around it.</summary>
        public EntityKind Kind { get; set; }

        /// <summary>Kind-specific identity: a mover's scene-authored mover id, zero for a pawn.</summary>
        public ushort AuxId { get; set; }

        /// <summary>Its state as of the keyframe's tick.</summary>
        public PawnState State { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteUInt(EntityId);
            writer.WriteUShort(PrefabId);
            writer.WriteInt(OwnerPeerId);
            writer.WriteByte((byte)Authority);
            writer.WriteByte((byte)Kind);
            writer.WriteUShort(AuxId);
            writer.WriteMessage(State);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            EntityId = reader.ReadUInt();
            PrefabId = reader.ReadUShort();
            OwnerPeerId = reader.ReadInt();
            Authority = (AuthorityMode)reader.ReadByte();
            Kind = (EntityKind)reader.ReadByte();
            AuxId = reader.ReadUShort();
            State = reader.ReadMessage<PawnState>();
        }
    }
}
