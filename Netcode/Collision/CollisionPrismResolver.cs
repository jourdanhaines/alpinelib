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
    /// are visited in index order and ties keep the lower index, so both ends pick the same face.
    /// </remarks>
    internal static class CollisionPrismResolver {
        /// <summary>Squared separation too short to take a direction from; the interior rule takes over.</summary>
        private const float DegenerateSquaredEpsilon = 1e-12f;

        /// <summary>Capsule against prism; the normal points out of the prism, as every contact's does.</summary>
        public static bool TryGetPrismContact(in CapsulePose pose, in CollisionShape shape, out CollisionContact contact) {
            contact = default;
            float radius = pose.Radius;
            float planX = pose.FootPosition.X - shape.Center.X;
            float planZ = pose.FootPosition.Z - shape.Center.Z;
            float reach = shape.Radius + radius;
            if (planX * planX + planZ * planZ > reach * reach) {
                return false;
            }

            float halfHeight = shape.HalfExtents.Y;
            float low = pose.SegmentBottom.Y - shape.Center.Y;
            float high = pose.SegmentTop.Y - shape.Center.Y;
            float gapY = VerticalGap(low, high, halfHeight);
            bool inside = PlanQuery(in shape, planX, planZ, out float squaredDistance, out float closestX, out float closestZ, out int innerEdge, out float innerDepth);
            if (!inside) {
                float separationSquared = squaredDistance + gapY * gapY;
                if (separationSquared >= radius * radius) {
                    return false;
                }

                if (separationSquared > DegenerateSquaredEpsilon) {
                    float distance = MathF.Sqrt(separationSquared);
                    var normal = new Vector3((planX - closestX) / distance, gapY / distance, (planZ - closestZ) / distance);
                    contact = new CollisionContact(normal, radius - distance, CollisionResolver.UnassignedShapeIndex);
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
            float planX = x - shape.Center.X;
            float planZ = z - shape.Center.Z;
            int count = shape.Plan.Count;
            for (int index = 0; index < count; index++) {
                shape.Plan.GetVertex(index, out float startX, out float startZ);
                shape.Plan.GetVertex(NextIndex(index, count), out float endX, out float endZ);
                float cross = (endX - startX) * (planZ - startZ) - (endZ - startZ) * (planX - startX);
                if (cross < 0f) {
                    return false;
                }
            }

            height = shape.Center.Y + shape.HalfExtents.Y;
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
                float edgeX = endX - startX;
                float edgeZ = endZ - startZ;
                float offsetX = planX - startX;
                float offsetZ = planZ - startZ;
                float lengthSquared = edgeX * edgeX + edgeZ * edgeZ;
                float signedDistance = (offsetX * edgeZ - offsetZ * edgeX) / MathF.Sqrt(lengthSquared);
                if (signedDistance > largestSigned) {
                    largestSigned = signedDistance;
                    innerEdge = index;
                }

                float along = MathF.Min(MathF.Max((offsetX * edgeX + offsetZ * edgeZ) / lengthSquared, 0f), 1f);
                float pointX = startX + edgeX * along;
                float pointZ = startZ + edgeZ * along;
                float gapX = planX - pointX;
                float gapZ = planZ - pointZ;
                float distanceSquared = gapX * gapX + gapZ * gapZ;
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
                return high + halfHeight;
            }

            if (low > halfHeight) {
                return low - halfHeight;
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
            contact = new CollisionContact(normal, radius - gap, CollisionResolver.UnassignedShapeIndex);
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
            float upDepth = (halfHeight - low) + radius;
            float sideDepth = innerDepth + radius;
            float downDepth = (high + halfHeight) + radius;
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
        private static Vector3 EdgeNormal(in CollisionShape shape, int edge) {
            int count = shape.Plan.Count;
            shape.Plan.GetVertex(edge, out float startX, out float startZ);
            shape.Plan.GetVertex(NextIndex(edge, count), out float endX, out float endZ);
            float edgeX = endX - startX;
            float edgeZ = endZ - startZ;
            float length = MathF.Sqrt(edgeX * edgeX + edgeZ * edgeZ);
            return new Vector3(edgeZ / length, 0f, -edgeX / length);
        }

        private static int NextIndex(int index, int count) {
            return index + 1 < count ? index + 1 : 0;
        }
    }
}
