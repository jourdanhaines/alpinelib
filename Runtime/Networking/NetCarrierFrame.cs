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
    /// <b>Rigid, unit-scale carriers only.</b> The conversion is the transform's own rotate-and-translate
    /// and nothing else; a scaled carrier would resize the pawn's coordinates along with its frame, and a
    /// deforming one has no single frame to convert into.
    /// </para>
    /// </remarks>
    public static class NetCarrierFrame {
        /// <summary>
        /// Expresses a world-space state in a carrier's frame: where the pawn stands on the deck, which
        /// way it faces relative to the carrier, and how fast it is moving <em>across</em> the deck.
        /// </summary>
        /// <remarks>
        /// The velocity has the carrier's own motion removed before it is rotated, so what replicates is
        /// the pawn's walking pace rather than the train's. That is the whole point of the frame: the
        /// server's gait ceiling and the interpolator's tangents then see a pawn doing what a pawn can do.
        /// </remarks>
        public static PawnState ToLocal(in PawnState world, NetCarrier carrier) {
            if (carrier == null) return world;

            Transform frame = carrier.transform;
            Vector3 position = frame.InverseTransformPoint(world.Position.ToUnity());
            Vector3 velocity = frame.InverseTransformDirection(world.Velocity.ToUnity() - carrier.Velocity);
            float yaw = WrapDegrees(world.YawDegrees - frame.eulerAngles.y);

            return new PawnState(position.ToNumerics(), yaw, velocity.ToNumerics(), world.Flags, carrier.CarrierId);
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
            Vector3 position = frame.TransformPoint(local.Position.ToUnity());
            Vector3 velocity = frame.TransformDirection(local.Velocity.ToUnity());
            float yaw = WrapDegrees(local.YawDegrees + frame.eulerAngles.y);

            return new PawnState(position.ToNumerics(), yaw, velocity.ToNumerics(), local.Flags, PawnState.WorldCarrierId);
        }

        /// <summary>Folds a yaw back into [0, 360), so a subtraction never hands out a negative facing.</summary>
        private static float WrapDegrees(float degrees) {
            float wrapped = degrees % 360f;

            return wrapped < 0f ? wrapped + 360f : wrapped;
        }
    }
}
