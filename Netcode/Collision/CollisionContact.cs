using System.Numerics;

namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// One overlap between a pawn's capsule and a piece of geometry: which way to push it out, how far,
    /// and what it hit.
    /// </summary>
    /// <remarks>
    /// The normal always points away from the shape and toward the capsule, so depenetration is
    /// <c>position += Normal * Depth</c> with no sign to get backwards. The provenance fields exist for
    /// the rider rule rather than for diagnostics: a pawn standing on a mover has to add that mover's
    /// per-tick delta to its own position before it moves, and the only way it knows which mover is by
    /// the contact — or support — that found it.
    /// </remarks>
    public struct CollisionContact {
        /// <summary>Creates a contact against a static shape.</summary>
        public CollisionContact(Vector3 normal, float depth, int shapeIndex) {
            Normal = normal;
            Depth = depth;
            ShapeIndex = shapeIndex;
            IsMover = false;
            MoverIndex = -1;
        }

        /// <summary>Creates a contact, naming the mover it came from when it came from one.</summary>
        public CollisionContact(Vector3 normal, float depth, int shapeIndex, bool isMover, int moverIndex) {
            Normal = normal;
            Depth = depth;
            ShapeIndex = shapeIndex;
            IsMover = isMover;
            MoverIndex = moverIndex;
        }

        /// <summary>Unit vector pointing out of the shape, along which the capsule must be pushed.</summary>
        public Vector3 Normal;

        /// <summary>How far the capsule has to travel along <see cref="Normal"/> to stop overlapping, in metres.</summary>
        public float Depth;

        /// <summary>Index of the shape within its owning array — the static array, or the mover's own shape.</summary>
        public int ShapeIndex;

        /// <summary>True when the shape belongs to a mover rather than to the static set.</summary>
        public bool IsMover;

        /// <summary>Index into <c>CollisionWorld.Movers</c>, or <c>-1</c> for static geometry.</summary>
        public int MoverIndex;
    }
}
