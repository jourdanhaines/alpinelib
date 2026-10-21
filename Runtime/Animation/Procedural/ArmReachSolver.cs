using System;
using UnityEngine;

namespace AlpineLib.Animation.Procedural {
    /// <summary>
    /// The arithmetic behind <see cref="ArmReachPoseModifier"/>: a two-bone arm solve, how far a carried
    /// pair must push out to clear the chest, and how much reach limits leave.
    /// </summary>
    /// <remarks>
    /// Pure and scene-free, so a gate can evaluate it. Any consistent space works; the modifier uses the
    /// actor's local space. Angles are degrees.
    /// </remarks>
    public static class ArmReachSolver {
        /// <summary>Gap kept from a fully folded or fully straight arm, in metres.</summary>
        public const float FoldMargin = 1e-4f;

        /// <summary>Share of the full reach a straight arm is allowed, so the elbow never locks.</summary>
        public const float StraightLimit = 0.999f;

        /// <summary>Sine of the bend below which an arm counts as straight and its bend plane as unknown.</summary>
        public const float StraightSine = 1e-3f;

        /// <summary>Length of the hand axis's part off the reach line below which it gives no swivel.</summary>
        public const float LeastBendMinimum = 1e-3f;

        /// <summary>Length of reach × palm below which the palm gives no least-roll swivel.</summary>
        public const float LeastRollMinimum = 1e-3f;

        /// <summary>Forearm roll, in degrees, the swivel refine weighs as one unit of cost.</summary>
        public const float RollReference = 40f;

        /// <summary>Wrist bend, in degrees, the swivel refine weighs as one unit of cost.</summary>
        public const float BendReference = 30f;

        /// <summary>Swivel, in degrees either way of the least-roll pole, the refine searches.</summary>
        public const float SwivelRefineDegrees = 40f;

        /// <summary>Golden-section steps of the swivel refine (about 0.25° left of the 80° span).</summary>
        public const int SwivelRefineSteps = 12;

        private const float MinimumLengthSquared = 1e-12f;
        private const float MinimumCosine = 0.05f;
        private const float GoldenSection = 0.618034f;
        private const float PairTolerance = 1e-6f;

        /// <summary>
        /// Elbow for an arm of <paramref name="upperLength"/> and <paramref name="lowerLength"/> from
        /// <paramref name="shoulder"/> toward <paramref name="target"/>, bent toward <paramref name="pole"/>.
        /// A target out of reach or too close is met as nearly as the arm allows, along the same line.
        /// </summary>
        public static Vector3 SolveElbow(Vector3 shoulder, Vector3 target, float upperLength, float lowerLength, Vector3 pole) {
            Vector3 toTarget = target - shoulder;
            float length = toTarget.magnitude;
            Vector3 direction = length * length > MinimumLengthSquared ? toTarget / length : AnyPerpendicular(pole);
            float distance = Mathf.Clamp(length, Mathf.Abs(upperLength - lowerLength) + FoldMargin, (upperLength + lowerLength) * StraightLimit);

            float cosine = (upperLength * upperLength + distance * distance - lowerLength * lowerLength) / (2f * upperLength * distance);
            cosine = Mathf.Clamp(cosine, -1f, 1f);
            float sine = Mathf.Sqrt(1f - cosine * cosine);

            Vector3 bend = pole - direction * Vector3.Dot(pole, direction);
            bend = bend.sqrMagnitude > MinimumLengthSquared ? bend.normalized : AnyPerpendicular(direction);
            return shoulder + upperLength * (direction * cosine + bend * sine);
        }

        /// <summary>
        /// Turns <paramref name="rotation"/> with its bone: the frame (<paramref name="fromDirection"/>,
        /// <paramref name="fromNormal"/>) onto (<paramref name="toDirection"/>, <paramref name="toNormal"/>),
        /// so the bone's twist about its length rides the bend plane.
        /// </summary>
        public static Quaternion Reorient(Vector3 fromDirection, Vector3 fromNormal, Vector3 toDirection, Vector3 toNormal, Quaternion rotation) {
            Quaternion from = Quaternion.LookRotation(fromDirection, fromNormal);
            Quaternion to = Quaternion.LookRotation(toDirection, toNormal);
            return to * Quaternion.Inverse(from) * rotation;
        }

