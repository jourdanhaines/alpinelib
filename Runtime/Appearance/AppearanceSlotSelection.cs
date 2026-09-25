using System;
using UnityEngine;

namespace AlpineLib.Appearance {
    /// <summary>An authored pick for one slot: an item asset and which of its variants.</summary>
    [Serializable]
    public class AppearanceSlotSelection {
        [Tooltip("Item worn in this slot; empty leaves the slot bare.")]
        [SerializeField] private AppearanceItem item;
        [Tooltip("Index of the item's material set.")]
        [SerializeField] private int variant;

        public AppearanceSlotSelection() {
        }

        public AppearanceSlotSelection(AppearanceItem item, int variant) {
            this.item = item;
            this.variant = variant;
        }

        /// <summary>Item worn in this slot, or null.</summary>
        public AppearanceItem Item => item;

        /// <summary>Index of the item's material set.</summary>
        public int Variant => variant;

        /// <summary>True when the slot holds no item.</summary>
        public bool IsEmpty => item == null;
    }
}
