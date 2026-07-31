using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Perception {
    /// <summary>
    /// Receives noises broadcast through <see cref="NoiseEmitter"/> while enabled.
    /// </summary>
    public class NoiseListener : MonoBehaviour {
        /// <summary>
        /// Every enabled listener, in registration order. Read by <see cref="NoiseEmitter.Emit"/>.
        /// </summary>
        public static readonly List<NoiseListener> All = new();

        /// <summary>
        /// Raised when a noise reaches this listener, with the emitting object and its world position.
        /// </summary>
        public event Action<GameObject, Vector3> NoiseHeard;

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        /// <summary>
        /// Delivers a noise to this listener. Called by <see cref="NoiseEmitter"/>.
        /// </summary>
        public void OnNoiseHeard(GameObject source, Vector3 position) {
            NoiseHeard?.Invoke(source, position);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos() {
            if (All.Count > 0 && All[0] == this)
                NoiseEmitter.DrawGizmos();
        }
#endif
    }
}
