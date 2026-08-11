using System;
using System.Numerics;
using AlpineLib.Netcode.Collision;
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
    /// between the two — gait speeds, gravity, jump impulse, the scene itself — arrives as an argument.
    /// The world is a <see cref="CollisionWorld"/> built from the same exported bytes on both ends, and
    /// the tick is passed in rather than counted here because a mover's pose is a pure function of it.
    /// </para>
    /// <para>
    /// <b>Determinism rules this file obeys, and any edit must keep.</b> Single-precision floats only,
    /// never <see cref="double"/>, so no platform can widen an intermediate and land a different last
    /// bit. Operations happen in a fixed order with no reassociation, which is why every vector sum below
    /// is spelled out componentwise instead of leaning on an operator whose lowering is the JIT's
    /// business. The only arithmetic on the position path is <c>+ - * /</c> plus
    /// <see cref="MathF.Sqrt"/>, <see cref="MathF.Min"/>, <see cref="MathF.Max"/> and
    /// <see cref="MathF.Abs"/>. There is exactly one trigonometric call per step —
    /// <see cref="MathF.Cos"/> of the profile's slope limit, turning an authored angle into the threshold
    /// a surface normal is compared against — and a libm difference of one ulp there can only flip
    /// walkability for a surface sitting exactly on the limit, which the next correction absorbs.
    /// <see cref="MathF.Atan2"/> derives yaw but never feeds back into position, so a facing may differ
    /// by a hair without ever moving a pawn somewhere else. Substep counts and iteration counts are fixed
    /// by comparison, never by "iterate until it stops changing".
    /// </para>
    /// <para>
    /// The velocity model is blunt on the ground and inertial in the air, matching the engine-side actor:
    /// grounded velocity snaps to the gait's top speed, airborne velocity steers toward the commanded
    /// direction at <see cref="MovementProfile.AirAcceleration"/> and keeps its momentum when input is
    /// released.
    /// </para>
    /// <para>
    /// <b>How a step moves.</b> (1) If the pawn was standing on a mover, that mover's travel since the
    /// previous tick is added to its position — the whole of the rider rule, stateless and deterministic.
    /// (2) Horizontal and (3) vertical velocity are resolved. (4) The displacement is integrated in up to
    /// <see cref="MaxSubsteps"/> substeps, each no longer than half the capsule's radius so nothing is
    /// tunnelled through, and each substep is followed by up to
    /// <see cref="MaxDepenetrationIterations"/> passes that push the capsule out of whatever it ended up
    /// inside and slide the velocity along the surface. (5) A single vertical support query decides
    /// grounding, clamps the feet to the surface and zeroes vertical velocity. (6) Yaw and the flags byte
    /// are packed.
    /// </para>
    /// <para>
    /// <b>Why the capsule is shortened while grounded.</b> A pawn that was standing at the start of the
    /// step collides with a capsule whose feet are raised by <see cref="MovementProfile.StepOffset"/>
    /// and whose crown is left where it was. Geometry whose top is no higher than the step offset then
    /// cannot touch that capsule at all — the raised lower sphere's centre sits exactly
    /// <c>StepOffset + Radius</c> above the feet, so a ledge of exactly the step offset is exactly
    /// tangent — and the support query in step 5 finds it and clamps the feet up onto it. Anything taller
    /// still blocks, and blocks horizontally: contacts found by the raised capsule are resolved along the
    /// horizontal part of their normal, so no wall, however slanted, can ever push a walking pawn upward.
    /// That single substitution is the whole of "step up onto low ledges, slide off steep faces", and it
    /// is why the walkability test lives in the support query rather than in depenetration. An airborne
    /// pawn collides with its true capsule and is pushed out along the full normal, because landing on
    /// something is exactly that push.
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
        /// Most pieces one tick's displacement is broken into. A pawn moving faster than half its radius
        /// per substep could otherwise start one substep outside a wall and end it outside the other side,
        /// with nothing to depenetrate against in between.
        /// </summary>
        public const int MaxSubsteps = 4;

        /// <summary>
        /// Most push-out passes one substep gets. Each pass resolves the deepest contact, so four passes
        /// settle a pawn wedged into a four-sided pocket; a fifth would be arithmetic spent on a case that
        /// does not occur, and an unbounded loop would be a place for the two ends to disagree.
        /// </summary>
        public const int MaxDepenetrationIterations = 4;

        /// <summary>Degrees to radians, for the one slope-limit cosine each step takes.</summary>
        private const float DegreesToRadians = MathF.PI / 180f;

        /// <summary>
        /// Shortest horizontal part of a contact normal a wall push will divide by. A normal barely off
        /// vertical would otherwise demand an enormous horizontal push to resolve a millimetre of overlap;
        /// clamping the divisor under-resolves such a contact instead, which the next iteration and the
        /// next tick continue to work on.
        /// </summary>
        private const float MinimumWallSlant = 0.2f;

        /// <summary>
        /// Below this the normal has no horizontal direction at all and the wall response has nothing to
        /// push along, so the contact is resolved along its full normal like an airborne one.
        /// </summary>
        private const float WallNormalEpsilon = 1e-4f;

        /// <summary>
        /// Advances a pawn by one fixed step.
        /// </summary>
        /// <param name="input">The owner's intent for this tick.</param>
        /// <param name="state">The state this step starts from.</param>
        /// <param name="profile">Gait speeds, gravity, jump impulse and capsule dimensions for this archetype.</param>
        /// <param name="world">The scene's collision, identical on both ends of the wire.</param>
        /// <param name="simTick">The tick being simulated. Mover poses are a pure function of it.</param>
        /// <param name="deltaSeconds">Length of the step. Callers pass the fixed tick interval.</param>
        /// <returns>The state after the step.</returns>
        public static PawnState Step(
            in PawnInput input,
            in PawnState state,
            MovementProfile profile,
            CollisionWorld world,
            uint simTick,
            float deltaSeconds) {
            if (profile == null) {
                throw new ArgumentNullException(nameof(profile));
            }

            if (world == null) {
                throw new ArgumentNullException(nameof(world));
            }

            if (deltaSeconds <= 0f) {
                return state;
            }

            Vector2 moveDirection = ClampToUnit(input.MoveDirection);
            float gaitSpeed = profile.GetSpeedForGait((int)input.Gait);
            bool startedGrounded = state.IsGrounded;

            Vector3 ride = ResolveRideDelta(in state, world, simTick);
            var position = new Vector3(
                state.Position.X + ride.X,
                state.Position.Y + ride.Y,
                state.Position.Z + ride.Z);

            float velocityX;
            float velocityZ;

            if (startedGrounded) {
                velocityX = moveDirection.X * gaitSpeed;
                velocityZ = moveDirection.Y * gaitSpeed;
            } else {
                velocityX = state.Velocity.X;
                velocityZ = state.Velocity.Z;
                StepAirVelocity(ref velocityX, ref velocityZ, moveDirection, gaitSpeed, profile, deltaSeconds);
            }

            var velocity = new Vector3(velocityX, StepVerticalVelocity(in input, in state, profile, deltaSeconds), velocityZ);

            Integrate(ref position, ref velocity, startedGrounded, profile, world, simTick, deltaSeconds);
            bool isGrounded = ResolveGrounding(ref position, ref velocity, startedGrounded, profile, world, simTick);

            float yawDegrees = ResolveYaw(moveDirection, state.YawDegrees);
            byte flags = PawnState.PackFlags(input.Gait, input.Crouch, isGrounded);

            return new PawnState(position, yawDegrees, velocity, flags);
        }

        /// <summary>
        /// Runs a whole span of inputs through <see cref="Step"/> in order, advancing the tick with them.
        /// Used by prediction replay and by any caller catching a pawn up after a stall.
        /// </summary>
        /// <param name="firstTick">The tick the first input in the span is simulated at.</param>
        public static PawnState StepMany(
            ReadOnlySpan<PawnInput> inputs,
            in PawnState state,
            MovementProfile profile,
            CollisionWorld world,
            uint firstTick,
            float deltaSeconds) {
            PawnState current = state;

            for (int inputIndex = 0; inputIndex < inputs.Length; inputIndex++) {
                current = Step(in inputs[inputIndex], in current, profile, world, firstTick + (uint)inputIndex, deltaSeconds);
            }

            return current;
        }

        /// <summary>
        /// How far the surface under the pawn travelled between the previous tick and this one, when that
        /// surface belongs to a mover.
        /// </summary>
        /// <remarks>
        /// This is the whole of the rider rule. The probe is deliberately tight and deliberately asked at
        /// the <em>previous</em> tick: the pawn's feet were clamped to that surface at that tick, so a
        /// surface a hair either side of them is the one it was standing on, and asking at the current
        /// tick would look for a platform that has already moved out from under it. Nothing is remembered
        /// between steps, so a pawn that steps off a platform simply stops being carried.
        /// </remarks>
        private static Vector3 ResolveRideDelta(in PawnState state, CollisionWorld world, uint simTick) {
            if (!state.IsGrounded || simTick == 0u) {
                return Vector3.Zero;
            }

            float probeTop = state.Position.Y + GroundTolerance;
            float probeBottom = state.Position.Y - GroundTolerance;
            bool supported = world.TryGetSupport(
                state.Position.X,
                state.Position.Z,
                probeTop,
                probeBottom,
                simTick - 1u,
                out SupportHit hit);
            if (!supported || !hit.IsMover) {
                return Vector3.Zero;
            }

            return world.MoverDelta(hit.MoverIndex, simTick);
        }

        /// <summary>
        /// Walks the tick's displacement in substeps, depenetrating after each. Velocity is re-read every
        /// substep because sliding along a surface changes it, so a pawn that clips a wall halfway through
        /// a tick spends the rest of that tick travelling along the wall rather than into it.
        /// </summary>
        private static void Integrate(
            ref Vector3 position,
            ref Vector3 velocity,
            bool startedGrounded,
            MovementProfile profile,
            CollisionWorld world,
            uint simTick,
            float deltaSeconds) {
            var displacement = new Vector3(
                velocity.X * deltaSeconds,
                velocity.Y * deltaSeconds,
                velocity.Z * deltaSeconds);
            int substepCount = ResolveSubstepCount(in displacement, profile.CapsuleRadius * 0.5f);
            float substepSeconds = deltaSeconds / substepCount;

            for (int substep = 0; substep < substepCount; substep++) {
                position = new Vector3(
                    position.X + velocity.X * substepSeconds,
                    position.Y + velocity.Y * substepSeconds,
                    position.Z + velocity.Z * substepSeconds);
                Depenetrate(ref position, ref velocity, startedGrounded, profile, world, simTick);
            }
        }

        /// <summary>
        /// How many pieces this tick's displacement is cut into, so that no piece is longer than
        /// <paramref name="substepLimit"/>.
        /// </summary>
        /// <remarks>
        /// Counted by comparing squared lengths against squared thresholds rather than by dividing and
        /// rounding up: the comparison chain is a handful of multiplications whose result is exact on
        /// every runtime, where a ceiling of a quotient is one rounding away from answering three on one
        /// end of the wire and four on the other, and a different substep count is a different position.
        /// </remarks>
        private static int ResolveSubstepCount(in Vector3 displacement, float substepLimit) {
            if (substepLimit <= 0f) {
                return MaxSubsteps;
            }

            float lengthSquared = displacement.X * displacement.X
                + displacement.Y * displacement.Y
                + displacement.Z * displacement.Z;
            int substepCount = 1;

            while (substepCount < MaxSubsteps) {
                float reach = substepLimit * substepCount;
                if (lengthSquared <= reach * reach) {
                    break;
                }

                substepCount++;
            }

            return substepCount;
        }

        /// <summary>
        /// Pushes the capsule out of whatever it overlaps, deepest contact first, up to
        /// <see cref="MaxDepenetrationIterations"/> times.
        /// </summary>
        /// <remarks>
        /// One contact per pass, re-collected each time, rather than every contact in one sweep: two
        /// shapes reporting overlaps against the same pre-push position would each ask for their full
        /// depth and together push a pawn resting in a seam twice as far as either needed. Resolving the
        /// deepest and looking again costs an extra query and never overshoots.
        /// </remarks>
        private static void Depenetrate(
            ref Vector3 position,
            ref Vector3 velocity,
            bool startedGrounded,
            MovementProfile profile,
            CollisionWorld world,
            uint simTick) {
            for (int iteration = 0; iteration < MaxDepenetrationIterations; iteration++) {
                CapsulePose pose = BuildPose(in position, profile, startedGrounded);
                if (!TryFindDeepestContact(in pose, world, simTick, out CollisionContact contact)) {
                    return;
                }

                if (startedGrounded) {
                    ApplyWallResponse(ref position, ref velocity, in contact);
                    continue;
                }

                ApplyFreeResponse(ref position, ref velocity, in contact);
            }
        }

        /// <summary>
        /// The capsule the pawn collides with this substep: its true one while airborne, and one whose
        /// feet are raised by the step offset while grounded. See the type's remarks for why that
        /// substitution is the entire step-up mechanism.
        /// </summary>
        /// <remarks>
        /// Only the feet rise; the crown stays where it was, so a pawn does not lose its headroom for
        /// walking. A profile whose step offset eats most of its height would invert the inner segment, so
        /// the shortened capsule is floored at a sphere.
        /// </remarks>
        private static CapsulePose BuildPose(in Vector3 position, MovementProfile profile, bool startedGrounded) {
            if (!startedGrounded) {
                return new CapsulePose(position, profile.CapsuleRadius, profile.CapsuleHeight);
            }

            float lift = MathF.Max(profile.StepOffset, 0f);
            float shortened = MathF.Max(profile.CapsuleHeight - lift, profile.CapsuleRadius * 2f);
            var raised = new Vector3(position.X, position.Y + lift, position.Z);
            return new CapsulePose(raised, profile.CapsuleRadius, shortened);
        }

        /// <summary>
        /// The deepest overlap the capsule currently has, or nothing when it is clear. Ties keep the
        /// earlier contact, and the world reports statics in ascending index order before movers, so the
        /// same overlap wins on both ends of the wire.
        /// </summary>
        private static bool TryFindDeepestContact(
            in CapsulePose pose,
            CollisionWorld world,
            uint simTick,
            out CollisionContact deepest) {
            Span<CollisionContact> contacts = stackalloc CollisionContact[CollisionWorld.MaxContacts];
            int contactCount = world.CollectContacts(in pose, simTick, contacts);
            deepest = default;
            bool found = false;

            for (int index = 0; index < contactCount; index++) {
                if (found && contacts[index].Depth <= deepest.Depth) {
                    continue;
                }

                deepest = contacts[index];
                found = true;
            }

            return found;
        }

        /// <summary>
        /// Resolves a contact found by the grounded pawn's raised capsule. Everything that capsule can
        /// touch is a wall by construction — low ledges cannot reach it — so the push and the slide both
        /// happen in the horizontal plane and a walking pawn is never lifted or dropped by one.
        /// </summary>
        private static void ApplyWallResponse(ref Vector3 position, ref Vector3 velocity, in CollisionContact contact) {
            float horizontalSquared = contact.Normal.X * contact.Normal.X + contact.Normal.Z * contact.Normal.Z;
            if (horizontalSquared <= WallNormalEpsilon) {
                ApplyFreeResponse(ref position, ref velocity, in contact);
                return;
            }

            float horizontalLength = MathF.Sqrt(horizontalSquared);
            float pushX = contact.Normal.X / horizontalLength;
            float pushZ = contact.Normal.Z / horizontalLength;
            float distance = contact.Depth / MathF.Max(horizontalLength, MinimumWallSlant);
            position = new Vector3(position.X + pushX * distance, position.Y, position.Z + pushZ * distance);

            float into = velocity.X * pushX + velocity.Z * pushZ;
            if (into >= 0f) {
                return;
            }

            velocity = new Vector3(velocity.X - pushX * into, velocity.Y, velocity.Z - pushZ * into);
        }

        /// <summary>
        /// Resolves a contact against the airborne pawn's true capsule: pushed straight out along the
        /// contact normal, with the velocity into that normal removed.
        /// </summary>
        /// <remarks>
        /// A falling pawn's velocity is what the push cancels, which is what landing is. The one thing a
        /// contact may not do is hand a descending pawn upward speed — sliding down a steep face would
        /// otherwise convert the fall into a hop up it — so an upward result is flattened to nothing.
        /// </remarks>
        private static void ApplyFreeResponse(ref Vector3 position, ref Vector3 velocity, in CollisionContact contact) {
            position = new Vector3(
                position.X + contact.Normal.X * contact.Depth,
                position.Y + contact.Normal.Y * contact.Depth,
                position.Z + contact.Normal.Z * contact.Depth);

            float incomingY = velocity.Y;
            float into = velocity.X * contact.Normal.X + velocity.Y * contact.Normal.Y + velocity.Z * contact.Normal.Z;
            if (into < 0f) {
                velocity = new Vector3(
                    velocity.X - contact.Normal.X * into,
                    velocity.Y - contact.Normal.Y * into,
                    velocity.Z - contact.Normal.Z * into);
            }

            if (incomingY <= 0f && velocity.Y > 0f) {
                velocity.Y = 0f;
            }
        }

        /// <summary>
        /// The one query that decides grounding, clamps the feet to the surface and parks vertical
        /// velocity.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The probe spans a step offset either side of the feet, and how much of that span counts depends
        /// on where the pawn came from. A pawn that was already standing may be clamped anywhere inside
        /// it: upward is the step onto a low ledge, downward is the ramp it is walking down and the reason
        /// a walking pawn does not launch off every crest. A pawn arriving from the air has to actually
        /// reach the surface, within the ground tolerance — plus the sag, which is the gap a round-bottomed
        /// capsule keeps between its lowest point and a sloped surface it rests tangent to. Without that
        /// term a pawn landing on anything steeper than a gentle ramp would be depenetrated to tangency,
        /// measured against the surface directly below it, and told it was still falling, for ever.
        /// </para>
        /// <para>
        /// Walkability is the only trigonometry in the step: <see cref="MathF.Cos"/> of the authored slope
        /// limit, compared against the surface normal's vertical component. A surface sitting exactly on
        /// the limit may therefore be judged differently by one ulp on the two ends; a pawn balanced there
        /// is a pawn about to be corrected either way.
        /// </para>
        /// </remarks>
        private static bool ResolveGrounding(
            ref Vector3 position,
            ref Vector3 velocity,
            bool startedGrounded,
            MovementProfile profile,
            CollisionWorld world,
            uint simTick) {
            if (velocity.Y > 0f) {
                return false;
            }

            float probeTop = position.Y + profile.StepOffset;
            float probeBottom = position.Y - profile.StepOffset;
            if (!world.TryGetSupport(position.X, position.Z, probeTop, probeBottom, simTick, out SupportHit hit)) {
                return false;
            }

            float walkableThreshold = MathF.Cos(profile.SlopeLimitDegrees * DegreesToRadians);
            if (hit.Normal.Y <= 0f || hit.Normal.Y < walkableThreshold) {
                return false;
            }

            float rise = hit.Height - position.Y;
            if (rise > ReachAbove(profile, startedGrounded) || rise < -ReachBelow(profile, startedGrounded, hit.Normal.Y)) {
                return false;
            }

            position.Y = hit.Height;
            velocity.Y = 0f;
            return true;
        }

        /// <summary>
        /// How far above the feet a surface may sit and still be stood on: a whole step offset for a pawn
        /// that was already walking, and only the ground tolerance for one arriving from the air, which
        /// lands on ledges rather than being hoisted onto them.
        /// </summary>
        private static float ReachAbove(MovementProfile profile, bool startedGrounded) {
            return startedGrounded ? profile.StepOffset : GroundTolerance;
        }

        /// <summary>
        /// How far below the feet a surface may sit and still be clamped down to. See
        /// <see cref="ResolveGrounding"/> for why the airborne case carries the capsule's tangency sag,
        /// and why that sag is capped rather than allowed to run away as a normal approaches horizontal.
        /// </summary>
        private static float ReachBelow(MovementProfile profile, bool startedGrounded, float normalY) {
            if (startedGrounded) {
                return profile.StepOffset;
            }

            float sag = profile.CapsuleRadius / normalY - profile.CapsuleRadius;
            return GroundTolerance + MathF.Min(sag, profile.StepOffset);
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
