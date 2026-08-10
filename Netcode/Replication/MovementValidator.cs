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

        private readonly NetConfig config;

        public MovementValidator(NetConfig config) {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>The configuration this validator reads its tolerance and movement profiles from.</summary>
        public NetConfig Config => config;

        /// <summary>
        /// Judges one reported move.
        /// </summary>
        /// <param name="prefabId">Selects the movement profile; an unknown id validates nothing.</param>
        /// <param name="previous">The state the server currently holds.</param>
        /// <param name="next">The state the owning client reported.</param>
        /// <param name="deltaSeconds">Time between the two, as the server measured it.</param>
        public MovementVerdict Validate(ushort prefabId, in PawnState previous, in PawnState next, float deltaSeconds) {
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

            return new PawnState(position, next.YawDegrees, next.Velocity, next.Flags);
        }
    }
}
