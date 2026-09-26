using UnityEngine;

namespace AlpineLib.Procedural.Kits {
    /// <summary>Resolves a kit part name from a layout to the prefab that is instantiated for it.</summary>
    public interface IKitPartSource {
        /// <summary>Finds the prefab for <paramref name="partName"/>; false when the kit has no such part.</summary>
        bool TryGetPrefab(string partName, out GameObject prefab);
    }
}
