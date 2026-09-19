using UnityEngine;

namespace AlpineLib.Actors.Locomotion {
    /// <summary>
    /// One fixed step of kinematic capsule movement: walk or steer in the air, sweep-and-slide through
    /// the world, resolve the vertical, decide grounding once from the final pose, and put the result
    /// back into the frame of whatever the capsule is now standing on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pure function of state, input and the collision query — no transform is read or written here.
    /// The state lives in the carrier's frame; each step converts to the world at the carrier's current
    /// pose, does all of its sweeps there, and converts back. The carrier's own motion between steps is
    /// therefore never swept: a body parented under a moving deck is already where the deck took it, and
    /// only the metre it walked itself is ever tested against geometry.
    /// </para>
    /// <para>
    /// Grounding is decided once per step, at the end, by a downward probe from the resolved pose. That is
    /// the whole cure for the flicker a character controller's "did the last move touch the floor" flag
    /// produces after a purely horizontal move: there is one answer per step and every consumer reads it.
    /// </para>
    /// <para>
    /// Steps and ledges are climbed by the capsule's rounded end. A sweep that touches the top edge of a
    /// step reports a contact normal that leans away from the edge, which would read as a wall; the face
    /// behind the edge is probed instead, and an edge no higher than the step offset over a walkable face
    /// is walkable — sliding along its contact normal rides the capsule up and over. The same probe is
    /// what keeps a capsule resting across a gap between two decks grounded.
    /// </para>
    /// <para>
    /// The carrier is sticky through the air: a hop on a deck keeps the deck's frame, and only landing on
    /// something else changes it. Stepping off a moving carrier onto the ground hands the carrier's
    /// velocity to the walker, so the momentum is real rather than lost.
    /// </para>
    /// </remarks>
    public sealed class CapsuleMotor {
        private const int MaxSlideIterations = 4;
        private const int MaxDepenetrationPasses = 2;
        private const float SnapEpsilon = 0.001f;
        private const float MinSweep = 1e-5f;
        private const float EdgeProbeInset = 0.02f;
        private const float EdgeProbeHeight = 0.05f;
        private const float MinSkinCosine = 0.2f;

        private readonly ICapsuleCollisionQuery _query;

        public CapsuleMotor(ICapsuleCollisionQuery query) {
            _query = query;
        }

        /// <summary>
        /// Advances the state by one step.
        /// </summary>
        /// <param name="carrierVelocity">World velocity of the state's current carrier, zero for the world.</param>
        public void Step(ref MotorState state, in MotorInput input, in MotorSettings settings, Vector3 carrierVelocity, float deltaTime) {
            Transform carrier = state.CarrierRoot;
            Vector3 position = CarrierFrame.ToWorldPoint(carrier, state.LocalPosition);
            Vector3 planar = CarrierFrame.ToWorldPlanar(carrier, state.LocalPlanarVelocity);
            float vertical = state.VerticalVelocity;
            bool grounded = state.Grounded;
            Vector3 normal = state.Grounded ? state.GroundNormal : Vector3.up;
            float cosSlope = Mathf.Cos(settings.SlopeLimitDegrees * Mathf.Deg2Rad);
            CapsuleShape sweepShape = settings.Capsule.Shrunk(settings.SkinWidth);

            planar = ResolvePlanarVelocity(planar, grounded, in input, in settings, deltaTime);
            bool jumped = input.Jump && grounded;
            vertical = ResolveVerticalVelocity(vertical, grounded, jumped, in settings, deltaTime);
            if (jumped) grounded = false;

            Vector3 horizontal = planar * deltaTime;
            if (grounded) horizontal = AlongGround(horizontal, normal);
            horizontal += input.PendingDisplacement;
            MoveAndSlide(ref position, horizontal, in sweepShape, grounded, cosSlope, in settings);

            ResolveVertical(ref position, ref vertical, grounded, in sweepShape, cosSlope, settings.SkinWidth, deltaTime);

            Transform support = carrier;
            bool wasGrounded = state.Grounded;
            grounded = ResolveGrounding(ref position, ref vertical, ref normal, ref support, vertical > 0f, wasGrounded,
                in sweepShape, cosSlope, in settings);

            Depenetrate(ref position, in settings.Capsule);

            Transform newCarrier = grounded ? (CarrierFrame.IsUsable(support) ? support : null) : carrier;
            if (newCarrier != carrier && newCarrier == null) planar += carrierVelocity;

            float worldYaw = CarrierFrame.Heading(carrier) + state.LocalYaw;
            state.CarrierRoot = newCarrier;
            state.LocalPosition = CarrierFrame.ToLocalPoint(newCarrier, position);
            state.LocalPlanarVelocity = CarrierFrame.ToLocalPlanar(newCarrier, planar);
            state.LocalYaw = worldYaw - CarrierFrame.Heading(newCarrier);
            state.VerticalVelocity = vertical;
            state.Grounded = grounded;
            state.GroundNormal = grounded ? normal : Vector3.up;
            state.JumpedThisStep = jumped;
        }

