using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Claims.Messages {
    /// <summary>
    /// The server's verdict on one slot: who holds it now, or that nobody does.
    /// </summary>
    /// <remarks>
    /// This is the only claim message that ever leaves the server, and it is the whole client-visible
    /// state of a slot. It is broadcast on every transition and re-sent per held slot as a join keyframe,
    /// so a client that receives one it already agrees with must treat it as a no-op rather than as a
    /// fresh grant.
    /// </remarks>
    public struct ClaimChanged : INetMessage {
        /// <summary>Creates the verdict for one slot.</summary>
        public ClaimChanged(ushort slot, int holderPeerId) {
            Slot = slot;
            HolderPeerId = holderPeerId;
        }

        /// <summary>Which slot this is about.</summary>
        public ushort Slot { get; set; }

        /// <summary>Peer id of the holder, or -1 when the slot is free.</summary>
        public int HolderPeerId { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteUShort(Slot);
            writer.WriteInt(HolderPeerId);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Slot = reader.ReadUShort();
            HolderPeerId = reader.ReadInt();
        }
    }
}
