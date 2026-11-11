using System;
using System.Numerics;

namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// The shortest displacement that satisfies every row of a set of half-space constraints
    /// <c>row · x ≥ bound</c> at once: the joint push-out <see cref="PawnMotor"/> falls back to when resolving
    /// one contact at a time ping-pongs between the faces of an acute corner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum-norm point of a feasible set of half-spaces in three dimensions has at most three active rows,
    /// and on them it is the minimum-norm solution of the equalities. So the answer is the shortest feasible
    /// candidate among the projections onto every single row, every pair and every triple. Any feasible candidate
    /// is a valid push, and the optimum is one of them, so no multiplier test is needed. Rows confined to the
    /// horizontal plane never form a usable triple and are answered by singles and pairs alone.
    /// </para>
    /// <para>
    /// Same determinism rules as the motor: single-precision floats, every operation narrowed in a fixed order,
    /// <c>+ - * /</c> only, candidates visited in a fixed order with ties keeping the earlier one.
    /// </para>
    /// </remarks>
    internal static class PawnContactSolver {
        /// <summary>
        /// How far a candidate may undershoot a row and still satisfy it, per unit of <c>1 + |x|²</c>: rounding in a
        /// near-parallel pair grows with the size of the answer, and pushes and velocities run from millimetres to
        /// metres per second.
        /// </summary>
        public const float FeasibleSlack = 1e-5f;

        /// <summary>Rows whose Gram determinant falls below this share of its scale are treated as parallel.</summary>
        private const float DegenerateShare = 1e-6f;

        /// <summary>
        /// Opposing rows (more than 90° apart) whose Gram determinant falls below this share — normals within about 2°
        /// of straight opposite — are a gap, not a corner: their joint push runs along the gap as depth over the
        /// sine of the angle, so the pair is not offered at all.
        /// </summary>
        private const float OpposingShare = 1e-3f;

        /// <summary>
        /// Solves for the shortest <paramref name="solution"/> with <c>rows[i] · solution ≥ bounds[i]</c> for every
        /// row, or reports that no single row, pair or triple yields one.
        /// </summary>
        public static bool TrySolve(ReadOnlySpan<Vector3> rows, ReadOnlySpan<float> bounds, out Vector3 solution) {
            solution = Vector3.Zero;
            if (IsSatisfied(rows, bounds, in solution, FeasibleSlack)) {
                return true;
            }

            float shortest = float.MaxValue;
            bool found = false;
            TrySingles(rows, bounds, ref solution, ref shortest, ref found);
            TryPairs(rows, bounds, ref solution, ref shortest, ref found);
            TryTriples(rows, bounds, ref solution, ref shortest, ref found);
            return found;
        }

        // x · y with each product and sum narrowed, in x, y, z order.
        public static float Dot(in Vector3 left, in Vector3 right) {
            float xy = (float)((float)(left.X * right.X) + (float)(left.Y * right.Y));
            return (float)(xy + (float)(left.Z * right.Z));
        }

        private static void TrySingles(ReadOnlySpan<Vector3> rows, ReadOnlySpan<float> bounds, ref Vector3 best, ref float shortest, ref bool found) {
            for (int first = 0; first < rows.Length; first++) {
                float rowSquared = Dot(rows[first], rows[first]);
                if (bounds[first] <= 0f || rowSquared <= 0f) {
                    continue;
                }

                float scale = (float)(bounds[first] / rowSquared);
                Consider(rows, bounds, Scale(rows[first], scale), ref best, ref shortest, ref found);
            }
        }

        private static void TryPairs(ReadOnlySpan<Vector3> rows, ReadOnlySpan<float> bounds, ref Vector3 best, ref float shortest, ref bool found) {
            for (int first = 0; first < rows.Length; first++) {
                TryPairsWith(rows, bounds, first, ref best, ref shortest, ref found);
            }
        }

        private static void TryPairsWith(ReadOnlySpan<Vector3> rows, ReadOnlySpan<float> bounds, int first, ref Vector3 best, ref float shortest, ref bool found) {
            for (int second = first + 1; second < rows.Length; second++) {
                if (TrySolvePair(rows[first], bounds[first], rows[second], bounds[second], out Vector3 candidate)) {
                    Consider(rows, bounds, candidate, ref best, ref shortest, ref found);
                }
            }
        }

        private static void TryTriples(ReadOnlySpan<Vector3> rows, ReadOnlySpan<float> bounds, ref Vector3 best, ref float shortest, ref bool found) {
            for (int first = 0; first < rows.Length; first++) {
                for (int second = first + 1; second < rows.Length; second++) {
                    TryTriplesWith(rows, bounds, first, second, ref best, ref shortest, ref found);
                }
            }
        }

        private static void TryTriplesWith(ReadOnlySpan<Vector3> rows, ReadOnlySpan<float> bounds, int first, int second, ref Vector3 best, ref float shortest, ref bool found) {
            for (int third = second + 1; third < rows.Length; third++) {
                if (TrySolveTriple(rows[first], bounds[first], rows[second], bounds[second], rows[third], bounds[third], out Vector3 candidate)) {
                    Consider(rows, bounds, candidate, ref best, ref shortest, ref found);
                }
            }
        }

        /// <summary>
        /// The minimum-norm x with <c>a·x = boundA</c> and <c>b·x = boundB</c>: x = α a + β b from the 2×2 Gram
        /// system, or nothing when the rows are parallel.
        /// </summary>
        private static bool TrySolvePair(in Vector3 rowA, float boundA, in Vector3 rowB, float boundB, out Vector3 candidate) {
            candidate = Vector3.Zero;
            float aa = Dot(rowA, rowA);
            float bb = Dot(rowB, rowB);
            float ab = Dot(rowA, rowB);
            float scale = (float)(aa * bb);
            float determinant = (float)((float)(aa * bb) - (float)(ab * ab));
            float share = ab < 0f ? OpposingShare : DegenerateShare;
            if (!(determinant > (float)(scale * share))) {
                return false;
            }

            float alpha = (float)((float)((float)(boundA * bb) - (float)(boundB * ab)) / determinant);
            float beta = (float)((float)((float)(boundB * aa) - (float)(boundA * ab)) / determinant);
            candidate = Add(Scale(rowA, alpha), Scale(rowB, beta));
            return true;
        }

        /// <summary>
        /// The x with all three equalities, by Cramer's rule over cross products, or nothing when the rows are
        /// close to coplanar (always the case for rows confined to the horizontal plane).
        /// </summary>
        private static bool TrySolveTriple(in Vector3 rowA, float boundA, in Vector3 rowB, float boundB, in Vector3 rowC, float boundC, out Vector3 candidate) {
            candidate = Vector3.Zero;
            Vector3 crossBC = Cross(rowB, rowC);
            float determinant = Dot(rowA, crossBC);
            float scale = (float)((float)(Dot(rowA, rowA) * Dot(rowB, rowB)) * Dot(rowC, rowC));
            if (!((float)(determinant * determinant) > (float)(scale * DegenerateShare))) {
                return false;
            }

            Vector3 crossCA = Cross(rowC, rowA);
            Vector3 crossAB = Cross(rowA, rowB);
            Vector3 sum = Add(Add(Scale(crossBC, boundA), Scale(crossCA, boundB)), Scale(crossAB, boundC));
            candidate = Scale(sum, (float)(1f / determinant));
            return true;
        }

        private static void Consider(ReadOnlySpan<Vector3> rows, ReadOnlySpan<float> bounds, in Vector3 candidate, ref Vector3 best, ref float shortest, ref bool found) {
            float lengthSquared = Dot(candidate, candidate);
            float slack = (float)(FeasibleSlack * (float)(1f + lengthSquared));
            if (!(lengthSquared < shortest) || !IsSatisfied(rows, bounds, in candidate, slack)) {
                return;
            }

            best = candidate;
            shortest = lengthSquared;
            found = true;
        }

        private static bool IsSatisfied(ReadOnlySpan<Vector3> rows, ReadOnlySpan<float> bounds, in Vector3 candidate, float slack) {
            for (int index = 0; index < rows.Length; index++) {
                if ((float)(Dot(rows[index], candidate) + slack) < bounds[index]) {
                    return false;
                }
            }

            return true;
        }

        private static Vector3 Scale(in Vector3 value, float factor) {
            return new Vector3((float)(value.X * factor), (float)(value.Y * factor), (float)(value.Z * factor));
        }

        private static Vector3 Add(in Vector3 left, in Vector3 right) {
            return new Vector3((float)(left.X + right.X), (float)(left.Y + right.Y), (float)(left.Z + right.Z));
        }

        private static Vector3 Cross(in Vector3 left, in Vector3 right) {
            return new Vector3(
                (float)((float)(left.Y * right.Z) - (float)(left.Z * right.Y)),
                (float)((float)(left.Z * right.X) - (float)(left.X * right.Z)),
                (float)((float)(left.X * right.Y) - (float)(left.Y * right.X)));
        }
    }
}
