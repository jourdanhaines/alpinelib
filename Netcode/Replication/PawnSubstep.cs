using System.Numerics;

namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// Where one motor substep began, where its move put the pawn before any push-out, and the velocity it moved
    /// with: what the corner resolution restarts from or falls back to.
    /// </summary>
    internal readonly struct PawnSubstep {
        public PawnSubstep(Vector3 start, Vector3 moved, Vector3 movedVelocity) {
            Start = start;
            Moved = moved;
            MovedVelocity = movedVelocity;
        }

        /// <summary>The position before the substep's move.</summary>
        public readonly Vector3 Start;

        /// <summary>The position after the move, before any push-out.</summary>
        public readonly Vector3 Moved;

        /// <summary>The velocity the move was made with.</summary>
        public readonly Vector3 MovedVelocity;
    }
}
