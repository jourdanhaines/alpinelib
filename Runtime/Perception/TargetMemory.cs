using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Perception {
    /// <summary>
    /// Stores the last known position of targets so an AI can keep pursuing something it can no
    /// longer see.
    /// </summary>
    /// <remarks>
    /// Entries never expire and destroyed transforms stay in the table until
    /// <see cref="Forget"/> or <see cref="ForgetAll"/> is called.
    /// </remarks>
    public class TargetMemory : MonoBehaviour {
        private readonly Dictionary<Transform, Vector3> _memories = new();

        /// <summary>
        /// Records the target's current position as its last known position.
        /// </summary>
        public void Remember(Transform target) {
            _memories[target] = target.position;
        }

        /// <summary>
        /// Returns the target's last known position, or null if nothing is remembered about it.
        /// </summary>
        public Vector3? Recall(Transform target) {
            if (target == null) return null;
            if (_memories.TryGetValue(target, out Vector3 position))
                return position;
            return null;
        }

        /// <summary>
        /// Drops what is remembered about a single target.
        /// </summary>
        public void Forget(Transform target) {
            _memories.Remove(target);
        }

        /// <summary>
        /// Drops every remembered target.
        /// </summary>
        public void ForgetAll() {
            _memories.Clear();
        }
    }
}
