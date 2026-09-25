namespace AlpineLib.Netcode.Appearance {
    /// <summary>
    /// The engine-free facts about one character model: its catalog id, how many appearance slots it
    /// has, and which pawn prefab the server spawns for it.
    /// </summary>
    public readonly struct AppearanceModelInfo {
        public AppearanceModelInfo(ushort id, byte slotCount, ushort pawnPrefabId) {
            Id = id;
            SlotCount = slotCount;
            PawnPrefabId = pawnPrefabId;
        }

        /// <summary>Catalog id; never 0.</summary>
        public ushort Id { get; }

        /// <summary>Slots an outfit for this model carries, in wire order.</summary>
        public byte SlotCount { get; }

        /// <summary>Net prefab id of the pawn wearing this model, or 0 for the session's default pawn.</summary>
        public ushort PawnPrefabId { get; }

        /// <inheritdoc />
        public override string ToString() {
            return $"model {Id} ({SlotCount} slots, pawn prefab {PawnPrefabId})";
        }
    }
}
