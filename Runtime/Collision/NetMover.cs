using System.Collections.Generic;
using AlpineLib.Netcode.Collision;
using UnityEngine;

namespace AlpineLib.Collision {
    /// <summary>
    /// Authors one moving platform: an identity, a route drawn with child transforms, a speed and the box
    /// that rides along it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The waypoints are this object's direct children, in sibling order, so a designer builds a route by
    /// dragging empties around and sees it in the scene view rather than by typing coordinates into a
    /// list. The component itself never moves anything at runtime: the platform's pose is a pure function
    /// of the simulation tick evaluated from the exported <see cref="MoverPath"/>, on both the server and
    /// every client, which is the only way a pawn can stand on it while its own motion is being predicted
    /// and rewound.
    /// </para>
    /// <para>
    /// <b>v1 movers translate only.</b> A rotating platform would have to rotate its rider too, which
    /// turns the rider rule from one vector add into a frame transform and makes every reconciliation
    /// replay reproduce that transform exactly. Rotation is deliberately deferred, and the exporter
    /// ignores this object's rotation rather than pretending to honour it.
    /// </para>
    /// </remarks>
    public class NetMover : MonoBehaviour {
        [Header("Identity")]
        [Tooltip("Scene-unique id. Travels as the replicated entity's AuxId and is how a client matches this platform to its path.")]
        [SerializeField] private ushort moverId = 1;
        [Tooltip("Row in the net prefab registry clients instantiate for this platform.")]
        [SerializeField] private ushort prefabId = 1;

        [Header("Path")]
        [Tooltip("Travel speed in metres per second. Must be greater than zero.")]
        [SerializeField] private float speed = 1f;
        [Tooltip("What happens at the end of the waypoint list.")]
        [SerializeField] private MoverLoopMode loopMode = MoverLoopMode.PingPong;
        [Tooltip("Offset into the cycle in ticks, so identical platforms can run out of step.")]
        [SerializeField] private uint phaseTicks;

        [Header("Collision")]
        [Tooltip("Half-size of the platform's collision box, in metres. Authored around the mover's own origin.")]
        [SerializeField] private Vector3 boxHalfExtents = new Vector3(1.5f, 0.1f, 1.5f);

        /// <summary>Scene-unique id; the replicated entity's <c>AuxId</c>.</summary>
        public ushort MoverId => moverId;

        /// <summary>Prefab registry row clients instantiate for this platform.</summary>
        public ushort PrefabId => prefabId;

        /// <summary>Travel speed in metres per second.</summary>
        public float Speed => speed;

        /// <summary>What happens at the end of the waypoint list.</summary>
        public MoverLoopMode LoopMode => loopMode;

        /// <summary>Offset into the cycle, in ticks.</summary>
        public uint PhaseTicks => phaseTicks;

        /// <summary>Half-size of the collision box, authored around this object's origin.</summary>
        public Vector3 BoxHalfExtents => boxHalfExtents;

        /// <summary>How many waypoints the route has. Fewer than two is an export failure.</summary>
        public int WaypointCount => transform.childCount;

        /// <summary>World position of one waypoint, in sibling order.</summary>
        public Vector3 GetWaypoint(int waypointIndex) {
            return transform.GetChild(waypointIndex).position;
        }

        /// <summary>Appends every waypoint's world position to <paramref name="results"/>, in sibling order.</summary>
        public void CollectWaypoints(List<Vector3> results) {
            if (results == null) {
                return;
            }

            for (int waypointIndex = 0; waypointIndex < transform.childCount; waypointIndex++) {
                results.Add(transform.GetChild(waypointIndex).position);
            }
        }

        private void OnDrawGizmos() {
            DrawRoute(new Color(0.2f, 0.8f, 1f, 0.6f));
        }

        private void OnDrawGizmosSelected() {
            DrawRoute(new Color(0.2f, 0.8f, 1f, 1f));
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(transform.position, boxHalfExtents * 2f);
        }

        private void DrawRoute(Color color) {
            if (transform.childCount < 2) {
                return;
            }

            Gizmos.color = color;

            for (int waypointIndex = 0; waypointIndex < transform.childCount - 1; waypointIndex++) {
                Gizmos.DrawLine(GetWaypoint(waypointIndex), GetWaypoint(waypointIndex + 1));
            }

            if (loopMode == MoverLoopMode.Loop) {
                Gizmos.DrawLine(GetWaypoint(transform.childCount - 1), GetWaypoint(0));
            }
        }
    }
}
