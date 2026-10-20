using System;
using UnityEngine;

namespace AlpineLib.Animation.Procedural {
    /// <summary>
    /// The arithmetic behind <see cref="ArmPitchPoseModifier"/>: how far the upper arms must swing so a
    /// point they carry stays on the view axis as the look pitches.
    /// </summary>
    /// <remarks>
    /// Pure and scene-free, so a gate can evaluate the table. Everything is in the actor's local space
    /// in the resting pose; pitch and arm angles are degrees about the actor's right axis, positive
    /// down (the convention of <see cref="LookPoseSolver"/>). Only Y and Z matter.
    /// </remarks>
    public static class ArmPitchSolver {
        /// <summary>Pitch of the first table entry.</summary>
        public const float TableMinPitch = -85f;

        /// <summary>Degrees between table entries.</summary>
        public const float TableStep = 5f;

        /// <summary>Entries covering [−85, 85].</summary>
        public const int TableLength = 35;

        private const float ScanMin = -170f;
        private const float ScanMax = 170f;
        private const float ScanStep = 5f;
        private const int BisectIterations = 32;

        /// <summary>Pitch of table entry <paramref name="index"/>.</summary>
        public static float TablePitch(int index) {
            return TableMinPitch + index * TableStep;
        }

        /// <summary>
        /// Where <paramref name="carried"/> ends up when the arm swings by <paramref name="armDegrees"/>
        /// about <paramref name="shoulder"/> and the torso joints then bend for <paramref name="pitch"/>.
        /// </summary>
        public static Vector3 CarriedPoint(LookPoseJoint[] torso, float lookWeight, Vector3 shoulder, Vector3 carried, float pitch, float armDegrees) {
            Vector3 point = shoulder + Quaternion.Euler(armDegrees, 0f, 0f) * (carried - shoulder);
            return point + LookPoseSolver.ResolveTipOffset(torso, point, pitch, lookWeight);
        }

        /// <summary>Degrees <paramref name="point"/> sits below the horizontal through <paramref name="eye"/>.</summary>
        public static float Depression(Vector3 eye, Vector3 point) {
            return Mathf.Atan2(eye.y - point.y, point.z - eye.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Arm swing that puts the carried point <c>pitch − viewOffsetDegrees</c> below the horizontal
        /// through <paramref name="eye"/> (negative offsets hold it below the view axis). Falls back to
        /// the look the torso leaves over, <c>pitch × (1 − fallbackTorsoShare)</c>, when no swing reaches.
        /// </summary>
        public static float SolveArmAngle(LookPoseJoint[] torso, float lookWeight, Vector3 shoulder, Vector3 carried, Vector3 eye,
            float pitch, float viewOffsetDegrees, float fallbackTorsoShare) {
            float target = pitch - viewOffsetDegrees;
            float lower = ScanMin;
            float lowerError = DepressionError(torso, lookWeight, shoulder, carried, eye, pitch, lower, target);

            // Depression rises with the swing until the point passes under or behind the eye; the first
            // rising crossing without a wrap-around jump is the one in front of the holder.
            for (float upper = ScanMin + ScanStep; upper <= ScanMax; upper += ScanStep) {
                float upperError = DepressionError(torso, lookWeight, shoulder, carried, eye, pitch, upper, target);
                if (lowerError <= 0f && upperError > 0f && upperError - lowerError < 90f) {
                    return Bisect(torso, lookWeight, shoulder, carried, eye, pitch, target, lower, upper);
                }

                lower = upper;
                lowerError = upperError;
            }

            return pitch * (1f - fallbackTorsoShare);
        }

        /// <summary>
        /// Fills <paramref name="table"/> (length <see cref="TableLength"/>) with the arm swing for each
        /// table pitch; <paramref name="eyeAtPitch"/> gives the eye for a pitch, so a body that swings
        /// its eye out with the look is accounted for.
        /// </summary>
        public static void BuildTable(float[] table, LookPoseJoint[] torso, float lookWeight, Vector3 shoulder, Vector3 carried,
            Func<float, Vector3> eyeAtPitch, float viewOffsetDegrees, float torsoShareDown, float torsoShareUp) {
            for (int index = 0; index < table.Length; index++) {
                float pitch = TablePitch(index);
                float share = pitch >= 0f ? torsoShareDown : torsoShareUp;
                table[index] = SolveArmAngle(torso, lookWeight, shoulder, carried, eyeAtPitch(pitch), pitch, viewOffsetDegrees, share);
            }
        }

        /// <summary>Table value at <paramref name="pitch"/>, linearly interpolated and clamped to the table's range.</summary>
        public static float Sample(float[] table, float pitch) {
            if (table == null || table.Length == 0) return 0f;

            float position = Mathf.Clamp((pitch - TableMinPitch) / TableStep, 0f, table.Length - 1);
            int lowerIndex = Mathf.Min((int)position, table.Length - 1);
            int upperIndex = Mathf.Min(lowerIndex + 1, table.Length - 1);
            return Mathf.Lerp(table[lowerIndex], table[upperIndex], position - lowerIndex);
        }

        private static float DepressionError(LookPoseJoint[] torso, float lookWeight, Vector3 shoulder, Vector3 carried, Vector3 eye,
            float pitch, float armDegrees, float target) {
            Vector3 point = CarriedPoint(torso, lookWeight, shoulder, carried, pitch, armDegrees);
            return Mathf.DeltaAngle(target, Depression(eye, point));
        }

        private static float Bisect(LookPoseJoint[] torso, float lookWeight, Vector3 shoulder, Vector3 carried, Vector3 eye,
            float pitch, float target, float lower, float upper) {
            for (int iteration = 0; iteration < BisectIterations; iteration++) {
                float middle = (lower + upper) * 0.5f;
                if (DepressionError(torso, lookWeight, shoulder, carried, eye, pitch, middle, target) > 0f) {
                    upper = middle;
                } else {
                    lower = middle;
                }
            }

            return (lower + upper) * 0.5f;
        }
    }
}
