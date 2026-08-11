using System.Numerics;

namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// The surface a pawn is standing on, as found by a vertical support query: how high it is, which way
    /// it faces, and what it belongs to.
    /// </summary>
    /// <remarks>
    /// One query answers three questions the motor would otherwise ask separately — is the pawn grounded,
    /// what height should it be clamped to, and is there a ledge low enough to step onto — because they
    /// are all the same question asked over a vertical span. <see cref="Normal"/> is what decides
    /// walkability: a face steeper than the profile's slope limit is a wall the pawn slides down, not a
    /// floor it stands on, and the motor compares against <c>MathF.Cos(slopeLimit)</c> rather than taking
    /// an inverse cosine of the normal.
    /// </remarks>
    public struct SupportHit {
        /// <summary>Creates a support hit on static geometry.</summary>
        public SupportHit(float height, Vector3 normal, int shapeIndex) {
            Height = height;
            Normal = normal;
            ShapeIndex = shapeIndex;
            IsMover = false;
            MoverIndex = -1;
        }

        /// <summary>Creates a support hit, naming the mover it came from when it came from one.</summary>
        public SupportHit(float height, Vector3 normal, int shapeIndex, bool isMover, int moverIndex) {
            Height = height;
            Normal = normal;
            ShapeIndex = shapeIndex;
            IsMover = isMover;
            MoverIndex = moverIndex;
        }

        /// <summary>World height of the surface under the probe, in metres.</summary>
        public float Height;

        /// <summary>Upward-facing unit normal of the surface at that point.</summary>
        public Vector3 Normal;

        /// <summary>Index of the shape within its owning array.</summary>
        public int ShapeIndex;

        /// <summary>True when the surface belongs to a mover rather than to the static set.</summary>
        public bool IsMover;

        /// <summary>Index into <c>CollisionWorld.Movers</c>, or <c>-1</c> for static geometry.</summary>
        public int MoverIndex;
    }
}