        /// <summary>
        /// Bend-plane normal of a chain shoulder → elbow → hand, or <paramref name="fallback"/> when the
        /// chain is straight.
        /// </summary>
        public static Vector3 BendNormal(Vector3 shoulder, Vector3 elbow, Vector3 hand, Vector3 fallback) {
            return TryBendNormal(shoulder, elbow, hand, out Vector3 normal) ? normal : fallback;
        }

        /// <summary>
        /// Bend-plane normal of a chain shoulder → elbow → hand; false when the chain is straight to
        /// within <see cref="StraightSine"/>, where the plane is noise.
        /// </summary>
        public static bool TryBendNormal(Vector3 shoulder, Vector3 elbow, Vector3 hand, out Vector3 normal) {
            Vector3 upper = elbow - shoulder;
            Vector3 lower = hand - elbow;
            normal = Vector3.Cross(upper, lower);
            float limit = StraightSine * StraightSine * upper.sqrMagnitude * lower.sqrMagnitude;
            if (normal.sqrMagnitude <= Mathf.Max(limit, MinimumLengthSquared)) return false;

            normal.Normalize();
            return true;
        }

        /// <summary>
        /// A bend normal for a straight arm reaching along <paramref name="reach"/>, taken from the hand's
        /// own frame so the elbow bends the way the wrist is turned rather than an arbitrary way.
        /// </summary>
        public static Vector3 BendNormalFromHand(Vector3 reach, Quaternion handRotation) {
            Vector3 normal = Vector3.Cross(handRotation * Vector3.up, reach);
            if (normal.sqrMagnitude <= MinimumLengthSquared) normal = Vector3.Cross(handRotation * Vector3.forward, reach);
            return normal.sqrMagnitude > MinimumLengthSquared ? normal.normalized : AnyPerpendicular(reach);
        }

        /// <summary>
        /// Direction the elbow sits off the shoulder → hand line, for a chain reaching along
        /// <paramref name="reach"/> with bend normal <paramref name="bendNormal"/> (cross(upper, lower)).
        /// </summary>
        public static Vector3 PoleFromBend(Vector3 reach, Vector3 bendNormal) {
            return Vector3.Cross(reach, bendNormal);
        }

        /// <summary>
        /// Pole for an arm reaching from <paramref name="shoulder"/> to <paramref name="target"/>, moved
        /// by <paramref name="share"/> (0..1) from <paramref name="capturedPole"/> to the least-wrist-bend
        /// swivel: the elbow on the side the hand's long axis (<paramref name="handAxis"/>, unit) points
        /// away from, so the forearm lines up with the hand. Falls back to the captured pole when the hand
        /// axis runs along the reach line (part off it under <see cref="LeastBendMinimum"/>).
        /// </summary>
        public static Vector3 SwivelPole(Vector3 shoulder, Vector3 target, Vector3 handAxis, Vector3 capturedPole, float share) {
            Vector3 reach = target - shoulder;
            if (share <= 0f || reach.sqrMagnitude <= MinimumLengthSquared) return capturedPole;

            Vector3 direction = reach.normalized;
            Vector3 least = -handAxis - direction * Vector3.Dot(-handAxis, direction);
            if (least.magnitude < LeastBendMinimum) return capturedPole;

            return BlendPole(direction, capturedPole, least.normalized, share);
        }

        /// <summary>
        /// Pole for an arm reaching from <paramref name="shoulder"/> to <paramref name="target"/>, moved
        /// by <paramref name="share"/> (0..1) from <paramref name="capturedPole"/> to the least-roll swivel
        /// (<see cref="LeastRollPole"/> refined by <see cref="RefineSwivel"/>), so the forearm turns with
        /// the palm instead of twisting under it. Falls back to the least-wrist-bend rule when
        /// <paramref name="palmNormal"/> is zero or runs along the reach line.
        /// </summary>
        public static Vector3 SwivelPole(Vector3 shoulder, Vector3 target, float upperLength, float lowerLength, Vector3 handAxis, Vector3 palmNormal, Vector3 capturedPole, float share) {
            if (share <= 0f) return capturedPole;
            if (!TryLeastRollPole(shoulder, target, palmNormal, capturedPole, out Vector3 seed)) {
                return SwivelPole(shoulder, target, handAxis, capturedPole, share);
            }

            Vector3 least = RefineSwivel(shoulder, target, upperLength, lowerLength, palmNormal, handAxis, seed);
            return BlendPole((target - shoulder).normalized, capturedPole, least, share);
        }

