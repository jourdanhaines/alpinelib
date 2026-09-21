using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Replication;

namespace AlpineLib.Netcode.Sessions.Spawning {
    /// <summary>
    /// The support query every placement drops its pawns onto, and the standing state it builds from the
    /// answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A spawn place is authored in the XZ plane and knows nothing about the scene, so the height comes
    /// from a support query instead of a constant. Spawning at the nominal plane in a scene whose floor
    /// is a metre up means a pawn standing inside the floor until the motor pushes it out; in a scene
    /// whose floor is below it, one that falls for a moment. Both are visible on every client.
    /// </para>
    /// <para>
    /// The reach up is deliberately short. The probe takes the highest surface it crosses, so a ceiling
    /// starts counting as a floor the moment the span reaches it. The reach down is long, because a floor
    /// authored below the nominal plane is ordinary and a spawn that misses it drops the pawn through the
    /// world. A place over a hole finds no support and keeps its nominal height, which is the same guess
    /// the caller made.
    /// </para>
    /// </remarks>
    public readonly struct SpawnGroundProbe {
        /// <summary>How far above the nominal spawn plane the probe starts, in metres.</summary>
        public const float DefaultAboveMetres = 2f;

        /// <summary>How far below the nominal spawn plane the probe reaches, in metres.</summary>
        public const float DefaultBelowMetres = 20f;

        private readonly float _aboveMetres;
        private readonly float _belowMetres;

        /// <summary>Builds a probe with the given reach either side of the nominal plane.</summary>
        public SpawnGroundProbe(float aboveMetres, float belowMetres) {
            _aboveMetres = aboveMetres;
            _belowMetres = belowMetres;
        }

        /// <summary>A grounded, walking, world-space pawn state dropped onto whatever the scene puts under it.</summary>
        public PawnState BuildStandingState(CollisionWorld world, Vector3 nominalPosition, float yawDegrees, uint serverTick) {
            float height = ResolveHeight(world, nominalPosition.X, nominalPosition.Z, nominalPosition.Y, serverTick);
            var position = new Vector3(nominalPosition.X, height, nominalPosition.Z);
            byte flags = PawnState.PackFlags(WireLocomotion.Walk, false, true);
            return new PawnState(position, yawDegrees, Vector3.Zero, flags);
        }

        /// <summary>Height of the floor under a spawn place, or the nominal height where the scene offers none.</summary>
        public float ResolveHeight(CollisionWorld world, float x, float z, float nominalHeight, uint serverTick) {
            // A fallback world's floor is invented; the authored height is the only real one on offer.
            if (world == null || world.IsFallback) {
                return nominalHeight;
            }

            bool supported = world.TryGetSupport(
                x,
                z,
                nominalHeight + _aboveMetres,
                nominalHeight - _belowMetres,
                serverTick,
                out SupportHit hit);

            return supported ? hit.Height : nominalHeight;
        }
    }
}
