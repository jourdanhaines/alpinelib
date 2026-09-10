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
    /// alternative is rejecting every legitimate boarding, but it is bounded by frequency rather than by
    /// distance: a change is only honoured when the caller says it has not honoured one for this pawn
    /// within <see cref="CarrierSwitchCooldownSeconds"/>, and one arriving sooner is rejected outright. Frequency is the only thing the server can judge here — it
    /// does not know where any carrier is, so it cannot subtract two origins however much it would like
    /// to. What the hole cannot buy, either way, is speed: every tick that keeps the same carrier is
    /// validated exactly as before, in that carrier's frame, where the gait ceiling is the pawn's own
    /// walking pace and the carrier's motion is not part of the measurement at all.
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
        /// Shortest time between two honoured carrier changes for one pawn. Long enough that repeating
        /// the frame-change trick costs an order of magnitude in travel; short enough that a real
        /// boarding never notices, including the awkward one — walking across a coupler from one car to
        /// the next, which is two frame changes in quick succession.
        /// </summary>
        public const float CarrierSwitchCooldownSeconds = 0.25f;

        private readonly NetConfig config;

        public MovementValidator(NetConfig config) {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>The configuration this validator reads its tolerance and movement profiles from.</summary>
        public NetConfig Config => config;

        /// <summary>
        /// <see cref="CarrierSwitchCooldownSeconds"/> in server ticks, rounded up, never less than one.
        /// </summary>
        /// <remarks>
        /// Expressed here rather than at the call site so the policy and the constant it comes from stay
        /// in one place; the caller owns the per-entity clock because this class is stateless and shared
        /// by every pawn in the session.
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
        /// Whether this pawn's cooldown since its last honoured frame change has expired. False rejects a
        /// change outright, which holds the previous state — carrier included — so the pawn stays
        /// self-consistent and the client is told.
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