        /// <summary>
        /// Closed-form least-roll pole: ±(reach × palm), off the reach line, signed to agree with
        /// <paramref name="referencePole"/> (which side the elbow hinges is the reference's to say). It lays
        /// the bend plane's normal along the palm, the bind relation of a rig without twist bones.
        /// <paramref name="referencePole"/> when the reach or palm is degenerate.
        /// </summary>
        public static Vector3 LeastRollPole(Vector3 shoulder, Vector3 target, Vector3 palmNormal, Vector3 referencePole) {
            return TryLeastRollPole(shoulder, target, palmNormal, referencePole, out Vector3 pole) ? pole : referencePole;
        }

        /// <summary>
        /// <see cref="LeastRollPole"/>, unit; false when the reach is degenerate or the palm runs along it
        /// (reach × palm under <see cref="LeastRollMinimum"/>).
        /// </summary>
        public static bool TryLeastRollPole(Vector3 shoulder, Vector3 target, Vector3 palmNormal, Vector3 referencePole, out Vector3 pole) {
            pole = referencePole;
            Vector3 reach = target - shoulder;
            if (reach.sqrMagnitude <= MinimumLengthSquared || palmNormal.sqrMagnitude <= MinimumLengthSquared) return false;

            Vector3 candidate = Vector3.Cross(reach.normalized, palmNormal.normalized);
            if (candidate.magnitude < LeastRollMinimum) return false;

            candidate.Normalize();
            pole = Vector3.Dot(candidate, referencePole) < 0f ? -candidate : candidate;
            return true;
        }

        /// <summary>
        /// Swings <paramref name="seedPole"/> about the reach line, within <see cref="SwivelRefineDegrees"/>,
        /// to the swivel that minimises (roll / <see cref="RollReference"/>)² + (wrist bend /
        /// <see cref="BendReference"/>)² (golden section, <see cref="SwivelRefineSteps"/> steps). Roll is
        /// measured against the hinge side the seed picks; bend is the angle between
        /// <paramref name="handAxis"/> and the forearm. Never returns a costlier pole than the seed.
        /// </summary>
        public static Vector3 RefineSwivel(Vector3 shoulder, Vector3 target, float upperLength, float lowerLength, Vector3 palmNormal, Vector3 handAxis, Vector3 seedPole) {
            Vector3 reach = target - shoulder;
            if (reach.sqrMagnitude <= MinimumLengthSquared) return seedPole;

            Vector3 axis = reach.normalized;
            float hingeSign = Vector3.Dot(Vector3.Cross(seedPole, axis), palmNormal) < 0f ? -1f : 1f;
            float low = -SwivelRefineDegrees;
            float high = SwivelRefineDegrees;
            float left = high - GoldenSection * (high - low);
            float right = low + GoldenSection * (high - low);
            float leftCost = SwivelCost(shoulder, target, upperLength, lowerLength, palmNormal, handAxis, seedPole, axis, hingeSign, left);
            float rightCost = SwivelCost(shoulder, target, upperLength, lowerLength, palmNormal, handAxis, seedPole, axis, hingeSign, right);
            for (int step = 0; step < SwivelRefineSteps; step++) {
                if (leftCost < rightCost) {
                    high = right;
                    right = left;
                    rightCost = leftCost;
                    left = high - GoldenSection * (high - low);
                    leftCost = SwivelCost(shoulder, target, upperLength, lowerLength, palmNormal, handAxis, seedPole, axis, hingeSign, left);
                } else {
                    low = left;
                    left = right;
                    leftCost = rightCost;
                    right = low + GoldenSection * (high - low);
                    rightCost = SwivelCost(shoulder, target, upperLength, lowerLength, palmNormal, handAxis, seedPole, axis, hingeSign, right);
                }
            }

            float best = (low + high) * 0.5f;
            float seedCost = SwivelCost(shoulder, target, upperLength, lowerLength, palmNormal, handAxis, seedPole, axis, hingeSign, 0f);
            float bestCost = SwivelCost(shoulder, target, upperLength, lowerLength, palmNormal, handAxis, seedPole, axis, hingeSign, best);
            return bestCost < seedCost ? Quaternion.AngleAxis(best, axis) * seedPole : seedPole;
        }

