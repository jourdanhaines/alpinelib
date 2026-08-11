using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Collision {
    /// <summary>
    /// Marks a subtree of the scene as collision the shared simulation must know about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Opting geometry in rather than exporting everything is what keeps the exported world small and
    /// intentional. A level is full of colliders that exist for triggers, audio zones, camera blockers
    /// and decoration; a dedicated server has no business simulating any of them, and a shape the client
    /// exported but the server did not would be a wall only one end can see.
    /// </para>
    /// <para>
    /// Box, sphere and capsule colliders on this object and its children are collected; triggers are
    /// skipped, because a trigger is by definition not something a pawn stands on. Anything else — a mesh
    /// collider, a terrain collider — is a <b>validation failure</b> that aborts the export rather than
    /// being approximated: an approximated wall is a wall in the wrong place, and the whole point of the
    /// export is that both ends agree on where the walls are.
    /// </para>
    /// </remarks>
    public class NetStaticGeometry : MonoBehaviour {
        [Tooltip("Include colliders on inactive children. Off by default: a disabled object is not in the world the player walks through.")]
        [SerializeField] private bool includeInactiveChildren;

        private readonly List<Collider> colliderScratch = new List<Collider>();

        /// <summary>True when disabled children are exported too.</summary>
        public bool IncludeInactiveChildren => includeInactiveChildren;

        /// <summary>
        /// Appends every collider on this object and its children to <paramref name="results"/>, in a
        /// stable hierarchy order — the order becomes the exported shape index order, which is the
        /// collision iteration order on both ends.
        /// </summary>
        /// <remarks>
        /// Triggers are skipped here. Unsupported collider types are <b>not</b>: they are returned so the
        /// exporter can name them in its failure list rather than silently dropping them.
        /// </remarks>
        public void CollectColliders(List<Collider> results) {
            if (results == null) {
                return;
            }

            GetComponentsInChildren(includeInactiveChildren, colliderScratch);

            for (int colliderIndex = 0; colliderIndex < colliderScratch.Count; colliderIndex++) {
                Collider candidate = colliderScratch[colliderIndex];

                if (candidate == null || candidate.isTrigger) {
                    continue;
                }

                results.Add(candidate);
            }
        }
    }
}
