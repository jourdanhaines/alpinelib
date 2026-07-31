using System;
using AlpineLib.Actors;
using UnityEngine;

namespace AlpineLib.Perception {
    /// <summary>
    /// Line-of-sight sensor. Every fixed step it sweeps a sphere for colliders on the target layer,
    /// keeps those inside the horizontal half-angle of <c>transform.forward</c>, and confirms the
    /// first one that a raycast can actually reach.
    /// </summary>
    /// <remarks>
    /// Detection quirks worth knowing before relying on this: sight is traced from
    /// <c>transform.position + Vector3.up</c>, a fixed one metre eye height; the first candidate that
    /// passes wins, in physics buffer order rather than by distance; and <see cref="Target"/> is
    /// cleared at the start of every tick, so it reports current sight, never memory (pair it with a
    /// <see cref="TargetMemory"/> for that). The overlap buffer holds 32 colliders.
    /// </remarks>
    public class ViewCone : MonoBehaviour {
        [SerializeField] private LayerMask targetLayer;
        [SerializeField] private LayerMask obstacleLayers;
        [SerializeField] private float viewDistance = 15f;
        [SerializeField] private float viewAngle = 90f;

        /// <summary>
        /// Overrides the serialized view distance when set. Evaluated every tick, so a game can feed
        /// it from a stat sheet or any other live source.
        /// </summary>
        public Func<float> ViewDistanceProvider;

        /// <summary>
        /// Overrides the serialized view angle when set. Evaluated every tick.
        /// </summary>
        public Func<float> ViewAngleProvider;

        /// <summary>
        /// Optional extra test a candidate must pass to be seen. Runs after the built-in check that
        /// skips dead <see cref="IMortal"/> targets.
        /// </summary>
        public Func<Transform, bool> TargetFilter;

        /// <summary>
        /// What is visible right now, or null. Recomputed from scratch each fixed step.
        /// </summary>
        public Transform Target { get; private set; }

        /// <summary>
        /// How far this cone can see. Reads <see cref="ViewDistanceProvider"/> when one is set.
        /// </summary>
        public float ViewDistance {
            get => ViewDistanceProvider != null ? ViewDistanceProvider() : viewDistance;
            set => viewDistance = value;
        }

        /// <summary>
        /// Full width of the cone in degrees. Reads <see cref="ViewAngleProvider"/> when one is set.
        /// </summary>
        public float ViewAngle {
            get => ViewAngleProvider != null ? ViewAngleProvider() : viewAngle;
            set => viewAngle = value;
        }

        private static readonly Collider[] _overlapBuffer = new Collider[32];
        private const int ArcSegments = 20;

        private void FixedUpdate() {
            Target = null;

            float currentViewDistance = ViewDistance;
            float currentViewAngle = ViewAngle;

            int count = Physics.OverlapSphereNonAlloc(transform.position, currentViewDistance, _overlapBuffer, targetLayer);

            for (int i = 0; i < count; i++) {
                Transform candidate = _overlapBuffer[i].transform;
                Vector3 directionToTarget = candidate.position - transform.position;
                directionToTarget.y = 0;

                var mortal = candidate.GetComponentInParent<IMortal>();
                if (mortal != null && !mortal.IsAlive) continue;

                if (TargetFilter != null && !TargetFilter(candidate)) continue;

                if (Vector3.Angle(transform.forward, directionToTarget) > currentViewAngle * 0.5f) continue;

                int rayMask = targetLayer | obstacleLayers;
                if (Physics.Raycast(transform.position + Vector3.up, directionToTarget.normalized, out RaycastHit hit, currentViewDistance, rayMask)) {
                    if (((1 << hit.collider.gameObject.layer) & targetLayer) != 0) {
                        Target = candidate;
                        return;
                    }
                }
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos() {
            float currentViewDistance = ViewDistance;
            float currentViewAngle = ViewAngle;

            Gizmos.color = Target != null ? Color.red : new Color(1f, 1f, 0f, 0.5f);

            Vector3 origin = transform.position + Vector3.up * 0.1f;
            float halfAngle = currentViewAngle * 0.5f;

            Vector3 leftDir = Quaternion.AngleAxis(-halfAngle, Vector3.up) * transform.forward;
            Vector3 rightDir = Quaternion.AngleAxis(halfAngle, Vector3.up) * transform.forward;

            Gizmos.DrawRay(origin, leftDir * currentViewDistance);
            Gizmos.DrawRay(origin, rightDir * currentViewDistance);

            Vector3 prevPoint = origin + leftDir * currentViewDistance;
            for (int i = 1; i <= ArcSegments; i++) {
                float t = (float)i / ArcSegments;
                float angle = Mathf.Lerp(-halfAngle, halfAngle, t);
                Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * transform.forward;
                Vector3 point = origin + dir * currentViewDistance;
                Gizmos.DrawLine(prevPoint, point);
                prevPoint = point;
            }
        }
#endif
    }
}
