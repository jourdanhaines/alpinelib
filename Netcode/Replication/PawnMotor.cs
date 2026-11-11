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
    /// <see cref="MathF.Abs"/>. The slope threshold and air drag decay are libm results, so the profile
    /// computes them once in double (<see cref="MovementProfile.WalkableNormalY"/>,
    /// <see cref="MovementProfile.GetAirDragDecay"/>) and the step only reads them.
    /// <see cref="MathF.Atan2"/> derives yaw but never feeds back into position, so a facing may differ
    /// by a hair without ever moving a pawn somewhere else. Substep counts and iteration counts are fixed
    /// by comparison, never by "iterate until it stops changing".
    /// </para>
    /// <para>
    /// <b>World frame, always.</b> Every number in and out of a step is world space, and a
    /// <see cref="PawnState.CarrierId"/> is neither read nor written here. The collision world, the mover
    /// poses and the gravity vector all share that one origin, so stepping a carrier-relative state
    /// would resolve deck-local metres against world geometry — the pawn would appear wherever those
    /// coordinates happen to land in the scene. Converting between a carrier's frame and the world is
    /// the caller's job, on either side of a step; the motor is deliberately kept ignorant that carriers
    /// exist at all so its determinism argument stays a claim about arithmetic alone.
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
    /// inside and slide the velocity along the surface. When those passes still leave the capsule more than
    /// <see cref="CornerTolerance"/> deep — an acute concave corner between two shapes, where pushing out of
    /// one face pushes into the other — the substep is resolved again jointly against every contact at once
    /// (see <see cref="ResolveCorner"/>). (5) A single vertical support query decides
    /// grounding, clamps the feet to the surface and zeroes vertical velocity; a clamp that would put the capsule
    /// more than the tolerance into something, and a walk off support whose whole capsule lands inside something,
    /// are settled in the same step (<see cref="LimitSnapDown"/>, <see cref="ClearSnapUp"/>,
    /// <see cref="SettleOffLedge"/>). (6) Yaw and the flags byte are packed.
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
        /// Deepest overlap the one-contact-at-a-time passes may leave before the joint resolution takes over, and
        /// the deepest it may leave in turn. Below it a substep keeps the original passes' result bit for bit.
        /// </summary>
        private const float CornerTolerance = 0.002f;

        /// <summary>Overlap at which the joint resolution stops early.</summary>
        private const float CornerSettledDepth = 0.0001f;

        /// <summary>Most joint passes one substep gets, each re-collecting the contacts where the last one left it.</summary>
        private const int CornerPasses = 4;

        /// <summary>A clipped velocity component within this share of the speed (summed over axes) is rounding: zero.</summary>
        private const float ClipSnapShare = 1e-5f;

        /// <summary>Halvings a held-back ground clamp is searched with: 0.3 m to about a third of a millimetre.</summary>
        private const int SnapSearchSteps = 10;

        /// <summary>
        /// Highest a held-back ground clamp may leave a pawn above a mover it stands on: half the ground tolerance, so the
        /// next tick's ride probe still finds the platform with room for rounding.
        /// </summary>
        private const float HeldOnMoverLimit = 0.01f;

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
            return Step(in input, in state, profile, world, simTick, deltaSeconds, true);
        }

        /// <summary>
        /// <see cref="Step(in PawnInput, in PawnState, MovementProfile, CollisionWorld, uint, float)"/>, with the
        /// joint corner resolution switchable so tests can replay the one-contact-at-a-time motor alone.
        /// </summary>
        internal static PawnState Step(
            in PawnInput input,
            in PawnState state,
            MovementProfile profile,
            CollisionWorld world,
            uint simTick,
            float deltaSeconds,
            bool resolveCorners) {
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
                (float)(state.Position.X + ride.X),
                (float)(state.Position.Y + ride.Y),
                (float)(state.Position.Z + ride.Z));
            Vector3 stepStart = position;

            float velocityX;
            float velocityZ;

            if (startedGrounded) {
                velocityX = (float)(moveDirection.X * gaitSpeed);
                velocityZ = (float)(moveDirection.Y * gaitSpeed);
            } else {
                velocityX = state.Velocity.X;
                velocityZ = state.Velocity.Z;
                StepAirVelocity(ref velocityX, ref velocityZ, moveDirection, gaitSpeed, profile, deltaSeconds);
            }

            var velocity = new Vector3(velocityX, StepVerticalVelocity(in input, in state, profile, deltaSeconds), velocityZ);

            Integrate(ref position, ref velocity, startedGrounded, profile, world, simTick, deltaSeconds, resolveCorners);
            float unsnappedY = position.Y;
            bool isGrounded = ResolveGrounding(ref position, ref velocity, startedGrounded, profile, world, simTick, out bool onMover);
            float strideLimit = (float)(profile.MaxSpeed * deltaSeconds);
            if (resolveCorners && isGrounded && position.Y < unsnappedY) {
                LimitSnapDown(ref position, unsnappedY, onMover, profile, world, simTick);
            }

            if (resolveCorners && isGrounded && position.Y > unsnappedY) {
                ClearSnapUp(ref position, ref velocity, unsnappedY, in stepStart, strideLimit, profile, world, simTick);
            }

            bool jumped = input.Jump && startedGrounded;
            if (resolveCorners && startedGrounded && !isGrounded && !jumped && velocity.Y <= 0f) {
                SettleOffLedge(ref position, ref velocity, in stepStart, strideLimit, profile, world, simTick);
            }

            float yawDegrees = ResolveYaw(moveDirection, state.YawDegrees);
            // Presentation bits are not the motor's to decide; carry them like the look pitch.
            byte flags = (byte)(PawnState.PackFlags(input.Gait, input.Crouch, isGrounded) | (state.Flags & PawnState.HoldingPropBit));

            return new PawnState(position, yawDegrees, velocity, flags, PawnState.WorldCarrierId, state.LookPitchDegrees);
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

            float probeTop = (float)(state.Position.Y + GroundTolerance);
            float probeBottom = (float)(state.Position.Y - GroundTolerance);
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
            float deltaSeconds,
            bool resolveCorners) {
            var displacement = new Vector3(
                (float)(velocity.X * deltaSeconds),
                (float)(velocity.Y * deltaSeconds),
                (float)(velocity.Z * deltaSeconds));
            int substepCount = ResolveSubstepCount(in displacement, (float)(profile.CapsuleRadius * 0.5f));
            float substepSeconds = (float)(deltaSeconds / substepCount);
            Span<CollisionContact> movedContacts = stackalloc CollisionContact[CollisionWorld.MaxContacts];

            for (int substep = 0; substep < substepCount; substep++) {
                Vector3 substepStart = position;
                position = new Vector3(
                    (float)(position.X + (float)(velocity.X * substepSeconds)),
                    (float)(position.Y + (float)(velocity.Y * substepSeconds)),
                    (float)(position.Z + (float)(velocity.Z * substepSeconds)));
                Vector3 moved = position;
                Vector3 movedVelocity = velocity;
                bool exhausted = Depenetrate(ref position, ref velocity, startedGrounded, profile, world, simTick, movedContacts, out int movedCount);
                if (!exhausted || !resolveCorners) {
                    continue;
                }

                var substepMove = new PawnSubstep(substepStart, moved, movedVelocity);
                ResolveCorner(ref position, ref velocity, in substepMove, movedContacts.Slice(0, movedCount), startedGrounded, profile, world, simTick);
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

            float lengthSquared = (float)((float)((float)(displacement.X * displacement.X)
                + (float)(displacement.Y * displacement.Y))
                + (float)(displacement.Z * displacement.Z));
            int substepCount = 1;

            while (substepCount < MaxSubsteps) {
                float reach = (float)(substepLimit * substepCount);
                if (lengthSquared <= (float)(reach * reach)) {
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
        /// <returns>True when every pass was spent, so the capsule may still overlap something.</returns>
        /// <remarks>
        /// One contact per pass, re-collected each time, rather than every contact in one sweep: two
        /// shapes reporting overlaps against the same pre-push position would each ask for their full
        /// depth and together push a pawn resting in a seam twice as far as either needed. Resolving the
        /// deepest and looking again costs an extra query and never overshoots. The first pass's contacts — those
        /// where the move put the pawn — are left in <paramref name="firstContacts"/> when it is not empty, for a
        /// corner resolution starting from the same place.
        /// </remarks>
        private static bool Depenetrate(
            ref Vector3 position,
            ref Vector3 velocity,
            bool startedGrounded,
            MovementProfile profile,
            CollisionWorld world,
            uint simTick,
            Span<CollisionContact> firstContacts,
            out int firstCount) {
            firstCount = 0;
            for (int iteration = 0; iteration < MaxDepenetrationIterations; iteration++) {
                CapsulePose pose = BuildPose(in position, profile, startedGrounded);
                CollisionContact contact;
                bool found;
                if (iteration == 0 && firstContacts.Length > 0) {
                    firstCount = world.CollectContacts(in pose, simTick, firstContacts);
                    found = TryPickDeepest(firstContacts.Slice(0, firstCount), out contact);
                } else {
                    found = TryFindDeepestContact(in pose, world, simTick, out contact);
                }

                if (!found) {
                    return false;
                }

                if (startedGrounded) {
                    ApplyWallResponse(ref position, ref velocity, in contact);
                    continue;
                }

                ApplyFreeResponse(ref position, ref velocity, in contact);
            }

            return true;
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
            float shortened = MathF.Max((float)(profile.CapsuleHeight - lift), (float)(profile.CapsuleRadius * 2f));
            var raised = new Vector3(position.X, (float)(position.Y + lift), position.Z);
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
            return TryPickDeepest(contacts.Slice(0, contactCount), out deepest);
        }

        /// <summary>The deepest of <paramref name="contacts"/>, ties keeping the earlier one.</summary>
        private static bool TryPickDeepest(ReadOnlySpan<CollisionContact> contacts, out CollisionContact deepest) {
            deepest = default;
            bool found = false;

            for (int index = 0; index < contacts.Length; index++) {
                if (found && contacts[index].Depth <= deepest.Depth) {
                    continue;
                }

                deepest = contacts[index];
                found = true;
            }

            return found;
        }

        /// <summary>
        /// Re-resolves a substep the deepest-first passes left more than <see cref="CornerTolerance"/> deep, jointly
        /// against every contact, from where the substep's move put the pawn.
        /// </summary>
        /// <remarks>
        /// <para>
        /// In an acute concave corner each face's push drives the capsule into the other, so one contact at a time
        /// ping-pongs and the overlap grows as the pawn keeps walking in. Here every contact is a half-space on the
        /// push instead, horizontal for a grounded pawn exactly as <see cref="ApplyWallResponse"/> shapes it, and
        /// <see cref="PawnContactSolver"/> finds the shortest push satisfying all of them, re-collecting contacts
        /// where each push lands (see <see cref="PawnCornerConstraints"/>).
        /// </para>
        /// <para>
        /// A push longer than the move plus the overlap the substep started with (and the tolerance, for curved
        /// faces whose tangent planes the start does not quite satisfy), or one that would leave the pawn further
        /// than that from where the substep started, is refused: from a clear start the true answer is never longer
        /// than the move, so anything more is a gap the capsule does not fit, solved as a far-off vertex. Then the
        /// passes' result stands when it is no deeper than the start (a pawn spawned inside, or a moving shape pushed
        /// into it), and otherwise the move is undone (see <see cref="UndoMove"/>).
        /// </para>
        /// </remarks>
        private static void ResolveCorner(
            ref Vector3 position,
            ref Vector3 velocity,
            in PawnSubstep substep,
            ReadOnlySpan<CollisionContact> movedContacts,
            bool startedGrounded,
            MovementProfile profile,
            CollisionWorld world,
            uint simTick) {
            float residual = DeepestDepthAt(in position, startedGrounded, profile, world, simTick);
            if (residual <= CornerTolerance) {
                return;
            }

            float startResidual = DeepestDepthAt(in substep.Start, startedGrounded, profile, world, simTick);
            float reach = (float)((float)(Distance(in substep.Moved, in substep.Start) + startResidual) + CornerTolerance);
            Span<Vector3> rows = stackalloc Vector3[PawnCornerConstraints.Capacity];
            Span<float> bounds = stackalloc float[PawnCornerConstraints.Capacity];
            Span<int> keys = stackalloc int[PawnCornerConstraints.Capacity];
            Span<CollisionContact> contacts = stackalloc CollisionContact[CollisionWorld.MaxContacts];
            var constraints = new PawnCornerConstraints(rows, bounds, keys);
            if (TryPushJointly(in substep, reach, startedGrounded, profile, world, simTick, movedContacts, contacts, ref constraints, out Vector3 resolved)) {
                position = resolved;
                velocity = ClipVelocity(in substep.MovedVelocity, constraints.Rows);
                return;
            }

            if (residual <= startResidual) {
                return;
            }

            UndoMove(ref position, ref velocity, in substep, startResidual, startedGrounded, profile, world, simTick);
        }

        /// <summary>
        /// Up to <see cref="CornerPasses"/> joint pushes from where the substep's move put the pawn, each solving the
        /// held constraint set for the total push. False when the set has no push of at most <paramref name="reach"/>,
        /// when a push would leave the pawn further than <paramref name="reach"/> from where the substep started, or
        /// when the last push still leaves more than <see cref="CornerTolerance"/> of overlap. The first pass reuses
        /// <paramref name="movedContacts"/>, the contacts already collected where the move put the pawn.
        /// </summary>
        private static bool TryPushJointly(
            in PawnSubstep substep,
            float reach,
            bool startedGrounded,
            MovementProfile profile,
            CollisionWorld world,
            uint simTick,
            ReadOnlySpan<CollisionContact> movedContacts,
            Span<CollisionContact> contacts,
            ref PawnCornerConstraints constraints,
            out Vector3 resolved) {
            float reachSquared = (float)(reach * reach);
            Vector3 moved = substep.Moved;
            Vector3 push = Vector3.Zero;
            resolved = moved;

            for (int pass = 0; pass < CornerPasses; pass++) {
                ReadOnlySpan<CollisionContact> found = movedContacts;
                if (pass > 0) {
                    CapsulePose pose = BuildPose(in resolved, profile, startedGrounded);
                    found = contacts.Slice(0, world.CollectContacts(in pose, simTick, contacts));
                }

                if (DeepestDepth(found) <= CornerSettledDepth) {
                    constraints.DropInactive(in push);
                    return true;
                }

                constraints.Merge(found, in push, startedGrounded);
                if (!constraints.TrySolve(out push) || PawnContactSolver.Dot(in push, in push) > reachSquared) {
                    return false;
                }

                resolved = new Vector3((float)(moved.X + push.X), (float)(moved.Y + push.Y), (float)(moved.Z + push.Z));
                if (Distance(in resolved, in substep.Start) > reach) {
                    return false;
                }
            }

            constraints.DropInactive(in push);
            return DeepestDepthAt(in resolved, startedGrounded, profile, world, simTick) <= CornerTolerance;
        }

        /// <summary>
        /// Takes back a substep's move into a gap the capsule does not fit. A standing pawn stays where the substep
        /// started, on the ground it stood on. For a falling one the vertical part is kept when, resolved on its own,
        /// it ends no deeper than the substep began, so a pawn stopped at a gap's mouth still falls and lands;
        /// otherwise it stays where the substep started. Either way it stops horizontally.
        /// </summary>
        private static void UndoMove(
            ref Vector3 position,
            ref Vector3 velocity,
            in PawnSubstep substep,
            float startResidual,
            bool startedGrounded,
            MovementProfile profile,
            CollisionWorld world,
            uint simTick) {
            if (startedGrounded) {
                position = substep.Start;
                velocity = new Vector3(0f, substep.MovedVelocity.Y, 0f);
                return;
            }

            var dropped = new Vector3(substep.Start.X, substep.Moved.Y, substep.Start.Z);
            var droppedVelocity = new Vector3(0f, substep.MovedVelocity.Y, 0f);
            Depenetrate(ref dropped, ref droppedVelocity, startedGrounded, profile, world, simTick, Span<CollisionContact>.Empty, out int _);
            float allowed = MathF.Max(startResidual, CornerTolerance);
            if (DeepestDepthAt(in dropped, startedGrounded, profile, world, simTick) <= allowed) {
                position = dropped;
                velocity = droppedVelocity;
                return;
            }

            position = substep.Start;
            velocity = Vector3.Zero;
        }

        /// <summary>
        /// The smallest change to <paramref name="velocity"/> that leaves it heading into none of the constraint
        /// rows — a projection, so it never adds speed — with the airborne rule that a contact never hands a
        /// descending pawn upward speed. Components left within rounding of zero are made exactly zero.
        /// </summary>
        private static Vector3 ClipVelocity(in Vector3 velocity, ReadOnlySpan<Vector3> rows) {
            Span<float> bounds = stackalloc float[PawnCornerConstraints.Capacity];
            for (int index = 0; index < rows.Length; index++) {
                bounds[index] = -PawnContactSolver.Dot(in rows[index], in velocity);
            }

            if (!PawnContactSolver.TrySolve(rows, bounds.Slice(0, rows.Length), out Vector3 change)) {
                return Vector3.Zero;
            }

            float speed = (float)((float)(MathF.Abs(velocity.X) + MathF.Abs(velocity.Y)) + MathF.Abs(velocity.Z));
            float snap = (float)(ClipSnapShare * speed);
            var clipped = new Vector3(
                SnapToZero((float)(velocity.X + change.X), snap),
                SnapToZero((float)(velocity.Y + change.Y), snap),
                SnapToZero((float)(velocity.Z + change.Z), snap));
            if (velocity.Y <= 0f && clipped.Y > 0f) {
                clipped.Y = 0f;
            }

            return clipped;
        }

        private static float SnapToZero(float value, float snap) {
            return MathF.Abs(value) <= snap ? 0f : value;
        }

        private static float HorizontalDistance(in Vector3 from, in Vector3 to) {
            float x = (float)(from.X - to.X);
            float z = (float)(from.Z - to.Z);
            return (float)MathF.Sqrt((float)((float)(x * x) + (float)(z * z)));
        }

        private static float Distance(in Vector3 from, in Vector3 to) {
            float x = (float)(from.X - to.X);
            float y = (float)(from.Y - to.Y);
            float z = (float)(from.Z - to.Z);
            return (float)MathF.Sqrt((float)((float)((float)(x * x) + (float)(y * y)) + (float)(z * z)));
        }

        /// <summary>The deepest overlap the capsule has at <paramref name="position"/>, or zero when it is clear.</summary>
        private static float DeepestDepthAt(in Vector3 position, bool startedGrounded, MovementProfile profile, CollisionWorld world, uint simTick) {
            CapsulePose pose = BuildPose(in position, profile, startedGrounded);
            return world.DeepestContactDepth(in pose, simTick);
        }

        private static float DeepestDepth(ReadOnlySpan<CollisionContact> contacts) {
            float deepest = 0f;
            for (int index = 0; index < contacts.Length; index++) {
                deepest = MathF.Max(deepest, contacts[index].Depth);
            }

            return deepest;
        }

        /// <summary>
        /// The half-space row a contact puts on a push: the full normal for an airborne pawn, and for a grounded one
        /// the horizontal direction scaled so that a unit of depth asks for the same horizontal push
        /// <see cref="ApplyWallResponse"/> would make, slant clamp included.
        /// </summary>
        internal static Vector3 BuildCornerRow(in CollisionContact contact, bool startedGrounded) {
            if (!startedGrounded) {
                return contact.Normal;
            }

            float horizontalSquared = (float)((float)(contact.Normal.X * contact.Normal.X) + (float)(contact.Normal.Z * contact.Normal.Z));
            if (horizontalSquared <= WallNormalEpsilon) {
                return contact.Normal;
            }

            float horizontalLength = (float)MathF.Sqrt(horizontalSquared);
            float slant = (float)(MathF.Max(horizontalLength, MinimumWallSlant) / horizontalLength);
            return new Vector3((float)(contact.Normal.X * slant), 0f, (float)(contact.Normal.Z * slant));
        }

        /// <summary>
        /// Resolves a contact found by the grounded pawn's raised capsule. Everything that capsule can
        /// touch is a wall by construction — low ledges cannot reach it — so the push and the slide both
        /// happen in the horizontal plane and a walking pawn is never lifted or dropped by one.
        /// </summary>
        private static void ApplyWallResponse(ref Vector3 position, ref Vector3 velocity, in CollisionContact contact) {
            float horizontalSquared = (float)((float)(contact.Normal.X * contact.Normal.X) + (float)(contact.Normal.Z * contact.Normal.Z));
            if (horizontalSquared <= WallNormalEpsilon) {
                ApplyFreeResponse(ref position, ref velocity, in contact);
                return;
            }

            float horizontalLength = (float)MathF.Sqrt(horizontalSquared);
            float pushX = (float)(contact.Normal.X / horizontalLength);
            float pushZ = (float)(contact.Normal.Z / horizontalLength);
            float distance = (float)(contact.Depth / MathF.Max(horizontalLength, MinimumWallSlant));
            position = new Vector3((float)(position.X + (float)(pushX * distance)), position.Y, (float)(position.Z + (float)(pushZ * distance)));

            float into = (float)((float)(velocity.X * pushX) + (float)(velocity.Z * pushZ));
            if (into >= 0f) {
                return;
            }

            velocity = new Vector3((float)(velocity.X - (float)(pushX * into)), velocity.Y, (float)(velocity.Z - (float)(pushZ * into)));
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
                (float)(position.X + (float)(contact.Normal.X * contact.Depth)),
                (float)(position.Y + (float)(contact.Normal.Y * contact.Depth)),
                (float)(position.Z + (float)(contact.Normal.Z * contact.Depth)));

            float incomingY = velocity.Y;
            float intoXY = (float)((float)(velocity.X * contact.Normal.X) + (float)(velocity.Y * contact.Normal.Y));
            float into = (float)(intoXY + (float)(velocity.Z * contact.Normal.Z));
            if (into < 0f) {
                velocity = new Vector3(
                    (float)(velocity.X - (float)(contact.Normal.X * into)),
                    (float)(velocity.Y - (float)(contact.Normal.Y * into)),
                    (float)(velocity.Z - (float)(contact.Normal.Z * into)));
            }

            if (incomingY <= 0f && velocity.Y > 0f) {
                velocity.Y = 0f;
            }
        }

        /// <summary>
        /// Holds back a downward ground clamp that would put the standing capsule (feet raised by the step offset)
        /// more than <see cref="CornerTolerance"/> into something it was clear of before the clamp: stepping down
        /// beside a rock whose top was below the raised feet.
        /// </summary>
        /// <remarks>
        /// The feet stop where that capsule just clears, found by halving the clamp a fixed
        /// <see cref="SnapSearchSteps"/> times, so a pawn beside such a rock stands on its edge until it walks
        /// clear rather than being thrown sideways off it. Held feet are never lower than before the clamp, so
        /// nothing accumulates from tick to tick. A clamp that leaves the capsule clear is untouched. Over a mover the
        /// hold stops at <see cref="HeldOnMoverLimit"/>, where the next tick's ride probe still finds the platform.
        /// </remarks>
        private static void LimitSnapDown(ref Vector3 position, float unsnappedY, bool onMover, MovementProfile profile, CollisionWorld world, uint simTick) {
            float snappedDepth = DeepestDepthAt(in position, true, profile, world, simTick);
            if (snappedDepth <= CornerTolerance) {
                return;
            }

            var unsnapped = new Vector3(position.X, unsnappedY, position.Z);
            float allowed = MathF.Max(DeepestDepthAt(in unsnapped, true, profile, world, simTick), CornerTolerance);
            if (snappedDepth <= allowed) {
                return;
            }

            float heldY = SearchClearHeight(in position, unsnappedY, position.Y, true, allowed, profile, world, simTick);
            position.Y = onMover ? MathF.Min(heldY, (float)(position.Y + HeldOnMoverLimit)) : heldY;
        }

        /// <summary>
        /// An upward ground clamp — the step onto a ledge or up a slope — that lifts the standing capsule more than
        /// <see cref="CornerTolerance"/> into something overhead it was clear of before is followed by the push-out a
        /// walking pawn gets, horizontal and against every contact at once, as it would be a tick later.
        /// </summary>
        /// <remarks>
        /// Holding the clamp back instead would leave the feet below the surface, and the next tick's gravity would
        /// sink them further. A push that would carry the pawn further this step than <paramref name="strideLimit"/>
        /// from <paramref name="stepStart"/>, or leave it no clearer, is not taken and the clamp stands as it was.
        /// </remarks>
        private static void ClearSnapUp(
            ref Vector3 position,
            ref Vector3 velocity,
            float unsnappedY,
            in Vector3 stepStart,
            float strideLimit,
            MovementProfile profile,
            CollisionWorld world,
            uint simTick) {
            float snappedDepth = DeepestDepthAt(in position, true, profile, world, simTick);
            if (snappedDepth <= CornerTolerance) {
                return;
            }

            var unsnapped = new Vector3(position.X, unsnappedY, position.Z);
            if (snappedDepth <= DeepestDepthAt(in unsnapped, true, profile, world, simTick)) {
                return;
            }

            Vector3 snapped = position;
            Vector3 cleared = position;
            Vector3 clearedVelocity = velocity;
            Span<CollisionContact> snappedContacts = stackalloc CollisionContact[CollisionWorld.MaxContacts];
            if (Depenetrate(ref cleared, ref clearedVelocity, true, profile, world, simTick, snappedContacts, out int snappedCount)) {
                var stay = new PawnSubstep(snapped, snapped, velocity);
                ResolveCorner(ref cleared, ref clearedVelocity, in stay, snappedContacts.Slice(0, snappedCount), true, profile, world, simTick);
            }

            float allowedStride = MathF.Max(strideLimit, HorizontalDistance(in snapped, in stepStart));
            bool isClearer = DeepestDepthAt(in cleared, true, profile, world, simTick) < snappedDepth;
            if (!isClearer || HorizontalDistance(in cleared, in stepStart) > allowedStride || cleared.Y != snapped.Y) {
                return;
            }

            position = cleared;
            velocity = clearedVelocity;
        }

        /// <summary>
        /// A pawn that walked off its support this step changes from the shortened capsule to its whole one, whose
        /// lower part — a step offset tall — may already be inside something the shortened one passed over: a low
        /// object beside a ledge it walked off, or a steep bump it cannot stand on. When the whole capsule is more
        /// than <see cref="CornerTolerance"/> inside, it is settled now rather than pushed a tick later.
        /// </summary>
        /// <remarks>
        /// First choice is the push the airborne passes would make on the next tick, taken now when it clears and
        /// keeps the step within <paramref name="strideLimit"/> of <paramref name="stepStart"/>. Otherwise, when
        /// nothing at all lies under the pawn's centre within a step offset above its feet — a walk-off, not a bump —
        /// it is lifted straight up to the lowest height that clears, by halving a fixed
        /// <see cref="SnapSearchSteps"/> times. Otherwise the step's result stands, as it did before. With open air
        /// required under the centre, a lift never hoists a pawn up a steep face; neither runs on a jump.
        /// </remarks>
        private static void SettleOffLedge(
            ref Vector3 position,
            ref Vector3 velocity,
            in Vector3 stepStart,
            float strideLimit,
            MovementProfile profile,
            CollisionWorld world,
            uint simTick) {
            if (DeepestDepthAt(in position, false, profile, world, simTick) <= CornerTolerance) {
                return;
            }

            Vector3 pushed = position;
            Vector3 pushedVelocity = velocity;
            Span<CollisionContact> wholeContacts = stackalloc CollisionContact[CollisionWorld.MaxContacts];
            if (Depenetrate(ref pushed, ref pushedVelocity, false, profile, world, simTick, wholeContacts, out int wholeCount)) {
                var stay = new PawnSubstep(position, position, velocity);
                ResolveCorner(ref pushed, ref pushedVelocity, in stay, wholeContacts.Slice(0, wholeCount), false, profile, world, simTick);
            }

            float allowedStride = MathF.Max(strideLimit, HorizontalDistance(in position, in stepStart));
            bool pushClears = DeepestDepthAt(in pushed, false, profile, world, simTick) <= CornerTolerance;
            if (pushClears && HorizontalDistance(in pushed, in stepStart) <= allowedStride) {
                position = pushed;
                velocity = pushedVelocity;
                return;
            }

            var lifted = new Vector3(position.X, (float)(position.Y + profile.StepOffset), position.Z);
            if (DeepestDepthAt(in lifted, false, profile, world, simTick) > CornerTolerance) {
                return;
            }

            if (world.TryGetSupport(position.X, position.Z, lifted.Y, position.Y, simTick, out SupportHit _)) {
                return;
            }

            position.Y = SearchClearHeight(in position, lifted.Y, position.Y, false, CornerTolerance, profile, world, simTick);
        }

        /// <summary>
        /// The height nearest <paramref name="blockedY"/> on the way from <paramref name="clearY"/> at which the capsule
        /// (standing or whole) overlaps no more than <paramref name="allowed"/>, by halving the span a fixed
        /// <see cref="SnapSearchSteps"/> times; <paramref name="clearY"/> itself must already be that clear.
        /// </summary>
        private static float SearchClearHeight(
            in Vector3 position,
            float clearY,
            float blockedY,
            bool standing,
            float allowed,
            MovementProfile profile,
            CollisionWorld world,
            uint simTick) {
            for (int step = 0; step < SnapSearchSteps; step++) {
                float middleY = (float)((float)(clearY + blockedY) * 0.5f);
                var middle = new Vector3(position.X, middleY, position.Z);
                if (DeepestDepthAt(in middle, standing, profile, world, simTick) <= allowed) {
                    clearY = middleY;
                } else {
                    blockedY = middleY;
                }
            }

            return clearY;
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
        /// Walkability compares the surface normal's vertical component against the profile's cached
        /// <see cref="MovementProfile.WalkableNormalY"/>, so no trigonometry runs here.
        /// </para>
        /// </remarks>
        private static bool ResolveGrounding(
            ref Vector3 position,
            ref Vector3 velocity,
            bool startedGrounded,
            MovementProfile profile,
            CollisionWorld world,
            uint simTick,
            out bool onMover) {
            onMover = false;
            if (velocity.Y > 0f) {
                return false;
            }

            float probeTop = (float)(position.Y + profile.StepOffset);
            float probeBottom = (float)(position.Y - profile.StepOffset);
            if (!world.TryGetSupport(position.X, position.Z, probeTop, probeBottom, simTick, out SupportHit hit)) {
                return false;
            }

            if (hit.Normal.Y <= 0f || hit.Normal.Y < profile.WalkableNormalY) {
                return false;
            }

            float rise = (float)(hit.Height - position.Y);
            if (rise > ReachAbove(profile, startedGrounded) || rise < -ReachBelow(profile, startedGrounded, hit.Normal.Y)) {
                return false;
            }

            position.Y = hit.Height;
            velocity.Y = 0f;
            onMover = hit.IsMover;
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

            float sag = (float)((float)(profile.CapsuleRadius / normalY) - profile.CapsuleRadius);
            return (float)(GroundTolerance + MathF.Min(sag, profile.StepOffset));
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
            bool hasIntent = LengthSquared(moveDirection) >= (float)(MoveDeadZone * MoveDeadZone);

            if (!hasIntent) {
                if (profile.AirDrag > 0f) {
                    float decay = profile.GetAirDragDecay(deltaSeconds);
                    velocityX = (float)(velocityX * decay);
                    velocityZ = (float)(velocityZ * decay);
                }

                return;
            }

            float targetX = (float)(moveDirection.X * gaitSpeed);
            float targetZ = (float)(moveDirection.Y * gaitSpeed);
            float deltaX = (float)(targetX - velocityX);
            float deltaZ = (float)(targetZ - velocityZ);
            float maxStep = (float)(profile.AirAcceleration * deltaSeconds);
            float distanceSquared = (float)((float)(deltaX * deltaX) + (float)(deltaZ * deltaZ));

            if (distanceSquared <= (float)(maxStep * maxStep)) {
                velocityX = targetX;
                velocityZ = targetZ;
                return;
            }

            float distance = (float)MathF.Sqrt(distanceSquared);
            velocityX = (float)(velocityX + (float)((float)(deltaX / distance) * maxStep));
            velocityZ = (float)(velocityZ + (float)((float)(deltaZ / distance) * maxStep));
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

            return (float)(velocityY + (float)(profile.Gravity * deltaSeconds));
        }

        /// <summary>
        /// Faces the direction of travel, or holds the previous facing when there is no travel. Yaw is
        /// measured clockwise from +Z, matching the engine's convention.
        /// </summary>
        private static float ResolveYaw(Vector2 moveDirection, float previousYawDegrees) {
            if (LengthSquared(moveDirection) < (float)(MoveDeadZone * MoveDeadZone)) {
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
            float lengthSquared = LengthSquared(moveDirection);

            if (lengthSquared <= 1f) {
                return moveDirection;
            }

            float length = (float)MathF.Sqrt(lengthSquared);
            return new Vector2((float)(moveDirection.X / length), (float)(moveDirection.Y / length));
        }

        // x² + y² with each step narrowed, instead of Vector2.LengthSquared, whose accumulation is the runtime's.
        private static float LengthSquared(Vector2 value) {
            float xx = (float)(value.X * value.X);
            float yy = (float)(value.Y * value.Y);
            return (float)(xx + yy);
        }
    }
}
