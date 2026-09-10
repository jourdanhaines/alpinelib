using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication.StateChannel {
    /// <summary>
    /// One subject's line in a <see cref="StateChannelEnvelope{TState}"/>: which subject, the tick its
    /// state is from, what kind of line it is, and the state itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The per-record tick is what separates this from an entity snapshot record, which takes its tick
    /// from the envelope. A state channel carries things the server steps at its own pace — a train that
    /// only integrates while someone is driving it, a door that changes twice a minute — so the envelope
    /// tick says when the packet was sent while the record tick says when the state was true. The client
    /// needs the latter to drop a keyframe that restates something a snapshot has already superseded.
    /// </para>
    /// <para>
    /// <see cref="Flags"/> is a wire slot before it is a feature. Nothing in this protocol carries a
    /// format version to negotiate a change with, so a byte added to the record later would break every
    /// shipped build that already speaks it; one byte paid once is what makes it possible to say
    /// anything new about a line at all. Only <see cref="RetiredFlag"/> is defined today — the other
    /// seven bits are reserved and are written as zero.
    /// </para>
    /// </remarks>
    public struct StateChannelRecord<TState> : INetMessage where TState : struct, INetMessage {
        /// <summary>The subject is gone; the state that follows is filler and means nothing.</summary>
        public const byte RetiredFlag = 1;

        /// <summary>Creates a record carrying a subject's live state.</summary>
        public StateChannelRecord(ushort id, uint tick, in TState state) : this(id, tick, state, 0) { }

        /// <summary>Creates a record with an explicit flag set.</summary>
        public StateChannelRecord(ushort id, uint tick, in TState state, byte flags) {
            Id = id;
            Tick = tick;
            Flags = flags;
            State = state;
        }

        /// <summary>The channel-local id of the subject this line describes.</summary>
        public ushort Id { get; set; }

        /// <summary>The tick the state was authoritative at, not the tick it was sent at.</summary>
        public uint Tick { get; set; }

        /// <summary>What kind of line this is. Bit 0 is <see cref="RetiredFlag"/>; the rest are reserved.</summary>
        public byte Flags { get; set; }

        /// <summary>The subject's state, in whatever shape the game defined.</summary>
        public TState State { get; set; }

        /// <summary>True when this line retires its subject instead of restating it.</summary>
        public bool IsRetired => (Flags & RetiredFlag) != 0;

        /// <summary>Builds the line that tells a client to forget a subject.</summary>
        /// <remarks>
        /// The state is written out in full even though it means nothing, so every record on the wire is
        /// the same shape and a reader never has to branch on a flag it may not understand.
        /// </remarks>
        public static StateChannelRecord<TState> Retired(ushort id, uint tick) {
            return new StateChannelRecord<TState>(id, tick, default, RetiredFlag);
        }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteUShort(Id);
            writer.WriteUInt(Tick);
            writer.WriteByte(Flags);
            writer.WriteMessage(State);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Id = reader.ReadUShort();
            Tick = reader.ReadUInt();
            Flags = reader.ReadByte();
            State = reader.ReadMessage<TState>();
        }
    }
}