        /// <summary>
        /// Signed roll, in degrees, about <paramref name="axis"/> from <paramref name="from"/> to
        /// <paramref name="to"/>, both projected off it (Unity's <see cref="Vector3.SignedAngle"/> sign).
        /// Zero when either lies along the axis.
        /// </summary>
        public static float RollAbout(Vector3 axis, Vector3 from, Vector3 to) {
            Vector3 fromOff = Vector3.ProjectOnPlane(from, axis);
            Vector3 toOff = Vector3.ProjectOnPlane(to, axis);
            if (fromOff.sqrMagnitude <= MinimumLengthSquared || toOff.sqrMagnitude <= MinimumLengthSquared) return 0f;

            return Vector3.SignedAngle(fromOff, toOff, axis);
        }

        /// <summary>
        /// Forearm roll of a chain shoulder → elbow → wrist, in degrees: <see cref="RollAbout"/> the forearm
        /// (elbow → wrist) from <paramref name="hingeSign"/>·(upper × forearm) to <paramref name="palmNormal"/>.
        /// Zero when the chain is straight.
        /// </summary>
        public static float ForearmRollDegrees(Vector3 shoulder, Vector3 elbow, Vector3 wrist, Vector3 palmNormal, float hingeSign) {
            if (!TryBendNormal(shoulder, elbow, wrist, out Vector3 hinge)) return 0f;

            return RollAbout(wrist - elbow, hinge * hingeSign, palmNormal);
        }

        /// <summary>
        /// Distance along <paramref name="direction"/> (pitched <paramref name="pitchDegrees"/> off level)
        /// a carried pair must move so its <paramref name="bottom"/> sits <paramref name="clearance"/>
        /// ahead of <paramref name="chestFront"/> along <paramref name="forward"/>. Negative when there
        /// is room to spare.
        /// </summary>
        public static float ClearanceShift(Vector3 chestFront, Vector3 bottom, Vector3 forward, float clearance, float pitchDegrees) {
            float cosine = Mathf.Max(Mathf.Cos(pitchDegrees * Mathf.Deg2Rad), MinimumCosine);
            return (clearance - Vector3.Dot(bottom - chestFront, forward)) / cosine;
        }

        /// <summary>
        /// Range of shifts <c>s</c> along unit <paramref name="direction"/> that keep
        /// <c>target + direction·s</c> within <paramref name="reach"/> of <paramref name="shoulder"/>.
        /// False when no shift does.
        /// </summary>
        public static bool ReachInterval(Vector3 target, Vector3 shoulder, Vector3 direction, float reach, out float min, out float max) {
            Vector3 offset = target - shoulder;
            float along = Vector3.Dot(offset, direction);
            float discriminant = along * along - (offset.sqrMagnitude - reach * reach);
            if (discriminant < 0f) {
                min = 0f;
                max = 0f;
                return false;
            }

            float root = Mathf.Sqrt(discriminant);
            min = -along - root;
            max = -along + root;
            return true;
        }

        /// <summary>
        /// Smallest <c>t ≥ 0</c> that brings <c>target + direction·t</c> (unit <paramref name="direction"/>)
        /// within <paramref name="reach"/> of <paramref name="shoulder"/>: 0 when already in reach, the
        /// closest approach when the ray never gets there.
        /// </summary>
        public static float ReachEntry(Vector3 target, Vector3 shoulder, Vector3 direction, float reach) {
            float closest = Mathf.Max(0f, Vector3.Dot(shoulder - target, direction));
            if (!ReachInterval(target, shoulder, direction, reach, out float min, out float max)) return closest;
            if (max < 0f) return closest;

            return Mathf.Max(0f, min);
        }

        /// <summary>
        /// Shift both hand targets take together, toward the shoulders' midpoint from the targets' centre,
        /// so each comes within its arm's reach: the larger of the two arms' <see cref="ReachEntry"/>.
        /// Zero when both already reach. One shared move keeps the spacing, so a prop held between the
        /// hands stays rigid; when the arms disagree past each other's reach the farther arm wins.
        /// </summary>
        public static Vector3 PairShift(Vector3 leftTarget, Vector3 leftShoulder, float leftReach, Vector3 rightTarget, Vector3 rightShoulder, float rightReach) {
            Vector3 direction = (leftShoulder + rightShoulder) - (leftTarget + rightTarget);
            return PairShiftAlong(direction, leftTarget, leftShoulder, leftReach, rightTarget, rightShoulder, rightReach);
        }

