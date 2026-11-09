using System;
using System.Numerics;

namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// The sloped prism half of the narrow phase: an upright pawn capsule against a convex prism with a flat bottom and
    /// a planar top, and a vertical probe against that top. Called only through <see cref="CollisionResolver"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The capsule is upright, so the distance from its segment to the solid is the least, over plan points q, of the
    /// plan distance to q combined with the gap between the segment's height range and the column [bottom, t(q)]. Above
    /// the bottom that is the distance from the segment's low point to the solid's upper surface: the plane projection
    /// when it lands inside the plan, else the least over the rim edges of a convex two-piece quadratic, found exactly
    /// from three candidate parameters per edge. No iteration, no trigonometry.
    /// </para>
    /// <para>
    /// The same determinism rules as <see cref="CollisionPrismResolver"/>: floats only, every operation narrowed, edges
    /// in index order, ties keep the earlier candidate. The plane normal costs one square root per query.
    /// </para>
    /// </remarks>
    internal static class CollisionSlopedPrismResolver {
        /// <summary>Squared separation too short to take a direction from; the interior rule takes over.</summary>
        private const float DegenerateSquaredEpsilon = 1e-12f;

        /// <summary>Capsule against sloped prism; the normal points out of the solid.</summary>
        public static bool TryGetSlopedPrismContact(in CapsulePose pose, in CollisionShape shape, out CollisionContact contact) {
            contact = default;
            float radius = pose.Radius;
            float planX = (float)(pose.FootPosition.X - shape.Center.X);
            float planZ = (float)(pose.FootPosition.Z - shape.Center.Z);
            float reach = (float)(shape.Radius + radius);
            if (SumOfSquares(planX, planZ) > (float)(reach * reach)) {
                return false;
            }

            float low = pose.SegmentBottom.Y;
            float high = pose.SegmentTop.Y;
            bool inside = CollisionPrismResolver.PlanQuery(in shape, planX, planZ, out float squaredDistance, out float closestX, out float closestZ, out int innerEdge, out float innerDepth);
            if (high < shape.BottomY) {
                return TryResolveBelow(planX, planZ, (float)(high - shape.BottomY), inside, squaredDistance, closestX, closestZ, radius, out contact);
            }

            float top = shape.SlopedTopAtPlan(planX, planZ);
            float rise = (float)(low - top);
            if (rise > 0f && ProjectsOntoTopFace(in shape, planX, planZ, rise)) {
                return TryResolveTopFace(in shape, rise, radius, out contact);
            }

            if (inside && !(rise > 0f)) {
                ResolveInterior(in shape, low, high, rise, innerEdge, innerDepth, radius, out contact);
                return true;
            }

            float nearest = NearestOnRim(in shape, planX, planZ, low, out float awayX, out float awayY, out float awayZ);
            if (nearest >= (float)(radius * radius)) {
                return false;
            }

            if (nearest > DegenerateSquaredEpsilon) {
                float distance = (float)MathF.Sqrt(nearest);
                var normal = new Vector3((float)(awayX / distance), (float)(awayY / distance), (float)(awayZ / distance));
                contact = new CollisionContact(normal, (float)(radius - distance), CollisionResolver.UnassignedShapeIndex);
                return true;
            }

            ResolveInterior(in shape, low, high, rise, innerEdge, innerDepth, radius, out contact);
            return true;
        }

        /// <summary>The top height and plane normal when the column passes through the plan, rim included.</summary>
        public static bool TryGetSlopedPrismSurfaceHeight(in CollisionShape shape, float x, float z, out float height, out Vector3 normal) {
            height = 0f;
            normal = new Vector3(0f, 1f, 0f);
            float planX = (float)(x - shape.Center.X);
            float planZ = (float)(z - shape.Center.Z);
            if (!CollisionPrismResolver.PlanContains(in shape, planX, planZ)) {
                return false;
            }

            height = shape.SlopedTopAtPlan(planX, planZ);
            normal = TopNormal(in shape);
            return true;
        }

        /// <summary>
        /// The low point is <paramref name="rise"/> above the plane over its own column; true when its projection onto
        /// the plane, (p + rise / L² · (sx, sz)), lands in the plan, which makes that projection the solid's nearest point.
        /// </summary>
        private static bool ProjectsOntoTopFace(in CollisionShape shape, float planX, float planZ, float rise) {
            float slopeX = shape.TopSlopeX;
            float slopeZ = shape.TopSlopeZ;
            float lengthSquared = (float)((float)(1f + (float)(slopeX * slopeX)) + (float)(slopeZ * slopeZ));
            float pull = (float)(rise / lengthSquared);
            float footX = (float)(planX + (float)(pull * slopeX));
            float footZ = (float)(planZ + (float)(pull * slopeZ));
            return CollisionPrismResolver.PlanContains(in shape, footX, footZ);
        }

        /// <summary>Over the top face: the plane distance rise / L against the radius, pushed out along the plane normal.</summary>
        private static bool TryResolveTopFace(in CollisionShape shape, float rise, float radius, out CollisionContact contact) {
            contact = default;
            Vector3 normal = TopNormal(in shape);
            float distance = (float)(rise * normal.Y);
            if (distance >= radius) {
                return false;
            }

            contact = new CollisionContact(normal, (float)(radius - distance), CollisionResolver.UnassignedShapeIndex);
            return true;
        }

        /// <summary>
        /// Least squared distance from the segment (low point at <paramref name="low"/>) to the solid's rim: per edge,
        /// plan distance² plus the squared height of the low point above the top there (0 when beside the column).
        /// The separation is returned unnormalised.
        /// </summary>
        private static float NearestOnRim(in CollisionShape shape, float planX, float planZ, float low, out float awayX, out float awayY, out float awayZ) {
            int count = shape.Plan.Count;
            float best = float.MaxValue;
            awayX = 0f;
            awayY = 0f;
            awayZ = 0f;
            for (int index = 0; index < count; index++) {
                shape.Plan.GetVertex(index, out float startX, out float startZ);
                shape.Plan.GetVertex(index + 1 < count ? index + 1 : 0, out float endX, out float endZ);
                float edgeX = (float)(endX - startX);
                float edgeZ = (float)(endZ - startZ);
                float lengthSquared = SumOfSquares(edgeX, edgeZ);
                float projection = (float)((float)((float)(planX - startX) * edgeX) + (float)((float)(planZ - startZ) * edgeZ));
                float climb = (float)((float)(shape.TopSlopeX * edgeX) + (float)(shape.TopSlopeZ * edgeZ));
                float startGap = (float)(low - shape.SlopedTopAtPlan(startX, startZ));

                // Stationary point of the piece above the top, of the piece beside it, and the break between them.
                float above = (float)((float)(projection + (float)(climb * startGap)) / (float)(lengthSquared + (float)(climb * climb)));
                float beside = (float)(projection / lengthSquared);
                float split = climb != 0f ? (float)(startGap / climb) : 0f;
                TryEdgePoint(in shape, planX, planZ, low, startX, startZ, edgeX, edgeZ, above, ref best, ref awayX, ref awayY, ref awayZ);
                TryEdgePoint(in shape, planX, planZ, low, startX, startZ, edgeX, edgeZ, beside, ref best, ref awayX, ref awayY, ref awayZ);
                TryEdgePoint(in shape, planX, planZ, low, startX, startZ, edgeX, edgeZ, split, ref best, ref awayX, ref awayY, ref awayZ);
            }

            return best;
        }

        // Evaluates the edge point at the clamped parameter and keeps it when strictly nearer.
        private static void TryEdgePoint(
            in CollisionShape shape,
            float planX,
            float planZ,
            float low,
            float startX,
            float startZ,
            float edgeX,
            float edgeZ,
            float along,
            ref float best,
            ref float awayX,
            ref float awayY,
            ref float awayZ) {
            float clamped = MathF.Min(MathF.Max(along, 0f), 1f);
            float pointX = (float)(startX + (float)(edgeX * clamped));
            float pointZ = (float)(startZ + (float)(edgeZ * clamped));
            float gapX = (float)(planX - pointX);
            float gapZ = (float)(planZ - pointZ);
            float gapY = MathF.Max((float)(low - shape.SlopedTopAtPlan(pointX, pointZ)), 0f);
            float squared = (float)(SumOfSquares(gapX, gapZ) + (float)(gapY * gapY));
            if (!(squared < best)) {
                return;
            }

            best = squared;
            awayX = gapX;
            awayY = gapY;
            awayZ = gapZ;
        }

        /// <summary>Wholly under the flat bottom: as a prism, a push down off the base or out and down past its rim.</summary>
        private static bool TryResolveBelow(
            float planX,
            float planZ,
            float gapY,
            bool inside,
            float squaredDistance,
            float closestX,
            float closestZ,
            float radius,
            out CollisionContact contact) {
            contact = default;
            float gap = MathF.Abs(gapY);
            if (inside) {
                if (gap >= radius) {
                    return false;
                }

                contact = new CollisionContact(new Vector3(0f, -1f, 0f), (float)(radius - gap), CollisionResolver.UnassignedShapeIndex);
                return true;
            }

            float separationSquared = (float)(squaredDistance + (float)(gapY * gapY));
            if (separationSquared >= (float)(radius * radius)) {
                return false;
            }

            float distance = (float)MathF.Sqrt(separationSquared);
            float awayX = (float)(planX - closestX);
            float awayZ = (float)(planZ - closestZ);
            var normal = new Vector3((float)(awayX / distance), (float)(gapY / distance), (float)(awayZ / distance));
            contact = new CollisionContact(normal, (float)(radius - distance), CollisionResolver.UnassignedShapeIndex);
            return true;
        }

        /// <summary>
        /// The segment's plan point is inside and its range overlaps the column: push out along the plane normal, out
        /// of the nearest side or down off the base, whichever is shortest; ties go up, then side, then down.
        /// </summary>
        private static void ResolveInterior(
            in CollisionShape shape,
            float low,
            float high,
            float rise,
            int innerEdge,
            float innerDepth,
            float radius,
            out CollisionContact contact) {
            Vector3 normal = TopNormal(in shape);
            float upDepth = (float)((float)(-rise * normal.Y) + radius);
            float sideDepth = (float)(innerDepth + radius);
            float downDepth = (float)((float)(high - shape.BottomY) + radius);
            float shallowest = MathF.Min(upDepth, MathF.Min(sideDepth, downDepth));
            if (upDepth <= shallowest) {
                contact = new CollisionContact(normal, upDepth, CollisionResolver.UnassignedShapeIndex);
                return;
            }

            if (sideDepth <= shallowest) {
                contact = new CollisionContact(CollisionPrismResolver.EdgeNormal(in shape, innerEdge), sideDepth, CollisionResolver.UnassignedShapeIndex);
                return;
            }

            contact = new CollisionContact(new Vector3(0f, -1f, 0f), downDepth, CollisionResolver.UnassignedShapeIndex);
        }

        /// <summary>The top's outward unit normal (−sx, 1, −sz) / √(1 + sx² + sz²).</summary>
        private static Vector3 TopNormal(in CollisionShape shape) {
            float slopeX = shape.TopSlopeX;
            float slopeZ = shape.TopSlopeZ;
            float lengthSquared = (float)((float)(1f + (float)(slopeX * slopeX)) + (float)(slopeZ * slopeZ));
            float length = (float)MathF.Sqrt(lengthSquared);
            return new Vector3((float)(-slopeX / length), (float)(1f / length), (float)(-slopeZ / length));
        }

        // x² + z², each product narrowed to float before the sum.
        private static float SumOfSquares(float x, float z) {
            float xx = (float)(x * x);
            float zz = (float)(z * z);
            return (float)(xx + zz);
        }
    }
}
