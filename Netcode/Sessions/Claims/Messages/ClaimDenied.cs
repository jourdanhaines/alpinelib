using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Claims.Messages {
    /// <summary>
    /// The server's answer to a request it will not grant: this slot, and nothing more.
    /// </summary>
    /// <remarks>
    /// Sent to the one peer that asked, never broadcast, because a refusal is nobody else's business
    /// and the slot's public state has not moved. It carries no reason on purpose: the honest reasons
    /// are "somebody else has it" — which the requester already learns from the next
    /// <see cref="ClaimChanged"/> — and "this session does not answer for that number", which is a
    /// modded client's own problem and not something to describe back to it.
    /// </remarks>
    public struct ClaimDenied : INetMessage {
        /// <summary>Creates the refusal for one slot.</summary>
        public ClaimDenied(ushort slot) {
            Slot = slot;
        }

        /// <summary>Which slot was asked for and not given.</summary>
        public ushort Slot { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteUShort(Slot);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Slot = reader.ReadUShort();
        }
    }
}
