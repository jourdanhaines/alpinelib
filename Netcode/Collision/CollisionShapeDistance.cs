using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// How far a point lies outside collision shapes: 0 on or inside, else the distance to the solid. For gates
    /// that check a look stays inside its colliders; off the simulation path, so double maths is fine.
    /// </summary>
    public static class CollisionShapeDistance {
        /// <summary>The distance from <paramref name="point"/> to the solid of <paramref name="shape"/>; 0 inside.</summary>
        public static double Outside(in CollisionShape shape, Vector3 point) {
            switch (shape.Type) {
                case CollisionShapeType.Plane:
                    return Math.Max(0.0, (double)point.Y - shape.Center.Y);
                case CollisionShapeType.Sphere:
                    return Math.Max(0.0, Vector3.Distance(point, shape.Center) - (double)shape.Radius);
                case CollisionShapeType.Capsule:
                    return Math.Max(0.0, SegmentDistance(shape, point) - shape.Radius);
                case CollisionShapeType.Box:
                    return BoxOutside(shape, point);
                case CollisionShapeType.Prism:
                    return PrismOutside(shape, point);
                case CollisionShapeType.SlopedPrism:
                    return SlopedPrismOutside(shape, point);
                default:
                    throw new ArgumentOutOfRangeException(nameof(shape), shape.Type, "Unknown shape type.");
            }
        }

        /// <summary>The least <see cref="Outside(in CollisionShape, Vector3)"/> over <paramref name="count"/> shapes from <paramref name="start"/>; infinity for none.</summary>
        public static double Outside(IReadOnlyList<CollisionShape> shapes, int start, int count, Vector3 point) {
            if (shapes == null) throw new ArgumentNullException(nameof(shapes));

            double best = double.PositiveInfinity;
            for (int index = start; index < start + count; index++) {
                best = Math.Min(best, Outside(shapes[index], point));
                if (best <= 0.0) return 0.0;
            }
            return best;
        }

        private static double BoxOutside(in CollisionShape shape, Vector3 point) {
            Vector3 offset = point - shape.Center;
            double x = Math.Max(0.0, Math.Abs((double)Vector3.Dot(offset, shape.AxisRight)) - shape.HalfExtents.X);
            double y = Math.Max(0.0, Math.Abs((double)Vector3.Dot(offset, shape.AxisUp)) - shape.HalfExtents.Y);
            double z = Math.Max(0.0, Math.Abs((double)Vector3.Dot(offset, shape.AxisForward)) - shape.HalfExtents.Z);
            return Math.Sqrt(x * x + y * y + z * z);
        }

        // Plan distance to the polygon (0 inside) and height-range excess, combined.
        private static double PrismOutside(in CollisionShape shape, Vector3 point) {
            double planDistance = PlanDistance(shape.Plan, (double)point.X - shape.Center.X, (double)point.Z - shape.Center.Z);
            double vertical = Math.Max(0.0, Math.Abs((double)point.Y - shape.Center.Y) - shape.HalfExtents.Y);
            return Math.Sqrt(planDistance * planDistance + vertical * vertical);
        }

        /// <summary>
        /// The distance from <paramref name="point"/> to a sloped prism's solid, 0 inside: under the bottom, plan distance
        /// and drop combined; above it, the distance to the solid's upper surface (the plane projection when it lands in
        /// the plan, else the nearest rim point, found exactly per edge).
        /// </summary>
        public static double SlopedPrismOutside(in CollisionShape shape, Vector3 point) {
            double x = (double)point.X - shape.Center.X;
            double z = (double)point.Z - shape.Center.Z;
            double y = point.Y;
            if (y < shape.BottomY) {
                double planDistance = PlanDistance(shape.Plan, x, z);
                double drop = shape.BottomY - y;
                return Math.Sqrt(planDistance * planDistance + drop * drop);
            }

            double rise = y - SlopedTop(shape, x, z);
            bool inside = PlanDistance(shape.Plan, x, z) <= 0.0;
            if (inside && rise <= 0.0) return 0.0;

            if (rise > 0.0) {
                double lengthSquared = 1.0 + (double)shape.TopSlopeX * shape.TopSlopeX + (double)shape.TopSlopeZ * shape.TopSlopeZ;
                double pull = rise / lengthSquared;
                if (PlanDistance(shape.Plan, x + pull * shape.TopSlopeX, z + pull * shape.TopSlopeZ) <= 0.0) return rise / Math.Sqrt(lengthSquared);
            }

            double best = double.PositiveInfinity;
            for (int index = 0; index < shape.Plan.Count; index++) best = Math.Min(best, RimEdgeSquared(shape, index, x, y, z));
            return Math.Sqrt(best);
        }

        private static double SlopedTop(in CollisionShape shape, double planX, double planZ) {
            return shape.TopY0 + shape.TopSlopeX * planX + shape.TopSlopeZ * planZ;
        }

        // Least of plan distance² plus height² above the top along one rim edge: a convex two-piece quadratic whose
        // minimum is at one of the two stationary points or the break between them.
        private static double RimEdgeSquared(in CollisionShape shape, int index, double x, double y, double z) {
            shape.Plan.GetVertex(index, out float startX, out float startZ);
            shape.Plan.GetVertex((index + 1) % shape.Plan.Count, out float endX, out float endZ);
            double edgeX = (double)endX - startX, edgeZ = (double)endZ - startZ;
            double lengthSquared = edgeX * edgeX + edgeZ * edgeZ;
            double projection = (x - startX) * edgeX + (z - startZ) * edgeZ;
            double climb = shape.TopSlopeX * edgeX + shape.TopSlopeZ * edgeZ;
            double startGap = y - SlopedTop(shape, startX, startZ);
            double best = double.PositiveInfinity;
            best = Math.Min(best, RimPointSquared(shape, startX, startZ, edgeX, edgeZ, (projection + climb * startGap) / (lengthSquared + climb * climb), x, y, z));
            best = Math.Min(best, RimPointSquared(shape, startX, startZ, edgeX, edgeZ, projection / lengthSquared, x, y, z));
            if (climb != 0.0) best = Math.Min(best, RimPointSquared(shape, startX, startZ, edgeX, edgeZ, startGap / climb, x, y, z));
            return best;
        }

        private static double RimPointSquared(in CollisionShape shape, double startX, double startZ, double edgeX, double edgeZ, double along, double x, double y, double z) {
            double clamped = Math.Max(0.0, Math.Min(1.0, along));
            double pointX = startX + edgeX * clamped, pointZ = startZ + edgeZ * clamped;
            double gapY = Math.Max(0.0, y - SlopedTop(shape, pointX, pointZ));
            return (x - pointX) * (x - pointX) + (z - pointZ) * (z - pointZ) + gapY * gapY;
        }

        // Distance from (x, z) to a convex CCW plan, 0 on or inside it.
        private static double PlanDistance(in PrismPlan plan, double x, double z) {
            bool inside = true;
            double best = double.PositiveInfinity;
            for (int index = 0; index < plan.Count; index++) {
                plan.GetVertex(index, out float startX, out float startZ);
                plan.GetVertex((index + 1) % plan.Count, out float endX, out float endZ);
                double edgeX = (double)endX - startX;
                double edgeZ = (double)endZ - startZ;
                double offsetX = x - startX;
                double offsetZ = z - startZ;
                if (offsetX * edgeZ - offsetZ * edgeX > 0.0) {
                    inside = false;
                }

                double lengthSquared = edgeX * edgeX + edgeZ * edgeZ;
                double along = Math.Max(0.0, Math.Min(1.0, (offsetX * edgeX + offsetZ * edgeZ) / lengthSquared));
                double gapX = offsetX - edgeX * along;
                double gapZ = offsetZ - edgeZ * along;
                best = Math.Min(best, gapX * gapX + gapZ * gapZ);
            }
            return inside ? 0.0 : Math.Sqrt(best);
        }

        private static double SegmentDistance(in CollisionShape shape, Vector3 point) {
            double along = Vector3.Dot(point - shape.Center, shape.AxisUp);
            along = Math.Max(-shape.HalfLength, Math.Min(shape.HalfLength, along));
            Vector3 closest = shape.Center + shape.AxisUp * (float)along;
            return Vector3.Distance(point, closest);
        }
    }
}
