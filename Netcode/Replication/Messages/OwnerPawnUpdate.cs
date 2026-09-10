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
        /// <summary>Set in <see cref="Flags"/> when this is the first update after a gap in reporting.</summary>
        /// <remarks>
        /// The owner side stops sending outright whenever it cannot say anything truthful about where its
        /// pawn is — see <c>NetActorSync.TryCaptureState</c> — and it places an owned pawn once, itself,
        /// at spawn. Either way the pose it comes back with is unrelated to the one the server is still
        /// holding, and measuring the gap against a walking gait rejects the resumption and snaps the
        /// player back the whole distance their game carried them meanwhile. This bit is what lets the
        /// server tell that apart from a teleport claimed mid-stride; see
        /// <c>ServerReplication.HandleOwnerPawnUpdate</c> for the trust it does and does not buy.
        /// </remarks>
        public const byte ResyncFlag = 1 << 0;

        /// <summary>Creates an update for one entity.</summary>
        public OwnerPawnUpdate(uint entityId, uint clientTick, in PawnState state)
            : this(entityId, clientTick, 0, in state) {
        }

        /// <summary>Creates an update for one entity, carrying the given <see cref="Flags"/>.</summary>
        public OwnerPawnUpdate(uint entityId, uint clientTick, byte flags, in PawnState state) {
            EntityId = entityId;
            ClientTick = clientTick;
            Flags = flags;
            State = state;
        }

        /// <summary>The pawn being reported. The server checks the sender actually owns it.</summary>
        public uint EntityId { get; set; }

        /// <summary>The sender's own tick counter, echoed back on any correction.</summary>
        public uint ClientTick { get; set; }

        /// <summary>
        /// What this update is beyond a pose: bit 0 is <see cref="ResyncFlag"/>, the rest are reserved.
        /// </summary>
        /// <remarks>
        /// A byte rather than a bool because it costs the same on the wire and the next thing an owner
        /// needs to say about its own update — a scripted teleport, a ragdoll hand-off — is another bit
        /// here rather than another message and another protocol bump.
        /// </remarks>
        public byte Flags { get; set; }

        /// <summary>Whether <see cref="ResyncFlag"/> is set; see it for what the server makes of it.</summary>
        public bool IsResync => (Flags & ResyncFlag) != 0;

        /// <summary>The state the owner claims.</summary>
        public PawnState State { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteUInt(EntityId);
            writer.WriteUInt(ClientTick);
            writer.WriteByte(Flags);
            writer.WriteMessage(State);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            EntityId = reader.ReadUInt();
            ClientTick = reader.ReadUInt();
            Flags = reader.ReadByte();
            State = reader.ReadMessage<PawnState>();
        }
    }
}
