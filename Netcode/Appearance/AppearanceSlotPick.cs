using System;

namespace AlpineLib.Netcode.Appearance {
    /// <summary>
    /// What one appearance slot is wearing: a catalog item id and which of its variants. Item id 0 leaves
    /// the slot empty.
    /// </summary>
    public readonly struct AppearanceSlotPick : IEquatable<AppearanceSlotPick> {
        /// <summary>A pick that leaves its slot empty.</summary>
        public static AppearanceSlotPick Empty => default;

        public AppearanceSlotPick(ushort itemId, byte variantIndex) {
            ItemId = itemId;
            VariantIndex = variantIndex;
        }

        /// <summary>Catalog item id, or 0 for an empty slot.</summary>
        public ushort ItemId { get; }

        /// <summary>Which of the item's variants (material sets) is worn.</summary>
        public byte VariantIndex { get; }

        /// <summary>True when the slot holds no item.</summary>
        public bool IsEmpty => ItemId == 0;

        /// <inheritdoc />
        public bool Equals(AppearanceSlotPick other) {
            return ItemId == other.ItemId && VariantIndex == other.VariantIndex;
        }

        /// <inheritdoc />
        public override bool Equals(object obj) {
            return obj is AppearanceSlotPick other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() {
            return HashCode.Combine(ItemId, VariantIndex);
        }

        /// <inheritdoc />
        public override string ToString() {
            return IsEmpty ? "empty" : $"{ItemId}:{VariantIndex}";
        }

        public static bool operator ==(AppearanceSlotPick left, AppearanceSlotPick right) {
            return left.Equals(right);
        }

        public static bool operator !=(AppearanceSlotPick left, AppearanceSlotPick right) {
            return !left.Equals(right);
        }
    }
}
