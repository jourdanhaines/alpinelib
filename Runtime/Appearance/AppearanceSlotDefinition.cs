using System;
using UnityEngine;

namespace AlpineLib.Appearance {
    /// <summary>One wearable slot of a <see cref="CharacterModel"/>: a key items name and a first-person default.</summary>
    [Serializable]
    public class AppearanceSlotDefinition {
        [Tooltip("Stable name items refer to (e.g. Hat, Torso). Never rename once items use it.")]
        [SerializeField] private string key;
        [Tooltip("Hide this slot's piece from its wearer in first person, e.g. anything in front of the eyes.")]
        [SerializeField] private bool hiddenInFirstPerson;

        public AppearanceSlotDefinition() {
        }

        public AppearanceSlotDefinition(string key, bool hiddenInFirstPerson) {
            this.key = key;
            this.hiddenInFirstPerson = hiddenInFirstPerson;
        }

        /// <summary>Stable slot name items refer to.</summary>
        public string Key => key;

        /// <summary>Default first-person visibility for items in this slot.</summary>
        public bool HiddenInFirstPerson => hiddenInFirstPerson;
    }
}
