using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Vitals {
    /// <summary>
    /// Index of the <see cref="ResourcePool"/> components on the same object, keyed by their
    /// <see cref="ResourceDefinition"/>. Lets game code ask an actor for "its mana" by asset
    /// reference instead of holding a serialized field per pool.
    /// </summary>
    public class ResourceSet : MonoBehaviour {
        private readonly Dictionary<ResourceDefinition, ResourcePool> _pools = new();

        /// <summary>Pools on this object, keyed by the definition each was built from.</summary>
        public IReadOnlyDictionary<ResourceDefinition, ResourcePool> Pools => _pools;

        private void Awake() {
            Rebuild();
        }

        /// <summary>
        /// The pool built from a definition, or null when this object has no such pool.
        /// </summary>
        public ResourcePool Get(ResourceDefinition definition) {
            if (definition == null) return null;

            return _pools.TryGetValue(definition, out var pool) ? pool : null;
        }

        /// <summary>
        /// Rebuilds the index from the pools currently attached. Called on awake; call it again
        /// after adding or removing a pool at runtime.
        /// </summary>
        public void Rebuild() {
            _pools.Clear();

            foreach (var pool in GetComponents<ResourcePool>()) {
                if (pool.Definition == null) continue;

                if (_pools.ContainsKey(pool.Definition)) {
                    Debug.LogWarning($"{nameof(ResourceSet)} on '{name}' has more than one pool for '{pool.Definition.name}'; keeping the first.", this);
                    continue;
                }

                _pools[pool.Definition] = pool;
            }
        }
    }
}
