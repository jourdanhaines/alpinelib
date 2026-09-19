using UnityEngine;

namespace AlpineLib.Actors.Locomotion {
    /// <summary>
    /// What a capsule sweep touched: where, which way the surface faces, how far the sweep travelled,
    /// and the carrier candidate the surface belongs to.
    /// </summary>
    /// <remarks>
    /// <see cref="Body"/> is the transform a rider would be parented under — the kinematic body the
    /// surface is attached to — or null for static geometry and anything a pawn may not ride. No
    /// collider leaks into the motor, so the motor stays a pure function of poses and hits.
    /// </remarks>
    public struct CollisionSweepHit {
        public Vector3 Point;
        public Vector3 Normal;
        public float Distance;
        public Transform Body;
    }
}
