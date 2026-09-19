using UnityEngine;

namespace AlpineLib.Actors.Locomotion {
    /// <summary>
    /// A world-upright capsule described from its foot point: the bottom of the lower hemisphere.
    /// </summary>
    /// <remarks>
    /// Foot-origin is the convention every pose in the motor uses, so a capsule standing on the floor
    /// has its foot at floor height and its segment ends one radius in from either end. A shrunk copy
    /// keeps the same segment and pulls its surface in by the skin on every side, so a sweep with it
    /// that stops a skin short of geometry leaves the real capsule exactly touching. The Unity twin of
    /// the engine-free <c>CapsulePose</c> in the netcode assembly.
    /// </remarks>
    public readonly struct CapsuleShape {
        public readonly float Radius;
        public readonly float Height;

        /// <summary>Metres between the foot point and the lowest point of this shape's surface.</summary>
        public readonly float Inset;

        public CapsuleShape(float radius, float height) : this(radius, height, 0f) { }

        private CapsuleShape(float radius, float height, float inset) {
            Radius = Mathf.Max(radius, 0.001f);
            Height = Mathf.Max(height, Radius * 2f + inset * 2f);
            Inset = Mathf.Max(inset, 0f);
        }

        /// <summary>Centre of the lower hemisphere for a capsule whose foot is at the given point.</summary>
        public Vector3 SegmentBottom(Vector3 foot) {
            return foot + Vector3.up * (Inset + Radius);
        }

        /// <summary>Centre of the upper hemisphere for a capsule whose foot is at the given point.</summary>
        public Vector3 SegmentTop(Vector3 foot) {
            return foot + Vector3.up * (Height - Inset - Radius);
        }

        /// <summary>The same segment with its surface pulled in by the skin on every side.</summary>
        public CapsuleShape Shrunk(float skin) {
            float clamped = Mathf.Clamp(skin, 0f, Radius - 0.001f);
            return new CapsuleShape(Radius - clamped, Height, Inset + clamped);
        }
    }
}
