using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication.Messages {
    /// <summary>
    /// Server to the owning client: this is where your pawn actually is, and this is the input of yours
    /// that it accounts for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="AcknowledgedInputTick"/> is the whole reason this message exists instead of the owner
    /// just reading itself out of the snapshot stream. A snapshot says where a pawn is <em>now</em>; the
    /// owner is already several ticks ahead of that on prediction, so adopting it directly would drag
    /// them backwards every time one arrived. Stamping the input tick lets
    /// <see cref="PredictionBuffer.Reconcile"/> rewind to exactly that point and replay forward, so the
    /// owner only ever moves when the server genuinely disagreed with them.
    /// </para>
    /// <para>
    /// In <see cref="AuthorityMode.OwnerClient"/> the same message carries a validator's verdict, where
    /// the acknowledged tick is the client tick of the update that was clamped or rejected.
    /// </para>
    /// </remarks>
    public struct AuthorityCorrection : INetMessage {
        /// <summary>Creates a correction for one entity.</summary>
        public AuthorityCorrection(uint entityId, uint serverTick, uint acknowledgedInputTick, in PawnState state) {
            EntityId = entityId;
            ServerTick = serverTick;
            AcknowledgedInputTick = acknowledgedInputTick;
            State = state;
        }

        /// <summary>The pawn being corrected.</summary>
        public uint EntityId { get; set; }

        /// <summary>The authoritative tick the state is from.</summary>
        public uint ServerTick { get; set; }

        /// <summary>The owner's own input tick this state already includes.</summary>
        public uint AcknowledgedInputTick { get; set; }

        /// <summary>The authoritative state at that point.</summary>
        public PawnState State { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteUInt(EntityId);
            writer.WriteUInt(ServerTick);
            writer.WriteUInt(AcknowledgedInputTick);
            writer.WriteMessage(State);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            EntityId = reader.ReadUInt();
            ServerTick = reader.ReadUInt();
            AcknowledgedInputTick = reader.ReadUInt();
            State = reader.ReadMessage<PawnState>();
        }
    }
}
