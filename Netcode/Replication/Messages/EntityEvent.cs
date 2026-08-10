using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication.Messages {
    /// <summary>
    /// A discrete thing that happened to an entity — a jump landing, an emote, a blink — that state
    /// replication would smear away rather than reproduce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Travels in both directions. A client raises one for its own pawn, and the server broadcasts it to
    /// the whole session <em>including the client that sent it</em>. The echo is deliberate: it means
    /// every peer, sender included, sees session events in one order that the server chose, instead of
    /// each client having its own slightly different history.
    /// </para>
    /// <para>
    /// <see cref="Sequence"/> is what makes the echo harmless. The sender stamps each event it raises,
    /// plays it locally at once, and holds the stamp in a pending set; when its own event comes back it
    /// recognises the stamp and drops it rather than playing the emote twice. Server-originated events
    /// carry sequence zero, which no client ever mints, so they are never mistaken for an echo.
    /// </para>
    /// </remarks>
    public struct EntityEvent : INetMessage {
        /// <summary>Sequence value reserved for server-originated events, which are never echoes.</summary>
        public const uint ServerSequence = 0u;

        /// <summary>Creates an event on one entity.</summary>
        public EntityEvent(uint entityId, byte eventId, byte argument, uint sequence) {
            EntityId = entityId;
            EventId = eventId;
            Argument = argument;
            Sequence = sequence;
        }

        /// <summary>The entity the event happened to.</summary>
        public uint EntityId { get; set; }

        /// <summary>Game-defined event kind. The netcode layer never interprets it.</summary>
        public byte EventId { get; set; }

        /// <summary>One byte of game-defined payload — an emote index, a surface type.</summary>
        public byte Argument { get; set; }

        /// <summary>The originating client's stamp, or <see cref="ServerSequence"/> from the server.</summary>
        public uint Sequence { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteUInt(EntityId);
            writer.WriteByte(EventId);
            writer.WriteByte(Argument);
            writer.WriteUInt(Sequence);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            EntityId = reader.ReadUInt();
            EventId = reader.ReadByte();
            Argument = reader.ReadByte();
            Sequence = reader.ReadUInt();
        }
    }
}
