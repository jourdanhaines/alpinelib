using AlpineLib.Netcode.Replication;
using UnityEngine;

namespace AlpineLib.Networking {
    /// <summary>
    /// Converts a pawn state between world space and a carrier's local frame — the two halves of the
    /// carrier-relative contract, in one place so they cannot drift apart.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pure and static on purpose: this is the only maths in the carrier path, one owner samples it and
    /// every observer inverts it, and any asymmetry between the two shows up as a rider sliding down the
    /// deck. Both directions read the carrier's transform at the moment they are called, so the caller's
    /// execution order decides which frame's pose they see — riders are synced after
    /// <see cref="NetExecutionOrder.Carriers"/> for exactly that reason.
    /// </para>
    /// <para>
    /// <b>Rigid, unit-scale, upright-or-pitched carriers only.</b> Both directions are rotation and
    /// translation only — never <c>TransformPoint</c>, which folds the carrier's scale into the position
    /// while a velocity rotated by the same transform keeps world units, and hands the server a scaled
    /// displacement to measure against an unscaled gait ceiling. <see cref="NetCarrier"/> refuses to
    /// register a scaled object for that reason.
    /// </para>
    /// <para>
    /// <b>Why a yaw is enough, and where it stops being.</b> Yaw is read off <c>eulerAngles.y</c>, which
    /// under Unity's ZXY decomposition is the exact heading of the carrier's forward axis for any pitch
    /// and any bank — the roll lands in <c>z</c> and leaves <c>y</c> alone — so the round trip through a
    /// graded <em>or</em> canted deck is exact to within a wire quantization step. What a bank breaks is
    /// not this arithmetic but the pose it is carried in: a rider on a canted deck stands tilted, and a
    /// single yaw cannot say so, so what replicates is a rider drawn upright on a deck that is not. The
    /// one arithmetic degeneracy is a carrier pitched to ±90°, where the decomposition has no heading
    /// left to name. A carrier whose riders need more than a heading needs more than this frame.
    /// </para>
    /// <para>
    /// A null carrier means world space in both directions, but they are not symmetric about it:
    /// <see cref="ToLocal"/> hands the state back unrelabelled, because a pawn its game reports no
    /// carrier for is already a world-frame state, while <see cref="ToWorld"/> relabels to
    /// <see cref="PawnState.WorldCarrierId"/>, because its caller has just decided to treat the numbers
    /// as world space and the label must say so. An <em>unregistered</em> carrier counts as no carrier
    /// for the same reason: <see cref="TryToWorld"/> asks the registry, so a label the registry cannot
    /// invert would put deck-local metres on the wire that no peer — the sender included — could ever
    /// convert back.
    /// </para>
    /// </remarks>
    public static class NetCarrierFrame {
        /// <summary>
        /// Puts a sampled or corrected state into world space, resolving the carrier it names through
        /// <see cref="NetCarrierRegistry"/>.
        /// </summary>
        /// <remarks>
        /// The one entry point every consumer of an inbound state goes through, so that no reader can
        /// mistake deck-local metres for a place in the world. Failure is reported rather than papered
        /// over: a state naming a carrier this client has not loaded yet — the window between a join's
        /// first snapshot and the consist being built, or a car streamed out mid-session — has no world
        /// pose at all, and the caller must hold what it already had instead of writing the deck-local
        /// numbers into a transform.
        /// </remarks>
        /// <returns>False when the state named a carrier no loaded object answers to.</returns>
        public static bool TryToWorld(in PawnState state, out PawnState world) {
            if (!state.IsCarrierRelative) {
                world = state;
                return true;
            }

            if (!NetCarrierRegistry.TryResolve(state.CarrierId, out NetCarrier carrier)) {
                world = state;
                return false;
            }

            world = ToWorld(in state, carrier);
            return true;
        }

        /// <summary>
        /// Expresses a world-space state in a carrier's frame: where the pawn stands on the deck, which
        /// way it faces relative to the carrier, and how fast it is moving <em>across</em> the deck.
        /// </summary>
        /// <remarks>
        /// The velocity has the carrier's own motion removed before it is rotated, so what replicates is
        /// the pawn's walking pace rather than the train's. That is the whole point of the frame: the
        /// server's gait ceiling and the interpolator's tangents then see a pawn doing what a pawn can do.
        ///
        /// A carrier the registry does not resolve to itself — the unassigned id, a duplicate, a refused
        /// scale — is treated as no carrier and the world state is handed straight back. Moving numbers
        /// into a frame nobody can name is strictly worse than leaving them in the one everybody shares.
        /// </remarks>
        public static PawnState ToLocal(in PawnState world, NetCarrier carrier) {
            if (carrier == null) return world;
            if (carrier.CarrierId == PawnState.WorldCarrierId) return world;
            if (!carrier.IsRegistered) return world;

            Transform frame = carrier.transform;
            Quaternion intoFrame = Quaternion.Inverse(frame.rotation);
            Vector3 position = intoFrame * (world.Position.ToUnity() - frame.position);
            Vector3 velocity = intoFrame * (world.Velocity.ToUnity() - carrier.Velocity);
            float yaw = WrapDegrees(world.YawDegrees - frame.eulerAngles.y);

            return new PawnState(position.ToNumerics(), yaw, velocity.ToNumerics(), world.Flags, carrier.CarrierId, world.LookPitchDegrees);
        }

        /// <summary>
        /// Puts a carrier-relative state back into world space, ready to be drawn.
        /// </summary>
        /// <remarks>
        /// The carrier's own velocity is deliberately <em>not</em> added back. What comes out is the
        /// rider's own motion, rotated into world axes, because that is what every consumer of a
        /// replicated velocity wants: the animator picks legs from it, the interpolator builds tangents
        /// from it, and a standing rider on a moving train must read as standing still in all of them.
        /// The ride itself reaches the screen through the carrier's transform, which the position has
        /// just been carried through.
        /// </remarks>
        public static PawnState ToWorld(in PawnState local, NetCarrier carrier) {
            if (carrier == null) return local.WithCarrier(PawnState.WorldCarrierId);

            Transform frame = carrier.transform;
            Quaternion outOfFrame = frame.rotation;
            Vector3 position = frame.position + outOfFrame * local.Position.ToUnity();
            Vector3 velocity = outOfFrame * local.Velocity.ToUnity();
            float yaw = WrapDegrees(local.YawDegrees + frame.eulerAngles.y);

            return new PawnState(position.ToNumerics(), yaw, velocity.ToNumerics(), local.Flags, PawnState.WorldCarrierId, local.LookPitchDegrees);
        }

        /// <summary>Folds a yaw back into [0, 360), so a subtraction never hands out a negative facing.</summary>
        private static float WrapDegrees(float degrees) {
            float wrapped = degrees % 360f;

            return wrapped < 0f ? wrapped + 360f : wrapped;
        }
    }
}
