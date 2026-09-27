using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Origin {
    /// <summary>
    /// Everything a floating-origin rebase moves on this client: world-frame scene roots (translated) and
    /// <see cref="IOriginShiftListener"/>s (told), applied together by <see cref="Apply"/>.
    /// </summary>
    /// <remarks>
    /// Roots must be world-frame and never nested under another registered root, or they move twice.
    /// Carrier-parented objects are not registered: they ride their carrier. <see cref="Apply"/> ends with
    /// <c>Physics.SyncTransforms</c> so the next motor step sweeps colliders where they now are.
    /// </remarks>
    public static class OriginShiftRegistry {
        private static readonly List<Transform> Roots = new List<Transform>();
        private static readonly List<IOriginShiftListener> Listeners = new List<IOriginShiftListener>();

        /// <summary>Registered roots, destroyed ones included until they unregister.</summary>
        public static int RootCount => Roots.Count;

        /// <summary>Registered listeners.</summary>
        public static int ListenerCount => Listeners.Count;

        /// <summary>Adds a world-frame root translated by every shift; repeats are ignored.</summary>
        public static void RegisterRoot(Transform root) {
            if (root == null || Roots.Contains(root)) return;

            Roots.Add(root);
        }

        /// <summary>Stops translating a root.</summary>
        public static bool UnregisterRoot(Transform root) {
            return Roots.Remove(root);
        }

        /// <summary>Adds a listener told about every shift, in registration order; repeats are ignored.</summary>
        public static void RegisterListener(IOriginShiftListener listener) {
            if (listener == null || Listeners.Contains(listener)) return;

            Listeners.Add(listener);
        }

        /// <summary>Stops telling a listener.</summary>
        public static bool UnregisterListener(IOriginShiftListener listener) {
            return Listeners.Remove(listener);
        }

        /// <summary>True when the listener is registered.</summary>
        public static bool IsRegistered(IOriginShiftListener listener) {
            return Listeners.Contains(listener);
        }

        /// <summary>
        /// Translates every root, tells every listener, then syncs physics — all before the next motor step.
        /// Registrations changed by a listener take effect on the next shift.
        /// </summary>
        public static void Apply(Vector3 delta) {
            if (delta == Vector3.zero) return;

            foreach (Transform root in Roots.ToArray()) {
                if (root != null) root.position += delta;
            }

            foreach (IOriginShiftListener listener in Listeners.ToArray()) {
                if (IsAlive(listener)) listener.OnOriginShifted(delta);
            }

            Physics.SyncTransforms();
        }

        // A destroyed component that never unregistered compares equal to null through Unity's operator.
        private static bool IsAlive(IOriginShiftListener listener) {
            if (listener is Object unityObject) return unityObject != null;

            return listener != null;
        }
    }
}
