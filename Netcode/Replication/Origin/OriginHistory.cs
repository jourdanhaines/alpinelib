using System.Numerics;

namespace AlpineLib.Netcode.Replication.Origin {
    /// <summary>
    /// The current origin and the one before it — the two frames a received world-frame state may have
    /// been written in and still be usable.
    /// </summary>
    /// <remarks>
    /// Unreliable traffic sent just before a rebase can land just after it, so a state stamped with the
    /// previous epoch is translated rather than refused. Anything older is two rebases stale — hundreds of
    /// metres of drift on a sane threshold — and is refused rather than guessed at.
    /// </remarks>
    public sealed class OriginHistory {
        /// <summary>The origin states are written in now.</summary>
        public SessionOrigin Current { get; private set; } = SessionOrigin.Initial;

        /// <summary>The origin before <see cref="Current"/>; meaningful only while <see cref="HasShifted"/>.</summary>
        public SessionOrigin Previous { get; private set; } = SessionOrigin.Initial;

        /// <summary>True once any rebase has been adopted.</summary>
        public bool HasShifted { get; private set; }

        /// <summary>Adopts <paramref name="next"/> and answers what to add to positions written in the old origin.</summary>
        public Vector3 Advance(in SessionOrigin next) {
            Vector3 delta = SessionOrigin.Delta(Current, in next);
            Previous = Current;
            Current = next;
            HasShifted = true;
            return delta;
        }

        /// <summary>
        /// What to add to a world-frame position stamped with <paramref name="epoch"/> to bring it into
        /// <see cref="Current"/>. False for an epoch that is neither current nor the one before.
        /// </summary>
        public bool TryResolveDelta(ushort epoch, out Vector3 deltaToCurrent) {
            deltaToCurrent = Vector3.Zero;

            if (epoch == Current.Epoch) {
                return true;
            }

            if (!HasShifted || epoch != Previous.Epoch) {
                return false;
            }

            deltaToCurrent = SessionOrigin.Delta(Previous, Current);
            return true;
        }

        /// <summary>
        /// Brings a received state into <see cref="Current"/>. Carrier-relative states are frame-free and
        /// pass whatever their stamp; a world-frame state from an unusable epoch returns false.
        /// </summary>
        public bool TryBringToCurrent(ushort epoch, in PawnState state, out PawnState current) {
            current = state;

            if (state.IsCarrierRelative) {
                return true;
            }

            if (!TryResolveDelta(epoch, out Vector3 delta)) {
                return false;
            }

            current = state.WithOriginShift(delta);
            return true;
        }
    }
}
