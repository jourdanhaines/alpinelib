using UnityEngine;

namespace AlpineLib.Actors.Locomotion {
    /// <summary>
    /// The collision questions <see cref="CapsuleMotor"/> asks, at explicit poses, of whatever world it
    /// is stepping through.
    /// </summary>
    /// <remarks>
    /// The engine seam: the Unity backend answers from PhysX, and an engine-free backend over the netcode
    /// collision world can answer the same questions for a server-side step. Every pose is a foot point
    /// (see <see cref="CapsuleShape"/>); nothing here reads or writes a transform.
    /// </remarks>
    public interface ICapsuleCollisionQuery {
        /// <summary>Sweeps the capsule from a foot point along a unit direction, reporting the nearest hit.</summary>
        bool Sweep(in CapsuleShape shape, Vector3 foot, Vector3 direction, float distance, out CollisionSweepHit hit);

        /// <summary>Finds the translation that separates the capsule from everything it overlaps.</summary>
        bool TryResolveOverlap(in CapsuleShape shape, Vector3 foot, out Vector3 correction);

        /// <summary>
        /// The normal of the first surface below a point, within a distance. What a sweep's contact
        /// normal cannot say when the capsule's rounded end touches an edge: which way the face behind
        /// that edge actually points.
        /// </summary>
        bool TryProbeSurface(Vector3 origin, float distance, out Vector3 normal);
    }
}
