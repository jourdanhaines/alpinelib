using System.Collections.Generic;
using AlpineLib.Netcode.Appearance;
using UnityEngine;

namespace AlpineLib.Appearance {
    /// <summary>A saved look: a character model and an item pick per slot, by asset reference.</summary>
    [CreateAssetMenu(fileName = "AppearanceProfile", menuName = "AlpineLib/Appearance/Profile")]
    public class AppearanceProfile : ScriptableObject {
        [Tooltip("Character model this look is for.")]
        [SerializeField] private CharacterModel model;
        [Tooltip("One pick per model slot, in slot order; missing entries are empty.")]
        [SerializeField] private List<AppearanceSlotSelection> slots = new List<AppearanceSlotSelection>();

        /// <summary>Character model this look is for.</summary>
        public CharacterModel Model => model;

        /// <summary>Picks in slot order.</summary>
        public IReadOnlyList<AppearanceSlotSelection> Slots => slots;

        /// <summary>
        /// Turns the picks into catalog ids and validates them. <paramref name="error"/> names the first
        /// problem, or is null.
        /// </summary>
        public bool TryToOutfit(AppearanceCatalog catalog, out AppearanceOutfit outfit, out string error) {
            outfit = default;
            error = CheckHeader(catalog);
            if (error != null) return false;

            var picks = new AppearanceSlotPick[model.SlotCount];
            int count = slots == null ? 0 : slots.Count;
            for (int index = 0; index < count; index++) {
                error = TryPick(catalog, index, picks);
                if (error != null) return false;
            }

            outfit = AppearanceOutfit.Create(catalog.IdOf(model), picks);
            return catalog.TryValidate(outfit, out error);
        }

        /// <summary>Replaces the model and picks with copies of <paramref name="selections"/>.</summary>
        public void CopyFrom(CharacterModel sourceModel, IReadOnlyList<AppearanceSlotSelection> selections) {
            model = sourceModel;
            slots = new List<AppearanceSlotSelection>();
            if (selections == null) return;

            foreach (AppearanceSlotSelection selection in selections) {
                slots.Add(selection == null ? new AppearanceSlotSelection() : new AppearanceSlotSelection(selection.Item, selection.Variant));
            }
        }

        private string CheckHeader(AppearanceCatalog catalog) {
            if (catalog == null) return $"Profile '{name}' was given no catalog.";
            if (model == null) return $"Profile '{name}' has no character model.";
            if (catalog.IdOf(model) == 0) return $"Profile '{name}' model '{model.name}' is not in catalog '{catalog.name}'.";
            if (model.SlotCount > AppearanceOutfit.MaxSlots) return $"Profile '{name}' model '{model.name}' has more than {AppearanceOutfit.MaxSlots} slots.";

            return null;
        }

        private string TryPick(AppearanceCatalog catalog, int slotIndex, AppearanceSlotPick[] picks) {
            AppearanceSlotSelection selection = slots[slotIndex];
            if (selection == null || selection.IsEmpty) return null;
            if (slotIndex >= picks.Length) return $"Profile '{name}' fills slot {slotIndex}, but model '{model.name}' has {picks.Length}.";

            ushort itemId = catalog.IdOf(selection.Item);
            if (itemId == 0) return $"Profile '{name}' item '{selection.Item.name}' is not in catalog '{catalog.name}'.";
            if (selection.Variant < 0 || selection.Variant > byte.MaxValue) return $"Profile '{name}' slot {slotIndex} names variant {selection.Variant}.";

            picks[slotIndex] = new AppearanceSlotPick(itemId, (byte)selection.Variant);
            return null;
        }
    }
}
