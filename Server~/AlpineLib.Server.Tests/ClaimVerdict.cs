namespace AlpineLib.Server.Tests {
    /// <summary>One recorded "slot S now belongs to peer P", or to nobody when the holder is -1.</summary>
    internal readonly struct ClaimVerdict {
        public ClaimVerdict(ushort slot, int holderPeerId) {
            Slot = slot;
            HolderPeerId = holderPeerId;
        }

        /// <summary>Which slot the verdict was about.</summary>
        public ushort Slot { get; }

        /// <summary>Who holds it now, or -1 when it went free.</summary>
        public int HolderPeerId { get; }

        /// <inheritdoc />
        public override string ToString() {
            return "Claim(slot=" + Slot.ToString() + ", holder=" + HolderPeerId.ToString() + ")";
        }
    }
}
