using System;
using System.Collections.Generic;

namespace AlpineLib.Netcode.Appearance {
    /// <summary>
    /// The engine-free facts about one wearable item: which slot it fills, how many variants it has, and
    /// which character models may wear it.
    /// </summary>
    public sealed class AppearanceItemInfo {
        private static readonly ushort[] NoModels = Array.Empty<ushort>();

        /// <summary>
        /// Creates an item. <paramref name="allowedModelIds"/> is copied, sorted and de-duplicated; null or
        /// empty means any model may wear it.
        /// </summary>
        public AppearanceItemInfo(ushort id, byte slotIndex, byte variantCount, IEnumerable<ushort> allowedModelIds) {
            Id = id;
            SlotIndex = slotIndex;
            VariantCount = variantCount;
            AllowedModelIds = NormaliseModelIds(allowedModelIds);
        }

        /// <summary>Catalog id; never 0.</summary>
        public ushort Id { get; }

        /// <summary>Index of the slot this item fills on every model that wears it.</summary>
        public byte SlotIndex { get; }

        /// <summary>Number of variants; a pick's variant index must be below this.</summary>
        public byte VariantCount { get; }

        /// <summary>Models that may wear this item, ascending. Empty means any model.</summary>
        public IReadOnlyList<ushort> AllowedModelIds { get; }

        /// <summary>True when <paramref name="modelId"/> may wear this item.</summary>
        public bool AllowsModel(ushort modelId) {
            if (AllowedModelIds.Count == 0) {
                return true;
            }

            for (int index = 0; index < AllowedModelIds.Count; index++) {
                if (AllowedModelIds[index] == modelId) return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override string ToString() {
            return $"item {Id} (slot {SlotIndex}, {VariantCount} variant(s))";
        }

        private static IReadOnlyList<ushort> NormaliseModelIds(IEnumerable<ushort> modelIds) {
            if (modelIds == null) {
                return NoModels;
            }

            var sorted = new SortedSet<ushort>(modelIds);
            var copy = new ushort[sorted.Count];
            sorted.CopyTo(copy);
            return copy;
        }
    }
}