        /// <summary>
        /// True when the capsule at the foot point can grow by the missing height: the current capsule,
        /// thinned by the skin so the floor it stands on is not a hit, sweeps up that far and meets nothing.
        /// </summary>
        public bool HasHeadroom(Vector3 foot, in CapsuleShape current, float missingHeight, float skin) {
            if (missingHeight <= 0f) return true;

            CapsuleShape sweepShape = current.Shrunk(skin);
            return !_query.Sweep(in sweepShape, foot, Vector3.up, missingHeight + skin, out _);
        }

        /// <summary>
        /// Grounded walking is instant at the gait's full speed; airborne steering accelerates towards the
        /// input and coasts, or decays, when there is none.
        /// </summary>
        private static Vector3 ResolvePlanarVelocity(Vector3 planar, bool grounded, in MotorInput input, in MotorSettings settings, float deltaTime) {
            Vector3 target = input.MoveDirection * input.MoveSpeed;
            target.y = 0f;
            if (grounded) return target;

            if (input.MoveDirection != Vector3.zero) {
                return Vector3.MoveTowards(planar, target, settings.AirAcceleration * deltaTime);
            }

            if (settings.AirDrag <= 0f) return planar;

            return planar * Mathf.Exp(-settings.AirDrag * deltaTime);
        }

        private static float ResolveVerticalVelocity(float vertical, bool grounded, bool jumped, in MotorSettings settings, float deltaTime) {
            if (jumped) return settings.JumpSpeed;
            if (grounded) return 0f;

            return vertical + settings.Gravity * deltaTime;
        }

        /// <summary>Lays a planar stride along the ground plane so slopes are walked, not scraped.</summary>
        private static Vector3 AlongGround(Vector3 horizontal, Vector3 normal) {
            if (normal.y >= 1f - 1e-4f) return horizontal;

            float length = horizontal.magnitude;
            if (length < MinSweep) return horizontal;

            Vector3 along = Vector3.ProjectOnPlane(horizontal, normal);
            if (along.sqrMagnitude < MinSweep * MinSweep) return horizontal;

            return along.normalized * length;
        }

        /// <summary>
        /// Sweeps the displacement in, sliding along whatever it hits. A grounded body slides up a
        /// walkable edge and never up a wall.
        /// </summary>
        private void MoveAndSlide(ref Vector3 position, Vector3 delta, in CapsuleShape sweepShape, bool grounded, float cosSlope, in MotorSettings settings) {
            Vector3 remaining = delta;
            for (int iteration = 0; iteration < MaxSlideIterations; iteration++) {
                float length = remaining.magnitude;
                if (length < MinSweep) return;

                Vector3 direction = remaining / length;
                if (!_query.Sweep(in sweepShape, position, direction, length + settings.SkinWidth, out CollisionSweepHit hit)) {
                    position += remaining;
                    return;
                }

                float travel = Mathf.Max(hit.Distance - SkinAlong(hit.Normal, direction, settings.SkinWidth), 0f);
                position += direction * travel;
                remaining -= direction * travel;

                bool isWall = !IsWalkable(in hit, position, cosSlope, in settings, out _);
                remaining = Vector3.ProjectOnPlane(remaining, hit.Normal);
                if (isWall) remaining.y = Mathf.Min(remaining.y, 0f);
            }
        }

        /// <summary>
        /// How far short of a contact a sweep stops so the surface stays a skin away along its normal,
        /// however obliquely the sweep met it. Capped for grazing contacts so a near-parallel sweep is
        /// not held metres away.
        /// </summary>
        private static float SkinAlong(Vector3 normal, Vector3 direction, float skin) {
            float cosine = Mathf.Max(-Vector3.Dot(normal, direction), MinSkinCosine);
            return skin / cosine;
        }

