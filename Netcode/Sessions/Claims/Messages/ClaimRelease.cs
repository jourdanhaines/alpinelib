using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Claims.Messages {
    /// <summary>
    /// A client gives up a claim slot it holds.
    /// </summary>
    /// <remarks>
    /// Only the holder's release is honoured, so this is safe to send unconditionally when a player walks
    /// away from whatever the slot stands for. A release from anyone else is dropped rather than
    /// answered, which keeps one peer from prising another off a lever by naming its slot.
    /// </remarks>
    public struct ClaimRelease : INetMessage {
        /// <summary>Creates the release for one slot.</summary>
        public ClaimRelease(ushort slot) {
            Slot = slot;
        }

        /// <summary>Which slot is being given up.</summary>
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
