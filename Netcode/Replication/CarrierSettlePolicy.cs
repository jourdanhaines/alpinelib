using System;

namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// Decides when a pawn's <em>reported</em> carrier follows the one it is physically standing on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The raw answer underneath is noisy: a ground probe standing across the gap between two coupled
    /// cars picks whichever collider the physics engine reported last and can alternate every frame, and
    /// each alternation reported would be a frame change the server accepts unmeasured — spending a
    /// budget that exists to bound a cheat while the player is doing nothing but walking. So a change is
    /// held for <see cref="DwellSeconds"/> before it is reported, and the timer restarts whenever the
    /// live answer changes, which is what makes an alternating contact never report at all.
    /// </para>
    /// <para>
    /// <b>The dwell costs dwell × relative speed.</b> While a pawn dwells it replicates in the frame it
    /// is leaving, so its reported position drifts by however fast the two frames are moving apart. Two
    /// coupled cars move together and the dwell is free. Boarding or leaving a moving consist it is the
    /// consist's whole speed, and every tick of that is measured against a walking gait as a rejection.
    /// Above <see cref="ImmediateRelativeSpeed"/> the change is therefore reported at once: below it the
    /// whole dwell drifts at most <see cref="MovementValidator.PositionSlackMetres"/>, which the
    /// validator forgives by construction, and frames moving apart faster than that cannot be the
    /// coupler flicker the dwell exists to hide.
    /// </para>
    /// </remarks>
    public sealed class CarrierSettlePolicy {
        /// <summary>
        /// How long a new live answer must hold before it is reported when the two frames move together.
        /// </summary>
        /// <remarks>
        /// Three ticks of a thirty-hertz server. A budget's worth of dwells has to outlast the server's
        /// frame-change window — <c>CarrierSwitchCooldownTicks / (ServerTickRate ×
        /// MaxCarrierSwitchesPerWindow)</c>, 8 / 90 = 0.089 s — and it cannot go to zero, because a hop
        /// or a step over a rail breaks ground contact for a handful of frames without the rider ever
        /// leaving the deck; see <see cref="MovementValidator.MaxCarrierSwitchesPerWindow"/>.
        /// </remarks>
        public const float DwellSeconds = 0.1f;

        /// <summary>
        /// Relative speed of the two frames above which a change is reported without dwelling. Sized so
        /// that a full dwell under it drifts no further than the validator's slack.
        /// </summary>
        public const float ImmediateRelativeSpeed = MovementValidator.PositionSlackMetres / DwellSeconds;

        private ushort settlingTo;
        private float settlingFor;

        /// <summary>The carrier currently on the wire; <see cref="PawnState.WorldCarrierId"/> for the world.</summary>
        public ushort ReportedCarrierId { get; private set; }

        /// <summary>
        /// Adopts a carrier outright and clears any dwell in progress: a spawn placement, or a game that
        /// placed the pawn by its own hand.
        /// </summary>
        public void Reset(ushort carrierId) {
            ReportedCarrierId = carrierId;
            settlingTo = carrierId;
            settlingFor = 0f;
        }

        /// <summary>
        /// Advances the dwell by one frame of the live answer.
        /// </summary>
        /// <param name="liveCarrierId">The carrier the pawn is physically on this frame.</param>
        /// <param name="relativeSpeed">How fast the reported frame and the live one are moving apart.</param>
        /// <param name="deltaSeconds">Frame time.</param>
        public void Advance(ushort liveCarrierId, float relativeSpeed, float deltaSeconds) {
            if (liveCarrierId != settlingTo) {
                settlingTo = liveCarrierId;
                settlingFor = 0f;
            }

            if (liveCarrierId == ReportedCarrierId) return;

            if (relativeSpeed > ImmediateRelativeSpeed) {
                Reset(liveCarrierId);
                return;
            }

            settlingFor += Math.Max(deltaSeconds, 0f);
            if (settlingFor < DwellSeconds) return;

            Reset(liveCarrierId);
        }
    }
}