        /// <summary>
        /// Whether a contact is something to stand on or walk over. A contact normal within the slope
        /// limit answers itself; a steeper one may be the capsule's rounded end on an edge, so the face
        /// behind the contact is probed, and it counts when that face is walkable and the edge is no
        /// higher above the feet than the step offset.
        /// </summary>
        private bool IsWalkable(in CollisionSweepHit hit, Vector3 foot, float cosSlope, in MotorSettings settings, out Vector3 surfaceNormal) {
            surfaceNormal = hit.Normal;
            if (hit.Normal.y >= cosSlope) return true;
            if (hit.Point.y - foot.y > settings.StepOffset + settings.SkinWidth) return false;

            Vector3 away = hit.Point - foot;
            away.y = 0f;
            if (away.sqrMagnitude < MinSweep * MinSweep) return false;

            Vector3 origin = hit.Point + away.normalized * EdgeProbeInset + Vector3.up * EdgeProbeHeight;
            if (!_query.TryProbeSurface(origin, EdgeProbeHeight * 2f, out Vector3 probed)) return false;
            if (probed.y < cosSlope) return false;

            surfaceNormal = probed;
            return true;
        }

        /// <summary>
        /// Rises against the ceiling, or falls against the floor: a fall that meets a surface too steep to
        /// stand on slides down it rather than hanging there. A grounded body has nothing to resolve.
        /// </summary>
        private void ResolveVertical(ref Vector3 position, ref float vertical, bool grounded, in CapsuleShape sweepShape, float cosSlope, float skin, float deltaTime) {
            float displacement = vertical * deltaTime;
            if (displacement > 0f) {
                if (!_query.Sweep(in sweepShape, position, Vector3.up, displacement + skin, out CollisionSweepHit ceiling)) {
                    position += Vector3.up * displacement;
                    return;
                }

                position += Vector3.up * Mathf.Max(ceiling.Distance - SkinAlong(ceiling.Normal, Vector3.up, skin), 0f);
                vertical = 0f;
                return;
            }

            if (grounded || displacement >= 0f) return;

            Vector3 remaining = Vector3.down * -displacement;
            for (int iteration = 0; iteration < MaxSlideIterations; iteration++) {
                float length = remaining.magnitude;
                if (length < MinSweep) return;

                Vector3 direction = remaining / length;
                if (!_query.Sweep(in sweepShape, position, direction, length + skin, out CollisionSweepHit floor)) {
                    position += remaining;
                    return;
                }

                float travel = Mathf.Max(floor.Distance - SkinAlong(floor.Normal, direction, skin), 0f);
                position += direction * travel;
                remaining -= direction * travel;
                if (floor.Normal.y >= cosSlope) return;

                remaining = Vector3.ProjectOnPlane(remaining, floor.Normal);
            }
        }

        /// <summary>
        /// The one grounding decision: a rising body is airborne; otherwise a probe reaches a step's depth
        /// while grounded, or a short landing distance while airborne, and a walkable surface within it
        /// grounds the body and snaps it onto the surface. Nothing found keeps the current carrier.
        /// </summary>
        private bool ResolveGrounding(ref Vector3 position, ref float vertical, ref Vector3 normal, ref Transform support, bool rising, bool wasGrounded, in CapsuleShape sweepShape, float cosSlope, in MotorSettings settings) {
            if (rising) return false;

            float skin = settings.SkinWidth;
            float probe = (wasGrounded ? settings.StepOffset : settings.LandingProbeDistance) + skin;
            if (!_query.Sweep(in sweepShape, position, Vector3.down, probe, out CollisionSweepHit ground)) return false;
            if (!IsWalkable(in ground, position, cosSlope, in settings, out Vector3 surface)) return false;

            float drop = ground.Distance - SkinAlong(ground.Normal, Vector3.down, skin);
            if (Mathf.Abs(drop) > SnapEpsilon) position += Vector3.down * drop;

            normal = surface;
            vertical = 0f;
            support = ground.Body;
            return true;
        }

        /// <summary>Safety net for geometry that moved into the capsule between steps.</summary>
        private void Depenetrate(ref Vector3 position, in CapsuleShape shape) {
            for (int pass = 0; pass < MaxDepenetrationPasses; pass++) {
                if (!_query.TryResolveOverlap(in shape, position, out Vector3 correction)) return;

                position += correction;
            }
        }
    }
}
