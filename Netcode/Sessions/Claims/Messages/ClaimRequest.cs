using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Claims.Messages {
    /// <summary>
    /// A client asks to take one claim slot.
    /// </summary>
    /// <remarks>
    /// There is no reply message. A request that wins comes back as the <see cref="ClaimChanged"/> the
    /// whole session receives, and one that loses comes back as nothing at all — the requester already
    /// holds a <see cref="ClaimChanged"/> naming the other holder, so a private denial would tell it
    /// only what it can already see.
    /// </remarks>
    public struct ClaimRequest : INetMessage {
        /// <summary>Creates the request for one slot.</summary>
        public ClaimRequest(ushort slot) {
            Slot = slot;
        }

        /// <summary>Which slot is being asked for. Slot numbering is the game's to define.</summary>
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
