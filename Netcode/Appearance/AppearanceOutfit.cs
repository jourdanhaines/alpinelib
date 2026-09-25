using System;
using System.Collections.Generic;
using System.Text;

namespace AlpineLib.Netcode.Appearance {
    /// <summary>
    /// A character model id plus one <see cref="AppearanceSlotPick"/> per slot of that model, in the
    /// model's slot order. Immutable: <see cref="With"/> returns a changed copy.
    /// </summary>
    /// <remarks>
    /// Whether the picks are allowed is <see cref="AppearanceRules"/>' call; this type only caps the slot
    /// count at <see cref="MaxSlots"/> so the wire size stays bounded. The default value has model 0 and
    /// no slots.
    /// </remarks>
    public readonly struct AppearanceOutfit : IEquatable<AppearanceOutfit> {
        /// <summary>Most slots any character model may declare.</summary>
        public const int MaxSlots = 16;

        private readonly AppearanceSlotPick[] picks;

        private AppearanceOutfit(ushort modelId, AppearanceSlotPick[] picks) {
            ModelId = modelId;
            this.picks = picks;
        }

        /// <summary>Catalog id of the character model this outfit is for.</summary>
        public ushort ModelId { get; }

        /// <summary>Number of slots the outfit carries.</summary>
        public int SlotCount => picks == null ? 0 : picks.Length;

        /// <summary>The pick in <paramref name="slotIndex"/>.</summary>
        public AppearanceSlotPick this[int slotIndex] {
            get {
                RequireSlotIndex(slotIndex);
                return picks[slotIndex];
            }
        }

        /// <summary>An outfit for <paramref name="modelId"/> holding a copy of <paramref name="slotPicks"/>.</summary>
        public static AppearanceOutfit Create(ushort modelId, IReadOnlyList<AppearanceSlotPick> slotPicks) {
            if (slotPicks == null) {
                throw new ArgumentNullException(nameof(slotPicks));
            }

            RequireSlotCount(slotPicks.Count, nameof(slotPicks));
            var copy = new AppearanceSlotPick[slotPicks.Count];
            for (int index = 0; index < copy.Length; index++) {
                copy[index] = slotPicks[index];
            }

            return new AppearanceOutfit(modelId, copy);
        }

        /// <summary>An outfit for <paramref name="modelId"/> with <paramref name="slotCount"/> empty slots.</summary>
        public static AppearanceOutfit EmptyFor(ushort modelId, int slotCount) {
            RequireSlotCount(slotCount, nameof(slotCount));
            return new AppearanceOutfit(modelId, new AppearanceSlotPick[slotCount]);
        }

        /// <summary>A copy of this outfit with <paramref name="slotIndex"/> holding <paramref name="pick"/>.</summary>
        public AppearanceOutfit With(int slotIndex, AppearanceSlotPick pick) {
            RequireSlotIndex(slotIndex);
            var copy = (AppearanceSlotPick[])picks.Clone();
            copy[slotIndex] = pick;
            return new AppearanceOutfit(ModelId, copy);
        }

        /// <inheritdoc />
        public bool Equals(AppearanceOutfit other) {
            if (ModelId != other.ModelId || SlotCount != other.SlotCount) {
                return false;
            }

            for (int index = 0; index < SlotCount; index++) {
                if (picks[index] != other.picks[index]) return false;
            }

            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object obj) {
            return obj is AppearanceOutfit other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() {
            var hash = new HashCode();
            hash.Add(ModelId);
            hash.Add(SlotCount);
            for (int index = 0; index < SlotCount; index++) {
                hash.Add(picks[index]);
            }

            return hash.ToHashCode();
        }

        /// <inheritdoc />
        public override string ToString() {
            var builder = new StringBuilder();
            builder.Append("model ").Append(ModelId).Append(" [");
            for (int index = 0; index < SlotCount; index++) {
                if (index > 0) builder.Append(", ");
                builder.Append(picks[index].ToString());
            }

            return builder.Append(']').ToString();
        }

        public static bool operator ==(AppearanceOutfit left, AppearanceOutfit right) {
            return left.Equals(right);
        }

        public static bool operator !=(AppearanceOutfit left, AppearanceOutfit right) {
            return !left.Equals(right);
        }

        private void RequireSlotIndex(int slotIndex) {
            if (slotIndex >= 0 && slotIndex < SlotCount) {
                return;
            }

            throw new ArgumentOutOfRangeException(nameof(slotIndex), $"Slot {slotIndex} is outside an outfit of {SlotCount} slot(s).");
        }

        private static void RequireSlotCount(int slotCount, string parameterName) {
            if (slotCount >= 0 && slotCount <= MaxSlots) {
                return;
            }

            throw new ArgumentOutOfRangeException(parameterName, $"An outfit holds 0 to {MaxSlots} slots, not {slotCount}.");
        }
    }
}
