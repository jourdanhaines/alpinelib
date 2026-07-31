using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Perception {
    /// <summary>
    /// Static broadcast bus for world noise. Anything that makes a sound calls
    /// <see cref="Emit"/> and every <see cref="NoiseListener"/> inside the radius is notified.
    /// </summary>
    /// <remarks>
    /// Delivery is a linear scan of the listener registry with a squared-distance test, so cost is
    /// O(listeners) per emit with no spatial partitioning. That is fine for the handful of listeners
    /// a scene usually holds; a game with hundreds of them needs a broadphase instead.
    /// </remarks>
    public static class NoiseEmitter {
        private struct NoiseGizmo {
            public Vector3 Position;
            public float Radius;
            public float SpawnTime;
        }

        private const float GizmoDuration = 1f;
        private static readonly List<NoiseGizmo> _activeGizmos = new();

        /// <summary>
        /// Notifies every listener within <paramref name="radius"/> of a noise at
        /// <paramref name="position"/>, attributing it to <paramref name="source"/>.
        /// </summary>
        public static void Emit(GameObject source, Vector3 position, float radius) {
            float radiusSqr = radius * radius;

            for (int i = 0; i < NoiseListener.All.Count; i++) {
                var listener = NoiseListener.All[i];

                float distSqr = (listener.transform.position - position).sqrMagnitude;
                if (distSqr <= radiusSqr)
                    listener.OnNoiseHeard(source, position);
            }

#if UNITY_EDITOR
            _activeGizmos.Add(new NoiseGizmo {
                Position = position,
                Radius = radius,
                SpawnTime = Time.time
            });
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Draws an expanding ring per recent emit. Called once per frame by the first
        /// registered <see cref="NoiseListener"/>, since a static class has no gizmo callback.
        /// </summary>
        public static void DrawGizmos() {
            for (int i = _activeGizmos.Count - 1; i >= 0; i--) {
                var gizmo = _activeGizmos[i];
                float elapsed = Time.time - gizmo.SpawnTime;

                if (elapsed > GizmoDuration) {
                    _activeGizmos.RemoveAt(i);
                    continue;
                }

                float t = elapsed / GizmoDuration;
                float currentRadius = Mathf.Lerp(0f, gizmo.Radius, t);
                float alpha = 1f - t;

                Gizmos.color = new Color(1f, 0.5f, 0f, alpha);
                Gizmos.DrawWireSphere(gizmo.Position + Vector3.up * 0.1f, currentRadius);
            }
        }
#endif
    }
}
