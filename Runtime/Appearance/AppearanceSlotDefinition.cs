using System;
using UnityEngine;

namespace AlpineLib.Appearance {
    /// <summary>
    /// One wearable slot of a <see cref="CharacterModel"/>: a key items name, a first-person default and
    /// whether every outfit must fill it.
    /// </summary>
    [Serializable]
    public class AppearanceSlotDefinition {
        [Tooltip("Stable name items refer to (e.g. Hat, Torso). Never rename once items use it.")]
        [SerializeField] private string key;
        [Tooltip("Hide this slot's piece from its wearer in first person, e.g. anything in front of the eyes.")]
        [SerializeField] private bool hiddenInFirstPerson;
        [Tooltip("Every outfit must fill this slot (e.g. a face's nose); an outfit leaving it empty is refused.")]
        [SerializeField] private bool required;

        public AppearanceSlotDefinition() {
        }

        public AppearanceSlotDefinition(string key, bool hiddenInFirstPerson, bool required = false) {
            this.key = key;
            this.hiddenInFirstPerson = hiddenInFirstPerson;
            this.required = required;
        }

        /// <summary>Stable slot name items refer to.</summary>
        public string Key => key;

        /// <summary>Default first-person visibility for items in this slot.</summary>
        public bool HiddenInFirstPerson => hiddenInFirstPerson;

        /// <summary>Whether every outfit must hold an item in this slot.</summary>
        public bool Required => required;
    }
}