        /// <summary>
        /// <see cref="PairShift"/> along a given <paramref name="direction"/> (any length), e.g. the way the
        /// shoulders moved since the hands were captured. Zero when the direction is degenerate.
        /// </summary>
        public static Vector3 PairShiftAlong(Vector3 direction, Vector3 leftTarget, Vector3 leftShoulder, float leftReach, Vector3 rightTarget, Vector3 rightShoulder, float rightReach) {
            if (direction.sqrMagnitude <= MinimumLengthSquared) return Vector3.zero;

            direction.Normalize();
            float left = ReachEntry(leftTarget, leftShoulder, direction, leftReach);
            float right = ReachEntry(rightTarget, rightShoulder, direction, rightReach);
            return direction * Mathf.Max(left, right);
        }

        /// <summary>
        /// Least move that brings <paramref name="target"/> within <paramref name="reach"/> of
        /// <paramref name="shoulder"/>: straight toward it by the deficit; zero when already in reach.
        /// </summary>
        public static Vector3 IntoReach(Vector3 target, Vector3 shoulder, float reach) {
            Vector3 toShoulder = shoulder - target;
            float distance = toShoulder.magnitude;
            if (distance <= reach || distance <= 0f) return Vector3.zero;

            return toShoulder * (1f - reach / distance);
        }

        /// <summary>
        /// Least shift <c>s</c> both hand targets take together so each comes within its arm's reach
        /// (|target + s − shoulder| ≤ reach): the point of the two reach balls' overlap nearest zero, so it
        /// never exceeds the larger single-arm deficit by more than the overlap forces and moves no more
        /// than the shoulders do. When the balls miss each other, the point between them that leaves both
        /// arms equally short. Zero when both already reach.
        /// </summary>
        public static Vector3 MinimalPairShift(Vector3 leftTarget, Vector3 leftShoulder, float leftReach, Vector3 rightTarget, Vector3 rightShoulder, float rightReach) {
            Vector3 leftCentre = leftShoulder - leftTarget;
            Vector3 rightCentre = rightShoulder - rightTarget;
            Vector3 left = IntoReach(leftTarget, leftShoulder, leftReach);
            if ((left - rightCentre).magnitude <= rightReach + PairTolerance) return left;

            Vector3 right = IntoReach(rightTarget, rightShoulder, rightReach);
            if ((right - leftCentre).magnitude <= leftReach + PairTolerance) return right;

            return NearestOnReachCircle(leftCentre, leftReach, rightCentre, rightReach);
        }

        /// <summary>
        /// Point nearest zero on the circle where two spheres meet; the point between them leaving both
        /// equally short when they miss each other.
        /// </summary>
        private static Vector3 NearestOnReachCircle(Vector3 leftCentre, float leftReach, Vector3 rightCentre, float rightReach) {
            Vector3 apart = rightCentre - leftCentre;
            float distance = apart.magnitude;
            if (distance <= 1e-6f) return leftCentre;

            Vector3 axis = apart / distance;
            if (distance >= leftReach + rightReach) return leftCentre + axis * (leftReach + (distance - leftReach - rightReach) * 0.5f);

            float along = (leftReach * leftReach - rightReach * rightReach + distance * distance) / (2f * distance);
            Vector3 centre = leftCentre + axis * along;
            float radius = Mathf.Sqrt(Mathf.Max(0f, leftReach * leftReach - along * along));
            Vector3 towardZero = Vector3.ProjectOnPlane(-centre, axis);
            if (towardZero.sqrMagnitude <= MinimumLengthSquared) towardZero = AnyPerpendicular(axis);
            return centre + towardZero.normalized * radius;
        }

        /// <summary>
        /// Next applied shift easing toward <paramref name="solved"/>: <paramref name="solved"/> at once when
        /// it is a need (<see cref="IsShiftNeed"/>), <paramref name="maxStep"/> is not positive, or the
        /// interval is empty; else a release of at most <paramref name="maxStep"/>, clamped into
        /// [<paramref name="lowest"/>, <paramref name="highest"/>] (the shifts both arms reach with the chest
        /// clear), so easing only slows a release the arms can afford and never leaves a hand short.
        /// </summary>
        public static float EaseShiftRelease(float applied, float solved, float maxStep, float lowest, float highest) {
            if (maxStep <= 0f || lowest > highest || IsShiftNeed(applied, solved)) return solved;

            return Mathf.Clamp(Mathf.MoveTowards(applied, solved, maxStep), lowest, highest);
        }

