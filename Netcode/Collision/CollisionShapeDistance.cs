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

        private static double SegmentDistance(in CollisionShape shape, Vector3 point) {
            double along = Vector3.Dot(point - shape.Center, shape.AxisUp);
            along = Math.Max(-shape.HalfLength, Math.Min(shape.HalfLength, along));
            Vector3 closest = shape.Center + shape.AxisUp * (float)along;
            return Vector3.Distance(point, closest);
        }
    }
}
