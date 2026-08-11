using System;
using System.Numerics;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// The one kinematic step shared by the authoritative server and the predicting client: intent plus
    /// previous state in, next state out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Server-authoritative movement only works if both ends compute the same answer, so this is written
    /// as a pure function with no fields, no engine types and no hidden state. Everything that could vary
    /// between the two — gait speeds, gravity, jump impulse, the floor — arrives as an argument.
    /// </para>
    /// <para>
    /// <b>Determinism rules this file obeys, and any edit must keep.</b> Single-precision floats only,
    /// never <see cref="double"/>, so no platform can widen an intermediate and land a different last
    /// bit. Operations happen in a fixed order with no reassociation. No trigonometry on the position
    /// path — yaw is derived with <see cref="MathF.Atan2"/> but never feeds back into position, so a
    /// libm difference between Unity and .NET can rotate a pawn's facing by a hair without ever moving
    /// it somewhere else.
    /// </para>
    /// <para>
    /// The model is blunt on the ground and inertial in the air, matching the engine-side actor:
    /// grounded velocity snaps to the gait's top speed, airborne velocity steers toward the commanded
    /// direction at <see cref="MovementProfile.AirAcceleration"/> and keeps its momentum when input is
    /// released. There is no collision beyond the ground clamp — correctable later; the contract that
    /// matters is that whatever this does, it does identically in both processes.
    /// </para>
    /// </remarks>
    public static class PawnMotor {
        /// <summary>
        /// Movement input shorter than this counts as no input at all. Keeps a resting stick's noise from
        /// spinning a pawn's facing, and keeps <see cref="MathF.Atan2"/> away from the origin.
        /// </summary>
        public const float MoveDeadZone = 0.0001f;

        /// <summary>
        /// How far above the sampled floor a pawn may sit and still count as standing on it. Without a
        /// little slack, the tick after a ground clamp reads as airborne and the pawn strobes between
        /// grounded and falling.
        /// </summary>
        public const float GroundTolerance = 0.02f;

        /// <summary>
        /// Advances a pawn by one fixed step.
        /// </summary>
        /// <param name="input">The owner's intent for this tick.</param>
        /// <param name="state">The state this step starts from.</param>
        /// <param name="profile">Gait speeds, gravity and jump impulse for this pawn archetype.</param>
        /// <param name="groundProvider">Where the floor is. Must be pure; see <see cref="IGroundProvider"/>.</param>
        /// <param name="deltaSeconds">Length of the step. Callers pass the fixed tick interval.</param>
        /// <returns>The state after the step.</returns>
        public static PawnState Step(
            in PawnInput input,
            in PawnState state,
            MovementProfile profile,
            IGroundProvider groundProvider,
            float deltaSeconds) {
            if (profile == null) {
                throw new ArgumentNullException(nameof(profile));
            }

            if (groundProvider == null) {
                throw new ArgumentNullException(nameof(groundProvider));
            }

            if (deltaSeconds <= 0f) {
                return state;
            }

            Vector2 moveDirection = ClampToUnit(input.MoveDirection);
            float gaitSpeed = profile.GetSpeedForGait((int)input.Gait);

            float velocityX;
            float velocityZ;

            if (state.IsGrounded) {
                velocityX = moveDirection.X * gaitSpeed;
                velocityZ = moveDirection.Y * gaitSpeed;
            } else {
                velocityX = state.Velocity.X;
                velocityZ = state.Velocity.Z;
                StepAirVelocity(ref velocityX, ref velocityZ, moveDirection, gaitSpeed, profile, deltaSeconds);
            }

            float velocityY = StepVerticalVelocity(in input, in state, profile, deltaSeconds);

            Vector3 nextPosition = new Vector3(
                state.Position.X + velocityX * deltaSeconds,
                state.Position.Y + velocityY * deltaSeconds,
                state.Position.Z + velocityZ * deltaSeconds);

            float groundHeight = groundProvider.SampleHeight(nextPosition.X, nextPosition.Z);
            bool isGrounded = nextPosition.Y <= groundHeight + GroundTolerance && velocityY <= 0f;

            if (isGrounded) {
                nextPosition = new Vector3(nextPosition.X, groundHeight, nextPosition.Z);
                velocityY = 0f;
            }

            float yawDegrees = ResolveYaw(moveDirection, state.YawDegrees);
            byte flags = PawnState.PackFlags(input.Gait, input.Crouch, isGrounded);

            return new PawnState(nextPosition, yawDegrees, new Vector3(velocityX, velocityY, velocityZ), flags);
        }

        /// <summary>
        /// Runs a whole span of inputs through <see cref="Step"/> in order. Used by prediction replay and
        /// by any caller catching a pawn up after a stall.
        /// </summary>
        public static PawnState StepMany(
            ReadOnlySpan<PawnInput> inputs,
            in PawnState state,
            MovementProfile profile,
            IGroundProvider groundProvider,
            float deltaSeconds) {
            PawnState current = state;

            for (int inputIndex = 0; inputIndex < inputs.Length; inputIndex++) {
                current = Step(in inputs[inputIndex], in current, profile, groundProvider, deltaSeconds);
            }

            return current;
        }

        /// <summary>
        /// Steers airborne horizontal velocity toward the commanded direction at the profile's air
        /// acceleration, or decays it by the profile's air drag when there is no input — the same
        /// two-regime model the engine-side actor integrates, at the same fixed step. Deterministic:
        /// floats only, fixed operation order, one square root on the steering path.
        /// </summary>
        private static void StepAirVelocity(
            ref float velocityX,
            ref float velocityZ,
            Vector2 moveDirection,
            float gaitSpeed,
            MovementProfile profile,
            float deltaSeconds) {
            bool hasIntent = moveDirection.LengthSquared() >= MoveDeadZone * MoveDeadZone;

            if (!hasIntent) {
                if (profile.AirDrag > 0f) {
                    float decay = MathF.Exp(-profile.AirDrag * deltaSeconds);
                    velocityX *= decay;
                    velocityZ *= decay;
                }

                return;
            }

            float targetX = moveDirection.X * gaitSpeed;
            float targetZ = moveDirection.Y * gaitSpeed;
            float deltaX = targetX - velocityX;
            float deltaZ = targetZ - velocityZ;
            float maxStep = profile.AirAcceleration * deltaSeconds;
            float distanceSquared = deltaX * deltaX + deltaZ * deltaZ;

            if (distanceSquared <= maxStep * maxStep) {
                velocityX = targetX;
                velocityZ = targetZ;
                return;
            }

            float distance = MathF.Sqrt(distanceSquared);
            velocityX += deltaX / distance * maxStep;
            velocityZ += deltaZ / distance * maxStep;
        }

        /// <summary>
        /// Jump impulse first, then gravity, then integrate — an order the two ends must share, since
        /// applying gravity before the impulse would eat one tick's worth of the jump.
        /// </summary>
        private static float StepVerticalVelocity(
            in PawnInput input,
            in PawnState state,
            MovementProfile profile,
            float deltaSeconds) {
            float velocityY = state.Velocity.Y;

            if (input.Jump && state.IsGrounded) {
                velocityY = profile.JumpVelocity;
            }

            return velocityY + profile.Gravity * deltaSeconds;
        }

        /// <summary>
        /// Faces the direction of travel, or holds the previous facing when there is no travel. Yaw is
        /// measured clockwise from +Z, matching the engine's convention.
        /// </summary>
        private static float ResolveYaw(Vector2 moveDirection, float previousYawDegrees) {
            if (moveDirection.LengthSquared() < MoveDeadZone * MoveDeadZone) {
                return previousYawDegrees;
            }

            float radians = MathF.Atan2(moveDirection.X, moveDirection.Y);
            return radians * (180f / MathF.PI);
        }

        /// <summary>
        /// Caps the input vector at unit length without touching shorter ones, so analogue input keeps its
        /// fine control while a client that sends an over-long vector gains nothing by it.
        /// </summary>
        private static Vector2 ClampToUnit(Vector2 moveDirection) {
            float lengthSquared = moveDirection.LengthSquared();

            if (lengthSquared <= 1f) {
                return moveDirection;
            }

            float length = MathF.Sqrt(lengthSquared);
            return new Vector2(moveDirection.X / length, moveDirection.Y / length);
        }
    }
}