        /// <summary>
        /// True when <paramref name="solved"/> lies at least as far from zero as <paramref name="applied"/>
        /// on the same side, or across zero: more pull-in or push-out is needed, never eased.
        /// </summary>
        public static bool IsShiftNeed(float applied, float solved) {
            if (applied * solved < 0f) return true;

            return Mathf.Abs(solved) >= Mathf.Abs(applied);
        }

        /// <summary>
        /// Degrees a clavicle must turn about <paramref name="clavicle"/> so the arm pivot at
        /// <paramref name="armPivot"/> comes within <paramref name="reach"/> of <paramref name="target"/>;
        /// zero when it already is, the full angle to the target line when no turn is enough.
        /// </summary>
        public static float ShrugDegrees(Vector3 clavicle, Vector3 armPivot, Vector3 target, float reach) {
            Vector3 toArm = armPivot - clavicle;
            Vector3 toTarget = target - clavicle;
            if ((target - armPivot).sqrMagnitude <= reach * reach) return 0f;

            float armLength = toArm.magnitude;
            float targetLength = toTarget.magnitude;
            float apart = Vector3.Angle(toArm, toTarget);
            if (armLength * targetLength < 1e-6f) return 0f;

            float cosine = (armLength * armLength + targetLength * targetLength - reach * reach) / (2f * armLength * targetLength);
            if (cosine >= 1f) return apart;

            float allowed = Mathf.Acos(Mathf.Max(cosine, -1f)) * Mathf.Rad2Deg;
            return Mathf.Max(0f, apart - allowed);
        }

        /// <summary>
        /// Value nearest <paramref name="wanted"/> on the segment from <paramref name="feasible"/> that
        /// <paramref name="isFeasible"/> accepts, assuming it accepts <paramref name="feasible"/> and the
        /// accepted values form one run from there. Returns <paramref name="wanted"/> when that passes.
        /// </summary>
        public static float BisectToward(float feasible, float wanted, int steps, Func<float, bool> isFeasible) {
            if (isFeasible(wanted)) return wanted;

            for (int step = 0; step < steps; step++) {
                float middle = (feasible + wanted) * 0.5f;
                if (isFeasible(middle)) {
                    feasible = middle;
                } else {
                    wanted = middle;
                }
            }

            return feasible;
        }

        /// <summary>
        /// <paramref name="capturedPole"/> (off the reach line) turned toward unit <paramref name="least"/> by
        /// <paramref name="share"/>; <paramref name="least"/> when the captured pole runs along the reach.
        /// </summary>
        private static Vector3 BlendPole(Vector3 direction, Vector3 capturedPole, Vector3 least, float share) {
            Vector3 captured = capturedPole - direction * Vector3.Dot(capturedPole, direction);
            if (captured.sqrMagnitude <= MinimumLengthSquared || share >= 1f) return least;

            Vector3 blended = Vector3.Lerp(captured.normalized, least, share);
            return blended.sqrMagnitude > MinimumLengthSquared ? blended.normalized : least;
        }

        /// <summary>Refine cost of the seed pole swung <paramref name="degrees"/> about the reach line.</summary>
        private static float SwivelCost(Vector3 shoulder, Vector3 target, float upperLength, float lowerLength, Vector3 palmNormal, Vector3 handAxis, Vector3 seedPole, Vector3 axis, float hingeSign, float degrees) {
            Vector3 pole = Quaternion.AngleAxis(degrees, axis) * seedPole;
            Vector3 elbow = SolveElbow(shoulder, target, upperLength, lowerLength, pole);
            Vector3 forearm = target - elbow;
            Vector3 hinge = BendNormal(shoulder, elbow, target, Vector3.Cross(pole, axis).normalized);
            float roll = RollAbout(forearm, hinge * hingeSign, palmNormal) / RollReference;
            float bend = Vector3.Angle(handAxis, forearm) / BendReference;
            return roll * roll + bend * bend;
        }

        private static Vector3 AnyPerpendicular(Vector3 vector) {
            Vector3 perpendicular = Vector3.Cross(vector, Vector3.up);
            if (perpendicular.sqrMagnitude < MinimumLengthSquared) perpendicular = Vector3.Cross(vector, Vector3.right);
            return perpendicular.sqrMagnitude > MinimumLengthSquared ? perpendicular.normalized : Vector3.forward;
        }
    }
}
