using System.Collections.Generic;

namespace AlpineLib.Netcode.Appearance {
    /// <summary>
    /// The engine-free facts about one character model: its catalog id, how many appearance slots it
    /// has, which of them may never be empty, and which pawn prefab the server spawns for it.
    /// </summary>
    public readonly struct AppearanceModelInfo {
        public AppearanceModelInfo(ushort id, byte slotCount, ushort pawnPrefabId, ushort requiredSlotMask = 0) {
            Id = id;
            SlotCount = slotCount;
            PawnPrefabId = pawnPrefabId;
            RequiredSlotMask = requiredSlotMask;
        }

        /// <summary>Catalog id; never 0.</summary>
        public ushort Id { get; }

        /// <summary>Slots an outfit for this model carries, in wire order.</summary>
        public byte SlotCount { get; }

        /// <summary>Net prefab registry row of the pawn wearing this model; 0 is row 0, not "none".</summary>
        public ushort PawnPrefabId { get; }

        /// <summary>Bit <c>n</c> set: slot <c>n</c> must hold an item in every outfit (e.g. a face's nose).</summary>
        public ushort RequiredSlotMask { get; }

        /// <summary>Whether slot <paramref name="slotIndex"/> must hold an item.</summary>
        public bool IsSlotRequired(int slotIndex) {
            return slotIndex >= 0 && slotIndex < AppearanceOutfit.MaxSlots && (RequiredSlotMask & (1 << slotIndex)) != 0;
        }

        /// <summary>The required slot indices, ascending.</summary>
        public IReadOnlyList<byte> RequiredSlots() {
            var slots = new List<byte>();
            for (int slotIndex = 0; slotIndex < AppearanceOutfit.MaxSlots; slotIndex++) {
                if (IsSlotRequired(slotIndex)) slots.Add((byte)slotIndex);
            }

            return slots;
        }

        /// <summary>The mask of <paramref name="slotIndices"/>.</summary>
        /// <exception cref="System.ArgumentException">An index is outside 0 to <see cref="AppearanceOutfit.MaxSlots"/> - 1.</exception>
        public static ushort MaskOf(IEnumerable<int> slotIndices) {
            int mask = 0;
            if (slotIndices == null) return 0;

            foreach (int slotIndex in slotIndices) {
                if (slotIndex < 0 || slotIndex >= AppearanceOutfit.MaxSlots) {
                    throw new System.ArgumentException($"Required slot {slotIndex} is outside the {AppearanceOutfit.MaxSlots}-slot cap.");
                }

                mask |= 1 << slotIndex;
            }

            return (ushort)mask;
        }

        /// <inheritdoc />
        public override string ToString() {
            string required = RequiredSlotMask == 0 ? string.Empty : $", required slots [{string.Join(", ", RequiredSlots())}]";
            return $"model {Id} ({SlotCount} slots{required}, pawn prefab {PawnPrefabId})";
        }
    }
}
