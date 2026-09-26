using UnityEngine;

namespace AlpineLib.Procedural.Kits {
    /// <summary>Finds named socket transforms (exported empties) under one kit model.</summary>
    /// <remarks>
    /// Socket names are unique per model only, so search from a single model's root, never from a parent
    /// holding several. The search is depth-first in hierarchy order, so a model's own sockets are found
    /// before those of parts mounted onto it afterwards.
    /// </remarks>
    public static class KitSockets {
        /// <summary>Finds a socket anywhere under a model root, including inactive children. Logs when it is missing.</summary>
        public static Transform Find(Transform modelRoot, string socketName) {
            if (TryFind(modelRoot, socketName, out Transform socket)) return socket;

            string rootName = modelRoot != null ? modelRoot.name : "<null>";
            Debug.LogError($"KitSockets::Find->{rootName} has no '{socketName}'; re-export the model with its socket empties.");
            return null;
        }

        /// <summary>Finds a socket anywhere under a model root, including inactive children, without logging.</summary>
        public static bool TryFind(Transform modelRoot, string socketName, out Transform socket) {
            socket = null;
            if (modelRoot == null || string.IsNullOrEmpty(socketName)) return false;

            foreach (Transform child in modelRoot.GetComponentsInChildren<Transform>(true)) {
                if (child.name != socketName) continue;

                socket = child;
                return true;
            }

            return false;
        }
    }
}
