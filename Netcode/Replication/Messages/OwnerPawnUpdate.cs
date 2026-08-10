using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication.Messages {
    /// <summary>
    /// Client to server, thirty times a second, in <see cref="AuthorityMode.OwnerClient"/> only: where
    /// the owner says its pawn now is.
    /// </summary>
    /// <remarks>
    /// The opt-out path. It exists for pawns whose movement the shared motor cannot reproduce — anything
    /// leaning on engine physics the server does not have — and it trades away the guarantee the default
    /// mode provides: everything here is a claim, and <see cref="MovementValidator"/> is the only thing
    /// standing between that claim and the other players' screens.
    /// </remarks>
    public struct OwnerPawnUpdate : INetMessage {
        /// <summary>Creates an update for one entity.</summary>
        public OwnerPawnUpdate(uint entityId, uint clientTick, in PawnState state) {
            EntityId = entityId;
            ClientTick = clientTick;
            State = state;
        }

        /// <summary>The pawn being reported. The server checks the sender actually owns it.</summary>
        public uint EntityId { get; set; }

        /// <summary>The sender's own tick counter, echoed back on any correction.</summary>
        public uint ClientTick { get; set; }

        /// <summary>The state the owner claims.</summary>
        public PawnState State { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteUInt(EntityId);
            writer.WriteUInt(ClientTick);
            writer.WriteMessage(State);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            EntityId = reader.ReadUInt();
            ClientTick = reader.ReadUInt();
            State = reader.ReadMessage<PawnState>();
        }
    }
}
