using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Appearance {
    /// <summary>
    /// A wearable item: the slot it fills, one prefab per character model, and the material sets a pick's
    /// variant index chooses between.
    /// </summary>
    [CreateAssetMenu(fileName = "AppearanceItem", menuName = "AlpineLib/Appearance/Item")]
    public class AppearanceItem : ScriptableObject {
        [Tooltip("Key of the slot this item fills on every model that wears it.")]
        [SerializeField] private string slotKey;
        [Tooltip("Whether the wearer sees this item in first person; UseSlot follows the slot's default.")]
        [SerializeField] private AppearanceFirstPersonVisibility firstPersonVisibility;
        [Tooltip("One prefab per character model; at most one per model.")]
        [SerializeField] private List<AppearanceItemVariant> variants = new List<AppearanceItemVariant>();
        [Tooltip("Append-only. A set's index is the variant on the wire; never reorder or delete. Empty = the prefab's own materials.")]
        [SerializeField] private List<AppearanceMaterialSet> materialSets = new List<AppearanceMaterialSet>();

        /// <summary>Key of the slot this item fills.</summary>
        public string SlotKey => slotKey;

        /// <summary>The item's first-person visibility override.</summary>
        public AppearanceFirstPersonVisibility FirstPersonVisibility => firstPersonVisibility;

        /// <summary>Per-model prefabs.</summary>
        public IReadOnlyList<AppearanceItemVariant> Variants => variants;

        /// <summary>Selectable looks, by variant index.</summary>
        public IReadOnlyList<AppearanceMaterialSet> MaterialSets => materialSets;

        /// <summary>Number of pickable variants: one per material set, and at least one.</summary>
        public int VariantCount => Mathf.Max(1, materialSets?.Count ?? 0);

        /// <summary>Creates an in-memory item; for tools and tests.</summary>
        public static AppearanceItem Create(string name, string slotKey, AppearanceFirstPersonVisibility firstPersonVisibility,
            IEnumerable<AppearanceItemVariant> variants, IEnumerable<AppearanceMaterialSet> materialSets) {
            AppearanceItem item = CreateInstance<AppearanceItem>();
            item.name = name;
            item.slotKey = slotKey;
            item.firstPersonVisibility = firstPersonVisibility;
            item.variants = new List<AppearanceItemVariant>(variants);
            item.materialSets = new List<AppearanceMaterialSet>(materialSets);
            return item;
        }

        /// <summary>Finds the variant authored for exactly <paramref name="model"/>.</summary>
        public bool TryGetVariantFor(CharacterModel model, out AppearanceItemVariant variant) {
            variant = null;
            if (model == null || variants == null) return false;

            foreach (AppearanceItemVariant candidate in variants) {
                if (candidate == null || candidate.Model != model) continue;

                variant = candidate;
                return true;
            }

            return false;
        }

        /// <summary>The material set for <paramref name="variantIndex"/>, or null when there is none.</summary>
        public AppearanceMaterialSet MaterialSetAt(int variantIndex) {
            if (materialSets == null || variantIndex < 0 || variantIndex >= materialSets.Count) return null;

            return materialSets[variantIndex];
        }

        /// <summary>Resolves the item's override against its slot's default.</summary>
        public bool IsHiddenInFirstPerson(bool slotDefault) {
            switch (firstPersonVisibility) {
                case AppearanceFirstPersonVisibility.Hidden:
                    return true;
                case AppearanceFirstPersonVisibility.Visible:
                    return false;
                default:
                    return slotDefault;
            }
        }
    }
}
