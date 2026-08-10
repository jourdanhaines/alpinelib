using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication.Messages {
    /// <summary>
    /// Client to server, thirty times a second: what the owner wants their pawn to do this tick.
    /// </summary>
    /// <remarks>
    /// The whole of the default authority model rests on this message. The client never says where it is
    /// — only what it is trying to do — so there is no position for it to lie about, and the fastest a
    /// cheat can make a pawn go is the fastest the server's own motor will carry it.
    /// </remarks>
    public struct InputCommand : INetMessage {
        /// <summary>Creates a command for one entity.</summary>
        public InputCommand(uint entityId, in PawnInput input) {
            EntityId = entityId;
            Input = input;
        }

        /// <summary>The pawn this input is for. The server checks the sender actually owns it.</summary>
        public uint EntityId { get; set; }

        /// <summary>The tick-stamped intent.</summary>
        public PawnInput Input { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteUInt(EntityId);
            writer.WriteMessage(Input);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            EntityId = reader.ReadUInt();
            Input = reader.ReadMessage<PawnInput>();
        }
    }
}
