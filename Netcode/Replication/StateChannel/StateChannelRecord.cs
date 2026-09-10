using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication.StateChannel {
    /// <summary>
    /// One subject's line in a <see cref="StateChannelEnvelope{TState}"/>: which subject, the tick its
    /// state is from, and the state itself.
    /// </summary>
    /// <remarks>
    /// The per-record tick is what separates this from an entity snapshot record, which takes its tick
    /// from the envelope. A state channel carries things the server steps at its own pace — a train that
    /// only integrates while someone is driving it, a door that changes twice a minute — so the envelope
    /// tick says when the packet was sent while the record tick says when the state was true. The client
    /// needs the latter to drop a keyframe that restates something a snapshot has already superseded.
    /// </remarks>
    public struct StateChannelRecord<TState> : INetMessage where TState : struct, INetMessage {
        /// <summary>Creates a record for one subject.</summary>
        public StateChannelRecord(ushort id, uint tick, in TState state) {
            Id = id;
            Tick = tick;
            State = state;
        }

        /// <summary>The channel-local id of the subject this line describes.</summary>
        public ushort Id { get; set; }

        /// <summary>The tick the state was authoritative at, not the tick it was sent at.</summary>
        public uint Tick { get; set; }

        /// <summary>The subject's state, in whatever shape the game defined.</summary>
        public TState State { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteUShort(Id);
            writer.WriteUInt(Tick);
            writer.WriteMessage(State);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Id = reader.ReadUShort();
            Tick = reader.ReadUInt();
            State = reader.ReadMessage<TState>();
        }
    }
}
