using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Procedural.Kits {
    /// <summary>
    /// A kit as an asset: part names mapped to prefabs. The simplest <see cref="IKitPartSource"/>; a game
    /// with richer per-part data can implement the interface on its own asset instead.
    /// </summary>
    [CreateAssetMenu(menuName = "AlpineLib/Procedural/Kit Part Table", fileName = "KitPartTable")]
    public sealed class KitPartTable : ScriptableObject, IKitPartSource {
        [Tooltip("Parts in the kit. A duplicate name keeps its first row.")]
        [SerializeField] private KitPartEntry[] entries = new KitPartEntry[0];

        private Dictionary<string, GameObject> _lookup;

        /// <summary>Every row, in authored order.</summary>
        public IReadOnlyList<KitPartEntry> Entries => entries;

        /// <inheritdoc />
        public bool TryGetPrefab(string partName, out GameObject prefab) {
            prefab = null;
            if (string.IsNullOrEmpty(partName)) return false;

            _lookup ??= BuildLookup();
            return _lookup.TryGetValue(partName, out prefab) && prefab != null;
        }

        private void OnValidate() {
            _lookup = null;
        }

        private Dictionary<string, GameObject> BuildLookup() {
            var lookup = new Dictionary<string, GameObject>();
            if (entries == null) return lookup;

            foreach (KitPartEntry entry in entries) {
                if (entry == null || string.IsNullOrEmpty(entry.PartName)) continue;
                if (lookup.TryAdd(entry.PartName, entry.Prefab)) continue;

                Debug.LogWarning($"KitPartTable::BuildLookup->{name} lists '{entry.PartName}' twice; the first row wins.");
            }

            return lookup;
        }
    }
}
