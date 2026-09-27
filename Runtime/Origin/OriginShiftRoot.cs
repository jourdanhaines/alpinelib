using UnityEngine;

namespace AlpineLib.Origin {
    /// <summary>
    /// Marks this transform as a world-frame root that a floating-origin rebase translates. Registered
    /// from its first activation until destroyed, so a root disabled later still moves with the world.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OriginShiftRoot : MonoBehaviour {
        private void Awake() {
            OriginShiftRegistry.RegisterRoot(transform);
        }

        private void OnDestroy() {
            OriginShiftRegistry.UnregisterRoot(transform);
        }
    }
}
