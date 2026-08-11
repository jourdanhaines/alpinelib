using System.Numerics;

namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// Where a pawn's collision capsule is this instant: the point its feet touch, plus the dimensions it
    /// was authored with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Anchoring at the feet rather than the centre is deliberate. <c>PawnState.Position</c> is a foot
    /// position — that is what the ground clamp writes and what every view places a mesh at — so a pose
    /// built from a state involves no conversion at all, and no chance of one end of the wire halving a
    /// height where the other did not.
    /// </para>
    /// <para>
    /// The capsule's inner segment runs from <c>FootPosition + (0, Radius, 0)</c> to
    /// <c>FootPosition + (0, Height - Radius, 0)</c>. A capsule shorter than twice its radius degenerates
    /// to a sphere, which the resolver handles without a special case because the segment simply
    /// collapses; the config validator still rejects such dimensions as an authoring mistake.
    /// </para>
    /// </remarks>
    public readonly struct CapsulePose {
        /// <summary>Creates a pose from a foot position and the pawn's capsule dimensions.</summary>
        public CapsulePose(Vector3 footPosition, float radius, float height) {
            FootPosition = footPosition;
            Radius = radius;
            Height = height;
        }

        /// <summary>World position of the capsule's lowest point.</summary>
        public Vector3 FootPosition { get; }

        /// <summary>Capsule radius in metres, from <c>MovementProfile.CapsuleRadius</c>.</summary>
        public float Radius { get; }

        /// <summary>Total capsule height in metres, from <c>MovementProfile.CapsuleHeight</c>.</summary>
        public float Height { get; }

        /// <summary>Lower end of the inner segment: one radius above the feet.</summary>
        public Vector3 SegmentBottom => new Vector3(FootPosition.X, FootPosition.Y + Radius, FootPosition.Z);

        /// <summary>Upper end of the inner segment: one radius below the crown.</summary>
        public Vector3 SegmentTop => new Vector3(FootPosition.X, FootPosition.Y + Height - Radius, FootPosition.Z);

        /// <summary>The same capsule with its feet somewhere else. Used as the motor substeps.</summary>
        public CapsulePose WithFootPosition(Vector3 footPosition) {
            return new CapsulePose(footPosition, Radius, Height);
        }
    }
}
