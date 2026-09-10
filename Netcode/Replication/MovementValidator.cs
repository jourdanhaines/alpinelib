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
    /// only the alternation nobody produces by walking — and honest play is held to the same edge from
    /// the other end, because <c>INetCarrierSource</c> owes this side hysteresis and a source honouring
    /// that dwell fills the budget exactly rather than overrunning it.
    /// </para>
    /// <para>
    /// <b>The other unmeasured move.</b> An owner that stopped reporting because it had nothing truthful
    /// to say comes back with <c>OwnerPawnUpdate.ResyncFlag</c> set, and that resumption is accepted on
    /// the same terms and out of the same budget as a frame change: unmeasured, bounded by frequency, and
    /// never free. It is the same trust boundary seen from the time axis rather than the space one — the
    /// two poses either side belong to different stories rather than different origins — so the exposure
    /// below covers it unchanged. What is charged is the resumption, not the datagrams carrying it: the
    /// owner repeats the flag against packet loss, and the repeats landing inside
    /// <see cref="ResyncBurstTicks"/> of the send that opened the burst are the same claim arriving
    /// again — see <see cref="IsResyncBurstContinuation"/>.
    /// </para>
    /// <para>
    /// <b>What the free tail of a burst is worth.</b> The repeats are judged at the held velocity, which
    /// nothing validates, so the wire is the only ceiling: <c>NetQuantization</c> clamps each axis to
    /// 127.996 m/s, a planar 181.02 m/s, which at the tolerance multiplier over one tick plus slack times
    /// <see cref="RejectDistanceRatio"/> is 27.30 m per repeat and 81.88 m for the three that fit a
    /// window. At the eight bursts a second <see cref="ResyncBurstTicks"/> allows that is 655 m/s of free
    /// travel — sitting behind eight charged teleports a second that are each unbounded in distance, so
    /// the tail is strictly dominated by the thing that opens it and cannot be had without a charged,
    /// violation-raising opener. Against the sprint gait alone the same repeat carries under a metre,
    /// which is the size of the prize for a velocity sanity check on the claim that opens a burst.
    /// </para>
    /// <para>
    /// <b>What the interval cap costs an honest client.</b> The gap a move is measured over is capped at
    /// <c>ServerReplication.MaxMeasuredIntervalSeconds</c>, so silence stops buying allowance — and the
    /// bill for that lands on a client whose datagrams were merely lost. With the cap at <c>T</c>, a gap
    /// <c>g</c> at gait <c>v</c> is rejected once <c>v·g &gt; RejectDistanceRatio · (tolerance · v · T +
    /// slack)</c>, i.e. from about <c>4.5·T</c> onwards at any gait: a two-second gap is clamped and
    /// rubber-banded, and one past roughly four and a half seconds is thrown away and snapped back. The
    /// resync flag cannot cover this case at all — an owner raises it only when it withholds or places a
    /// state itself, and a client losing packets has done neither and cannot tell. Repeating the flag
    /// (see <c>NetActorSync.SendOwnerSample</c>) covers the loss of a resync the owner did raise, and
    /// nothing more. The fix for the gap itself is a receipt clock on the server side, stamping when an
    /// owner update last <em>arrived</em> and treating an overlong gap as a resync.
    /// </para>
    /// <para>
    /// <b>And there is no recovery if the whole burst is lost.</b> The owner's flag is spent on each send
    /// and nothing re-raises it, so losing every repeat means the resumption is never adopted: the next
    /// plain update is measured over a clamped gap, rejected as a teleport, and the owner is corrected
    /// back onto the pre-withhold pose — for a rider who jumped off a consist, back onto the moving train
    /// they left. It converges within a second and produces a wrong-place snap rather than an exploit, so
    /// it is a residual rather than a hole. The honest way to state its likelihood is not three
    /// independent losses but "three sends inside 67 ms, which one loss burst covers": widening the
    /// repeat count spreads them over more ticks and buys nothing against an outage of the same length,
    /// and only the receipt clock above addresses the mechanism rather than the dice.
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
        /// Three is what a conforming source produces at its fastest, and it fills the window exactly.
        /// The window is eight ticks of a thirty-hertz clock and <c>NetCarrier.SourceHysteresisSeconds</c>
        /// is three of them, so a source honouring the dwell lands changes on ticks 0, 3 and 6 — all
        /// inside one window, which only reopens at elapsed ≥ 8 — and that is the honest burst itself, a
        /// hop off a deck and back or a walk across a coupler, seen at the dwell's own cadence. There is
        /// no margin against honest play in those three, which is the fact a future reader needs: the
        /// count cannot be lowered without lengthening the dwell first.
        /// </para>
        /// <para>
        /// <b>A resync does not need a fourth slot, because it cannot coincide with a charge.</b> An
        /// owner pushes at most one sample per send tick, so a resync on tick <c>k</c> means nothing at
        /// all was sent on tick <c>k-1</c> and nothing was charged there either. The earliest resync that
        /// can follow the burst above is therefore tick 8, and tick 8 reopens the window; a resync
        /// earlier in a window instead pushes the source's remaining dwells past its end. The two
        /// claimants interleave rather than stack.
        /// </para>
        /// <para>
        /// That argument is about the tick a resync is <em>charged</em> on, which is the tick the owner
        /// broke its silence — one per resumption, whatever the transport does to the datagrams. The
        /// repeats the owner sends behind it land on the following send ticks with nothing silent in
        /// front of them, and they are exactly why <see cref="ResyncBurstTicks"/> exists: they are the
        /// same claim arriving again and are charged nothing. It survives the transport bunching those
        /// datagrams too, because the caller adopts at most one of them per server tick — see
        /// <c>ServerReplication.TryAcceptResync</c> — so a tick that carries a hundred flagged arrivals
        /// still resolves to one pose, and the interleaving argument is counting the same ticks it was.
        /// </para>
        /// <para>
        /// The count is also the ceiling on a client that lies, and that ceiling is a rate rather than a
        /// distance: nothing bounds how far one unmeasured move may travel, so each slot is a teleport of
        /// any size and three slots is twelve of them a second at the default tick rate — a peak, and one
        /// only the frame-change path reaches, because on the resync path the binding constraint is
        /// <see cref="ResyncBurstTicks"/> and the measured ceiling is eight a second. Widening it for an
        /// interleaving nobody can produce would widen that in the same proportion.
        /// </para>
        /// </remarks>
        public const int MaxCarrierSwitchesPerWindow = 3;

        /// <summary>
        /// Ticks after the send that opened a resync burst within which a further flagged claim is the
        /// same resumption arriving again rather than a new one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// One more than the number of sends <c>NetActorSync.ResyncSendRepeats</c> puts the flag on, so
        /// the last repeat of a burst is still inside the window with a tick of slack for jitter. The
        /// owner sends at the server's tick rate — <c>NetConfig.Validate</c> refuses any other pairing —
        /// so a send tick and a server tick are the same thing here.
        /// </para>
        /// <para>
        /// It bounds the cheat as well as the honest case: a client that flags everything gets one
        /// unmeasured move of any size per window this long — fewer than the budget alone would give it
        /// — because the claims in between must pass
        /// <see cref="IsResyncBurstContinuation"/>, which a teleport does not.
        /// </para>
        /// <para>
        /// This is a window of ticks <em>and</em> a window of one adoption per tick. Counting only the
        /// ticks would leave the free side of the burst unbounded, since the sender chooses how many
        /// datagrams share a tick and each would be judged at the full one-tick bar; the caller therefore
        /// refuses a flagged claim arriving on a tick that already moved this pawn. So the free tail is
        /// at most <c>this - 1</c> repeats per burst, which is the bound the class remarks price.
        /// </para>
        /// </remarks>
        public const uint ResyncBurstTicks = 4;

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

            Vector3 travel = PlanarTravel(in previous, in next);
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
        /// Whether a flagged claim arriving inside a resync burst is the resumption already adopted,
        /// carried on for another tick, rather than a fresh unmeasured move.
        /// </summary>
        /// <param name="prefabId">Selects the movement profile; an unknown id continues anything.</param>
        /// <param name="held">The state the server adopted from the resync that opened the burst.</param>
        /// <param name="claim">The state the repeat reports.</param>
        /// <param name="deltaSeconds">Time between the two, as the server measured it.</param>
        /// <remarks>
        /// <para>
        /// The repeats of a burst are not the same numbers twice: a rider whose withhold ended while
        /// they were still carrying a thirty-metre-a-second consist covers a metre between sends, which
        /// is a teleport to the gait ceiling and ordinary continued motion to anyone watching. So the
        /// bar is the teleport bar — <see cref="RejectDistanceRatio"/> times the allowance — taken at the
        /// faster of the gait and the speed the server itself already holds for this pawn.
        /// </para>
        /// <para>
        /// Reading the held speed rather than the claimed one is what keeps this from being a way in.
        /// That velocity arrived on the claim the server charged a budget slot for and replicated to
        /// every other peer; a client wanting half-kilometre repeats has to have announced the speed
        /// that covers them, to everybody, and pay for the burst that opened it.
        /// </para>
        /// <para>
        /// A gap of no time is not a continuation of anything and is refused rather than waved through:
        /// the caller measures at least one tick, so this only answers a caller that has none, and the
        /// permissive reading is the one that made a same-tick flood free.
        /// </para>
        /// </remarks>
        public bool IsResyncBurstContinuation(
            ushort prefabId,
            in PawnState held,
            in PawnState claim,
            float deltaSeconds) {
            MovementProfile profile = config.GetMovementProfile(prefabId);

            if (deltaSeconds <= 0f) return false;
            if (profile == null) return true;

            float carriedSpeed = PlanarLength(held.Velocity) * config.MovementToleranceMultiplier;
            float allowedSpeed = Math.Max(ResolveAllowedSpeed(profile, in held, in claim), carriedSpeed);
            float allowedDistance = allowedSpeed * deltaSeconds + PositionSlackMetres;

            return PlanarTravel(in held, in claim).Length() <= allowedDistance * RejectDistanceRatio;
        }

        /// <summary>
        /// The horizontal step between two states. Vertical travel is left out of every distance rule
        /// here: gravity and a stair are not gait.
        /// </summary>
        private static Vector3 PlanarTravel(in PawnState previous, in PawnState next) {
            return new Vector3(
                next.Position.X - previous.Position.X,
                0f,
                next.Position.Z - previous.Position.Z);
        }

        /// <summary>The horizontal magnitude of a reported velocity.</summary>
        private static float PlanarLength(Vector3 velocity) {
            return new Vector3(velocity.X, 0f, velocity.Z).Length();
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
