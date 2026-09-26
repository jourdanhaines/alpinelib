using System;
using UnityEngine;

namespace AlpineLib.Procedural.Kits {
    /// <summary>One row of a <see cref="KitPartTable"/>: a part name and the prefab it instantiates.</summary>
    [Serializable]
    public sealed class KitPartEntry {
        [Tooltip("Name layouts refer to this part by; unique within the table.")]
        [SerializeField] private string partName;
        [Tooltip("Prefab instantiated for the part.")]
        [SerializeField] private GameObject prefab;

        /// <summary>An empty row, for serialization.</summary>
        public KitPartEntry() {
        }

        /// <summary>A row mapping <paramref name="partName"/> to <paramref name="prefab"/>.</summary>
        public KitPartEntry(string partName, GameObject prefab) {
            this.partName = partName;
            this.prefab = prefab;
        }

        /// <summary>Name layouts refer to this part by.</summary>
        public string PartName => partName;

        /// <summary>Prefab instantiated for the part.</summary>
        public GameObject Prefab => prefab;
    }
}
