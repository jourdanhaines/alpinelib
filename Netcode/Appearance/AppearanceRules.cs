using System;

namespace AlpineLib.Netcode.Appearance {
    /// <summary>
    /// Whether an outfit may be worn. Shared by the client builder, the inspector, the exporter and the
    /// server, so a request the server accepts is one every client can build.
    /// </summary>
    /// <remarks>
    /// The model must exist and the outfit must carry exactly its slot count. An empty slot has variant 0;
    /// a filled slot names a known item made for that slot, allowed on the model, at an existing variant.
    /// </remarks>
    public static class AppearanceRules {
        /// <summary>
        /// Checks <paramref name="outfit"/> against <paramref name="catalog"/>. <paramref name="error"/>
        /// names the first broken rule, or is null when the outfit is valid.
        /// </summary>
        public static bool Validate(in AppearanceOutfit outfit, IAppearanceCatalog catalog, out string error) {
            if (catalog == null) {
                throw new ArgumentNullException(nameof(catalog));
            }

            error = CheckModel(outfit, catalog) ?? CheckSlots(outfit, catalog);
            return error == null;
        }

        /// <summary>
        /// <see cref="Validate"/>, after first requiring the outfit to be for <paramref name="expectedModelId"/>
        /// — the model the sender actually spawned as.
        /// </summary>
        public static bool ValidateForModel(in AppearanceOutfit outfit, ushort expectedModelId, IAppearanceCatalog catalog, out string error) {
            if (outfit.ModelId != expectedModelId) {
                error = $"Outfit is for model {outfit.ModelId}, but the character is model {expectedModelId}.";
                return false;
            }

            return Validate(outfit, catalog, out error);
        }

        private static string CheckModel(in AppearanceOutfit outfit, IAppearanceCatalog catalog) {
            if (!catalog.TryGetModel(outfit.ModelId, out AppearanceModelInfo model)) return $"Unknown model {outfit.ModelId}.";
            if (outfit.SlotCount != model.SlotCount) return $"Outfit carries {outfit.SlotCount} slot(s); model {model.Id} has {model.SlotCount}.";

            return null;
        }

        private static string CheckSlots(in AppearanceOutfit outfit, IAppearanceCatalog catalog) {
            for (int index = 0; index < outfit.SlotCount; index++) {
                string error = CheckSlot(index, outfit[index], outfit.ModelId, catalog);
                if (error != null) return error;
            }

            return null;
        }

        private static string CheckSlot(int slotIndex, AppearanceSlotPick pick, ushort modelId, IAppearanceCatalog catalog) {
            if (pick.IsEmpty) {
                return pick.VariantIndex == 0 ? null : $"Slot {slotIndex} is empty but names variant {pick.VariantIndex}.";
            }

            if (!catalog.TryGetItem(pick.ItemId, out AppearanceItemInfo item)) return $"Slot {slotIndex} holds unknown item {pick.ItemId}.";
            if (item.SlotIndex != slotIndex) return $"Item {item.Id} belongs in slot {item.SlotIndex}, not slot {slotIndex}.";
            if (!item.AllowsModel(modelId)) return $"Item {item.Id} is not made for model {modelId}.";
            if (pick.VariantIndex >= item.VariantCount) return $"Item {item.Id} has {item.VariantCount} variant(s); variant {pick.VariantIndex} does not exist.";

            return null;
        }
    }
}
