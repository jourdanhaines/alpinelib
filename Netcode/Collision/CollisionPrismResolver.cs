using System;
using System.Numerics;

namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// The prism half of the narrow phase: an upright pawn capsule against a vertical convex prism, and a
    /// vertical probe against its cap. Called only through <see cref="CollisionResolver"/>.
    /// </summary>
    /// <remarks>
    /// The capsule is always upright, so its segment is one plan point with a height range and the test
    /// splits exactly into a plan query and a vertical clamp. The resolver's determinism rules hold here:
    /// floats only, fixed order, componentwise arithmetic, <c>+ - * /</c> plus Sqrt, Min, Max and Abs. Edges
    /// are visited in index order and ties keep the lower index, so both ends pick the same face. Every
    /// arithmetic result is narrowed with an explicit <c>(float)</c> before it feeds the next operation:
    /// Mono without its float32 optimisation (the Unity editor) evaluates float expressions in double and would
    /// otherwise round compound expressions once instead of per operation, unlike CoreCLR.
    /// </remarks>
    internal static class CollisionPrismResolver {
        /// <summary>Squared separation too short to take a direction from; the interior rule takes over.</summary>
        private const float DegenerateSquaredEpsilon = 1e-12f;

        /// <summary>Capsule against prism; the normal points out of the prism, as every contact's does.</summary>
        public static bool TryGetPrismContact(in CapsulePose pose, in CollisionShape shape, out CollisionContact contact) {
            contact = default;
            float radius = pose.Radius;
            float planX = (float)(pose.FootPosition.X - shape.Center.X);
            float planZ = (float)(pose.FootPosition.Z - shape.Center.Z);
            float reach = (float)(shape.Radius + radius);
            float planSquared = SumOfSquares(planX, planZ);
            if (planSquared > (float)(reach * reach)) {
                return false;
            }

            float halfHeight = shape.HalfExtents.Y;
            float bottom = pose.SegmentBottom.Y;
            float top = pose.SegmentTop.Y;
            float low = (float)(bottom - shape.Center.Y);
            float high = (float)(top - shape.Center.Y);
            float gapY = VerticalGap(low, high, halfHeight);
            bool inside = PlanQuery(in shape, planX, planZ, out float squaredDistance, out float closestX, out float closestZ, out int innerEdge, out float innerDepth);
            if (!inside) {
                float separationSquared = (float)(squaredDistance + (float)(gapY * gapY));
                if (separationSquared >= (float)(radius * radius)) {
                    return false;
                }

                if (separationSquared > DegenerateSquaredEpsilon) {
                    float distance = (float)MathF.Sqrt(separationSquared);
                    float awayX = (float)(planX - closestX);
                    float awayZ = (float)(planZ - closestZ);
                    var normal = new Vector3((float)(awayX / distance), (float)(gapY / distance), (float)(awayZ / distance));
                    contact = new CollisionContact(normal, (float)(radius - distance), CollisionResolver.UnassignedShapeIndex);
                    return true;
                }
            }

            if (gapY != 0f) {
                return TryResolveCapGap(gapY, radius, out contact);
            }

            ResolveInterior(in shape, low, high, halfHeight, innerEdge, innerDepth, radius, out contact);
            return true;
        }

        /// <summary>The cap height when the column passes through the plan, edges included; the cap is flat.</summary>
        /// <remarks>
        /// "Edges included" is exact arithmetic only: a float point on a slanted edge fails the test about a fifth of
        /// the time, so two prisms sharing an edge can both miss a column on the seam. Boxes behave the same.
        /// </remarks>
        public static bool TryGetPrismSurfaceHeight(in CollisionShape shape, float x, float z, out float height, out Vector3 normal) {
            height = 0f;
            normal = new Vector3(0f, 1f, 0f);
            float planX = (float)(x - shape.Center.X);
            float planZ = (float)(z - shape.Center.Z);
            if (!PlanContains(in shape, planX, planZ)) {
                return false;
            }

            height = (float)(shape.Center.Y + shape.HalfExtents.Y);
            return true;
        }

        /// <summary>True when the plan point (relative to the centre) is inside the plan or on its rim, by exact sign.</summary>
        public static bool PlanContains(in CollisionShape shape, float planX, float planZ) {
            int count = shape.Plan.Count;
            for (int index = 0; index < count; index++) {
                shape.Plan.GetVertex(index, out float startX, out float startZ);
                shape.Plan.GetVertex(NextIndex(index, count), out float endX, out float endZ);
                float cross = Cross((float)(endX - startX), (float)(endZ - startZ), (float)(planX - startX), (float)(planZ - startZ));
                if (cross < 0f) {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The plan point (<paramref name="planX"/>, <paramref name="planZ"/>), relative to the centre, against
        /// the plan polygon: squared distance to and closest point on its boundary, and the edge whose line
        /// it is least inside (or furthest outside) with that signed depth. Returns true inside or on it.
        /// </summary>
        /// <remarks>
        /// Signed edge distance is <c>s = ((px − x)·dz − (pz − z)·dx) / L</c>, positive outside. The largest
        /// <c>s</c> wins, the lower index on a tie; the closest point is replaced only by a strictly nearer one.
        /// </remarks>
        public static bool PlanQuery(
            in CollisionShape shape,
            float planX,
            float planZ,
            out float squaredDistance,
            out float closestX,
            out float closestZ,
            out int innerEdge,
            out float innerDepth) {
            int count = shape.Plan.Count;
            squaredDistance = float.MaxValue;
            closestX = 0f;
            closestZ = 0f;
            innerEdge = 0;
            float largestSigned = float.MinValue;
            for (int index = 0; index < count; index++) {
                shape.Plan.GetVertex(index, out float startX, out float startZ);
                shape.Plan.GetVertex(NextIndex(index, count), out float endX, out float endZ);
                float edgeX = (float)(endX - startX);
                float edgeZ = (float)(endZ - startZ);
                float offsetX = (float)(planX - startX);
                float offsetZ = (float)(planZ - startZ);
                float lengthSquared = SumOfSquares(edgeX, edgeZ);
                float length = (float)MathF.Sqrt(lengthSquared);
                float signedDistance = (float)(Cross(offsetX, offsetZ, edgeX, edgeZ) / length);
                if (signedDistance > largestSigned) {
                    largestSigned = signedDistance;
                    innerEdge = index;
                }

                float projection = (float)((float)(offsetX * edgeX) + (float)(offsetZ * edgeZ));
                float along = MathF.Min(MathF.Max((float)(projection / lengthSquared), 0f), 1f);
                float pointX = (float)(startX + (float)(edgeX * along));
                float pointZ = (float)(startZ + (float)(edgeZ * along));
                float gapX = (float)(planX - pointX);
                float gapZ = (float)(planZ - pointZ);
                float distanceSquared = SumOfSquares(gapX, gapZ);
                if (distanceSquared < squaredDistance) {
                    squaredDistance = distanceSquared;
                    closestX = pointX;
                    closestZ = pointZ;
                }
            }

            innerDepth = -largestSigned;
            return largestSigned <= 0f;
        }

        /// <summary>How far the segment's height range [low, high] misses [−h, h]: negative below, positive above.</summary>
        private static float VerticalGap(float low, float high, float halfHeight) {
            if (high < -halfHeight) {
                return (float)(high + halfHeight);
            }

            if (low > halfHeight) {
                return (float)(low - halfHeight);
            }

            return 0f;
        }

        /// <summary>Over or under the plan with air between: a straight vertical push off the cap or base.</summary>
        private static bool TryResolveCapGap(float gapY, float radius, out CollisionContact contact) {
            contact = default;
            float gap = MathF.Abs(gapY);
            if (gap >= radius) {
                return false;
            }

            var normal = new Vector3(0f, gapY < 0f ? -1f : 1f, 0f);
            contact = new CollisionContact(normal, (float)(radius - gap), CollisionResolver.UnassignedShapeIndex);
            return true;
        }

        /// <summary>
        /// The segment's plan point is inside and its range overlaps the prism: push up off the cap, out of
        /// the nearest side or down off the base, whichever is shortest; ties go up, then side, then down.
        /// </summary>
        private static void ResolveInterior(
            in CollisionShape shape,
            float low,
            float high,
            float halfHeight,
            int innerEdge,
            float innerDepth,
            float radius,
            out CollisionContact contact) {
            float upDepth = (float)((float)(halfHeight - low) + radius);
            float sideDepth = (float)(innerDepth + radius);
            float downDepth = (float)((float)(high + halfHeight) + radius);
            float shallowest = MathF.Min(upDepth, MathF.Min(sideDepth, downDepth));
            if (upDepth <= shallowest) {
                contact = new CollisionContact(new Vector3(0f, 1f, 0f), upDepth, CollisionResolver.UnassignedShapeIndex);
                return;
            }

            if (sideDepth <= shallowest) {
                contact = new CollisionContact(EdgeNormal(in shape, innerEdge), sideDepth, CollisionResolver.UnassignedShapeIndex);
                return;
            }

            contact = new CollisionContact(new Vector3(0f, -1f, 0f), downDepth, CollisionResolver.UnassignedShapeIndex);
        }

        /// <summary>The outward unit normal (dz, 0, −dx) / L of plan edge <paramref name="edge"/>.</summary>
        public static Vector3 EdgeNormal(in CollisionShape shape, int edge) {
            int count = shape.Plan.Count;
            shape.Plan.GetVertex(edge, out float startX, out float startZ);
            shape.Plan.GetVertex(NextIndex(edge, count), out float endX, out float endZ);
            float edgeX = (float)(endX - startX);
            float edgeZ = (float)(endZ - startZ);
            float length = (float)MathF.Sqrt(SumOfSquares(edgeX, edgeZ));
            return new Vector3((float)(edgeZ / length), 0f, (float)(-edgeX / length));
        }

        // x² + z², each product narrowed to float before the sum, so no runtime keeps a wider intermediate.
        private static float SumOfSquares(float x, float z) {
            float xx = (float)(x * x);
            float zz = (float)(z * z);
            return (float)(xx + zz);
        }

        // ax·bz − az·bx with the same narrowing.
        private static float Cross(float ax, float az, float bx, float bz) {
            float first = (float)(ax * bz);
            float second = (float)(az * bx);
            return (float)(first - second);
        }

        private static int NextIndex(int index, int count) {
            return index + 1 < count ? index + 1 : 0;
        }
    }
}
