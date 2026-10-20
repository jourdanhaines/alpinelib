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

        private const float MinimumLengthSquared = 1e-12f;
        private const float MinimumCosine = 0.05f;

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

        private static Vector3 AnyPerpendicular(Vector3 vector) {
            Vector3 perpendicular = Vector3.Cross(vector, Vector3.up);
            if (perpendicular.sqrMagnitude < MinimumLengthSquared) perpendicular = Vector3.Cross(vector, Vector3.right);
            return perpendicular.sqrMagnitude > MinimumLengthSquared ? perpendicular.normalized : Vector3.forward;
        }
    }
}
