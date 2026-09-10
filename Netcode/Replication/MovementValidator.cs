using System;
using System.Numerics;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// The server's check on a client that simulates its own pawn: did this move fit inside the gait the
    /// client claims to be in?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only <see cref="AuthorityMode.OwnerClient"/> needs this. In the default server-authoritative mode
    /// the client never reports a position at all, so there is nothing to validate — the motor cannot
    /// produce an illegal state in the first place.
    /// </para>
    /// <para>
    /// <b>Gait-aware, not speed-aware.</b> A single global cap has to be set to the fastest thing anyone
    /// can do, which means a sprint-speed ceiling applied to a crouching player — useless. Keying off the
    /// locomotion bits the client itself reported closes that: claiming a sprint to buy the sprint
    /// ceiling is visible to everyone, because those same bits drive the animation everyone can see.
    /// </para>
    /// <para>
    /// The gait checked is the <em>faster</em> of the two states' gaits. Taking only the new one would
    /// let a client sprint for a tick and relabel it as a crouch on arrival; taking only the old one
    /// would punish the honest tick where a player starts sprinting.
    /// </para>
    /// <para>
    /// <b>The trust boundary at a frame change.</b> Displacement only means anything when both states
    /// share an origin, so the tick a pawn boards or leaves a carrier is accepted unmeasured: the
    /// numbers either side are metres from different points, and subtracting them would read a step onto
    /// a train fifty metres down the track as a fifty-metre teleport. That is a real hole, and it is not
    /// one free move — it is one <em>unmeasured</em> move, of any size, and repeated on every tick it is
    /// unbounded travel at no speed the server ever sees. It is taken deliberately, because the
    /// alternative is rejecting every legitimate boarding, but it is bounded by frequency: the server
    /// does not know where any carrier is, so frequency is the only thing here it can judge.
    /// </para>
    /// <para>
    /// <b>A budget, not a bar.</b> The caller keeps a per-pawn window of
    /// <see cref="CarrierSwitchCooldownSeconds"/> and counts the frame changes landing inside it. The
    /// first <see cref="MaxCarrierSwitchesPerWindow"/> are accepted unmeasured; anything past that is
    /// rejected outright, holding the previous state — carrier included — so the pawn stays
    /// self-consistent and the client is told. A flat "no second change inside the window" bar was tried
    /// first and it punished honest play: walking across a coupler from one car to the next is two frame
    /// changes in quick succession, and a hop off a deck and back is another two, so real riders were
    /// corrected for several ticks running while a cheat merely switched more slowly. A budget refuses
    /// only the alternation nobody produces by walking — and honest play is kept away from it from the
    /// other end too, because <c>INetCarrierSource</c> owes this side hysteresis and a source that
    /// settles before it reports leaves a slot of the budget unspent.
    /// </para>
    /// <para>
    /// <b>The other unmeasured move.</b> An owner that stopped reporting because it had nothing truthful
    /// to say comes back with <c>OwnerPawnUpdate.ResyncFlag</c> set, and that resumption is accepted on
    /// the same terms and out of the same budget as a frame change: unmeasured, bounded by frequency, and
    /// never free. It is the same trust boundary seen from the time axis rather than the space one — the
    /// two poses either side belong to different stories rather than different origins — so the exposure
    /// below covers it unchanged.
    /// </para>
    /// <para>
    /// What the hole cannot buy, either way, is speed: every tick that keeps the same carrier is
    /// validated exactly as before, in that carrier's frame, where the gait ceiling is the pawn's own
    /// walking pace and the carrier's motion is not part of the measurement at all.
    /// </para>
    /// <para>
    /// <b>The remaining exposure, in full.</b> Distance is one half of it: a client alternating frames as
    /// fast as the budget allows still buys <see cref="MaxCarrierSwitchesPerWindow"/> unmeasured moves
    /// per window, of any size. The other half is not distance at all. The server never learns whether a
    /// claimed carrier id names anything, so a client can claim one no object in the session holds; every
    /// observer's <c>NetController</c> then fails to resolve the frame and holds that pawn's last pose
    /// indefinitely, while the cheat's own client draws itself wherever it likes — a pawn that is a
    /// stationary decoy to everyone but its owner. Closing that needs the server to know where carriers
    /// are, which it deliberately does not; until then it is a co-operative-play trust limitation, and
    /// <c>NetController.IsCarrierUnresolved</c> is what a game watches to see it happening.
    /// </para>
    /// </remarks>
    public sealed class MovementValidator {
        /// <summary>
        /// Fixed slack in metres added to every allowance, on top of the multiplier. Absorbs the
        /// position quantization and the sub-tick timing jitter that no multiplier should have to cover,
        /// and matters most at low speeds where a multiplier of a small number is still a small number.
        /// </summary>
        public const float PositionSlackMetres = 0.05f;

        /// <summary>
        /// How many times the allowed distance a move may cover and still be clamped rather than thrown
        /// away. Beyond this it is not jitter or a slope, it is a teleport.
        /// </summary>
        public const float RejectDistanceRatio = 3f;

        /// <summary>
        /// Length of the window the caller counts a pawn's frame changes over. Long enough that repeating
        /// the frame-change trick costs an order of magnitude in travel, short enough that the awkward
        /// honest cases — a coupler crossing, a hop off a deck and back — fit inside one window's budget
        /// rather than being spread across two.
        /// </summary>
        public const float CarrierSwitchCooldownSeconds = 0.25f;

        /// <summary>
        /// How many moves one pawn may have accepted unmeasured inside a single
        /// <see cref="CarrierSwitchCooldownSeconds"/> window before the rest are rejected: frame changes,
        /// and the owner resyncs charged alongside them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Three of the four are what a conforming source produces at its fastest, and they fill those
        /// three slots exactly. The window is eight ticks of a thirty-hertz clock and
        /// <c>NetCarrier.SourceHysteresisSeconds</c> is three of them, so a source honouring the dwell
        /// lands changes on ticks 0, 3 and 6 — all inside one window, which only reopens at elapsed ≥ 8 —
        /// and that is the honest burst itself, a hop off a deck and back or a walk across a coupler,
        /// seen at the dwell's own cadence. There is no margin against honest play in those three, which
        /// is the fact a future reader needs: the count cannot be lowered without lengthening the dwell
        /// first.
        /// </para>
        /// <para>
        /// The fourth is the margin, and it has a claimant. An owner's resync after a withheld silence is
        /// charged against this same budget — see <c>ServerReplication.HandleOwnerPawnUpdate</c> — and a
        /// resync arrives exactly when a rider is boarding or alighting, which is when the burst above is
        /// already in flight. At three the resync would be refused, measured against a walking gait over
        /// the gap, and the player corrected for walking: the correction storm this budget exists to
        /// avoid. It doubles as headroom for a source the library cannot see honouring the dwell at all.
        /// Anything past four inside a quarter of a second is not walking.
        /// </para>
        /// </remarks>
        public const int MaxCarrierSwitchesPerWindow = 4;

        private readonly NetConfig config;

        public MovementValidator(NetConfig config) {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>The configuration this validator reads its tolerance and movement profiles from.</summary>
        public NetConfig Config => config;

        /// <summary>
        /// <see cref="CarrierSwitchCooldownSeconds"/> in server ticks, rounded up, never less than one.
        /// The length of the window the caller counts a pawn's frame changes over.
        /// </summary>
        /// <remarks>
        /// Expressed here rather than at the call site so the policy and the constant it comes from stay
        /// in one place; the caller owns the per-entity clock and counter because this class is stateless
        /// and shared by every pawn in the session.
        /// </remarks>
        public uint CarrierSwitchCooldownTicks =>
            (uint)Math.Max(1, (int)Math.Ceiling(CarrierSwitchCooldownSeconds * config.ServerTickRate));

        /// <summary>
        /// Judges one reported move, honouring any carrier change it carries.
        /// </summary>
        /// <remarks>
        /// The overload without the frequency gate, for callers that hold no per-entity history — a
        /// first report, or a test asking only what the distance rule makes of a move.
        /// </remarks>
        public MovementVerdict Validate(ushort prefabId, in PawnState previous, in PawnState next, float deltaSeconds) {
            return Validate(prefabId, in previous, in next, deltaSeconds, true);
        }

        /// <summary>
        /// Judges one reported move.
        /// </summary>
        /// <param name="prefabId">Selects the movement profile; an unknown id validates nothing.</param>
        /// <param name="previous">The state the server currently holds.</param>
        /// <param name="next">The state the owning client reported.</param>
        /// <param name="deltaSeconds">Time between the two, as the server measured it.</param>
        /// <param name="carrierChangeAllowed">
        /// Whether this pawn still has frame changes left in its current window — see
        /// <see cref="MaxCarrierSwitchesPerWindow"/>. False rejects the change outright, which holds the
        /// previous state — carrier included — so the pawn stays self-consistent and the client is told.
        /// </param>
        public MovementVerdict Validate(
            ushort prefabId,
            in PawnState previous,
            in PawnState next,
            float deltaSeconds,
            bool carrierChangeAllowed) {
            if (previous.CarrierId != next.CarrierId) {
                // Two origins, no displacement to measure; see the trust-boundary note on the type.
                if (!carrierChangeAllowed) {
                    return MovementVerdict.Reject(in previous, 0f, 0f);
                }

                return MovementVerdict.Accept(in next, 0f, 0f);
            }

            MovementProfile profile = config.GetMovementProfile(prefabId);

            if (profile == null || deltaSeconds <= 0f) {
                // No envelope to measure against, or no time to have moved in. Trusting the client here is
                // the deliberate choice: silently snapping pawns because a prefab was left out of the
                // registry would be a far more confusing failure than an unvalidated one.
                return MovementVerdict.Accept(in next, 0f, 0f);
            }

            float allowedSpeed = ResolveAllowedSpeed(profile, in previous, in next);
            float allowedDistance = allowedSpeed * deltaSeconds + PositionSlackMetres;

            Vector3 travel = new Vector3(
                next.Position.X - previous.Position.X,
                0f,
                next.Position.Z - previous.Position.Z);
            float travelled = travel.Length();
            float reportedSpeed = travelled / deltaSeconds;

            if (travelled <= allowedDistance) {
                return MovementVerdict.Accept(in next, reportedSpeed, allowedSpeed);
            }

            if (travelled > allowedDistance * RejectDistanceRatio) {
                return MovementVerdict.Reject(in previous, reportedSpeed, allowedSpeed);
            }

            PawnState clamped = ClampTravel(in previous, in next, travel, travelled, allowedDistance);
            return MovementVerdict.Clamp(in clamped, reportedSpeed, allowedSpeed);
        }

        /// <summary>
        /// The gait ceiling this move is measured against, taking the more permissive of the two reported
        /// gaits and applying the configured tolerance.
        /// </summary>
        private float ResolveAllowedSpeed(MovementProfile profile, in PawnState previous, in PawnState next) {
            float previousGaitSpeed = profile.GetSpeedForGait((int)previous.Locomotion);
            float nextGaitSpeed = profile.GetSpeedForGait((int)next.Locomotion);
            float gaitSpeed = Math.Max(previousGaitSpeed, nextGaitSpeed);

            return gaitSpeed * config.MovementToleranceMultiplier;
        }

        /// <summary>
        /// Keeps the client's heading and everything vertical, but shortens the step to the distance the
        /// gait allowed. Correcting direction as well would fight the player over which way they are
        /// facing on top of how fast they got there.
        /// </summary>
        private static PawnState ClampTravel(
            in PawnState previous,
            in PawnState next,
            Vector3 travel,
            float travelled,
            float allowedDistance) {
            float scale = allowedDistance / travelled;
            Vector3 position = new Vector3(
                previous.Position.X + travel.X * scale,
                next.Position.Y,
                previous.Position.Z + travel.Z * scale);

            return new PawnState(position, next.YawDegrees, next.Velocity, next.Flags, next.CarrierId);
        }
    }
}
