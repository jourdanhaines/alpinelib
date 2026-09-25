using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Appearance {
    /// <summary>
    /// A character body items are made for, and its wearable slots. Slot order is wire order, so slots
    /// are append-only.
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterModel", menuName = "AlpineLib/Appearance/Character Model")]
    public class CharacterModel : ScriptableObject {
        [Tooltip("Stable, human-readable model name (e.g. MaleT).")]
        [SerializeField] private string key;
        [Tooltip("The body's imported model (FBX root). Used by validation and editor tools only.")]
        [SerializeField] private GameObject bodyModel;
        [Tooltip("Append-only. A slot's index is its position in every outfit on the wire; never reorder or delete.")]
        [SerializeField] private List<AppearanceSlotDefinition> slots = new List<AppearanceSlotDefinition>();

        /// <summary>Stable model name.</summary>
        public string Key => key;

        /// <summary>The body's imported model; editor and validation use only.</summary>
        public GameObject BodyModel => bodyModel;

        /// <summary>Number of wearable slots.</summary>
        public int SlotCount => slots?.Count ?? 0;

        /// <summary>Creates an in-memory model; for tools and tests.</summary>
        public static CharacterModel Create(string key, GameObject bodyModel, IEnumerable<AppearanceSlotDefinition> slots) {
            CharacterModel model = CreateInstance<CharacterModel>();
            model.name = key;
            model.key = key;
            model.bodyModel = bodyModel;
            model.slots = new List<AppearanceSlotDefinition>(slots);
            return model;
        }

        /// <summary>The slot at <paramref name="slotIndex"/>, or null when out of range.</summary>
        public AppearanceSlotDefinition SlotAt(int slotIndex) {
            if (slotIndex < 0 || slotIndex >= SlotCount) return null;

            return slots[slotIndex];
        }

        /// <summary>Index of the slot keyed <paramref name="slotKey"/>, or -1.</summary>
        public int IndexOfSlot(string slotKey) {
            if (string.IsNullOrEmpty(slotKey)) return -1;

            for (int index = 0; index < SlotCount; index++) {
                if (slots[index] != null && slots[index].Key == slotKey) return index;
            }

            return -1;
        }
    }
}
