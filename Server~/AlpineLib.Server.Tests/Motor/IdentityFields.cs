using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Collision;

namespace AlpineLib.Server.Tests.Motor {
    /// <summary>
    /// Seeded scenes with no acute concave corner in them: scattered convex shapes far enough apart that a capsule
    /// never touches two at once, and closed rooms whose walls meet at right or obtuse angles. Nothing overhangs the
    /// floor where it meets it: spheres and lying capsules are sunk past their equator, standing capsules sit on their
    /// cylinder, and tilted boxes are beams held above the floor.
    /// </summary>
    public static class IdentityFields {
        private const float FieldHalfSize = 14f;
        private const float Separation = 1.3f;
        private const int ShapeAttempts = 400;

        /// <summary>A floor and up to <paramref name="shapeCount"/> separated boxes, prisms, ramps, spheres and capsules.</summary>
        public static CollisionShape[] Scatter(int seed, int shapeCount) {
            var random = new Random(seed);
            var shapes = new List<CollisionShape> { CollisionShape.MakePlane(0f) };
            for (int attempt = 0; attempt < ShapeAttempts && shapes.Count <= shapeCount; attempt++) {
                CollisionShape candidate = RandomShape(random);
                if (IsClear(shapes, in candidate)) shapes.Add(candidate);
            }

            return shapes.ToArray();
        }

        /// <summary>
        /// A floor and a closed room of <paramref name="sides"/> tall walls, each wall a box overlapping its neighbours
        /// at the corners, turned <paramref name="yawDegrees"/>; four sides meet at right angles, more at obtuse ones.
        /// </summary>
        public static CollisionShape[] Room(int sides, float apothem, double yawDegrees) {
            var shapes = new List<CollisionShape> { CollisionShape.MakePlane(0f) };
            double halfAngle = Math.PI / sides;
            float halfLength = (float)(apothem * Math.Tan(halfAngle)) + 1.2f;
            for (int side = 0; side < sides; side++) {
                double angle = yawDegrees * Math.PI / 180.0 + side * 2.0 * halfAngle;
                var inward = new Vector3((float)-Math.Cos(angle), 0f, (float)-Math.Sin(angle));
                var along = new Vector3(-inward.Z, 0f, inward.X);
                Vector3 center = -inward * (apothem + 0.5f) + new Vector3(0f, 1.5f, 0f);
                shapes.Add(CollisionShape.MakeBox(center, along, Vector3.UnitY, Vector3.Cross(along, Vector3.UnitY), new Vector3(halfLength, 1.5f, 0.5f)));
            }

            shapes.Add(CollisionShape.MakeBox(new Vector3(1.5f, 0.1f, -1f), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.6f, 0.1f, 0.6f)));
            return shapes.ToArray();
        }

        private static CollisionShape RandomShape(Random random) {
            var center = new Vector3(Range(random, -FieldHalfSize, FieldHalfSize), 0f, Range(random, -FieldHalfSize, FieldHalfSize));
            switch (random.Next(6)) {
                case 0: return RandomBox(random, center, false);
                case 1: return RandomBox(random, center, true);
                case 2: return RandomPrism(random, center);
                case 3: return RandomRamp(random, center);
                case 4: return RandomSphere(random, center);
                default: return RandomCapsule(random, center);
            }
        }

        private static CollisionShape RandomSphere(Random random, Vector3 center) {
            float radius = Range(random, 0.3f, 1.4f);
            return CollisionShape.MakeSphere(center - new Vector3(0f, radius * Range(random, 0.1f, 0.9f), 0f), radius);
        }

        // Low boxes are step-ups and tall ones walls; tilted ones lean up to 25° as beams 1.1 m to 2 m up.
        private static CollisionShape RandomBox(Random random, Vector3 center, bool tilted) {
            double yaw = Range(random, 0f, 6.2831855f);
            double tilt = tilted ? Range(random, -0.44f, 0.44f) : 0.0;
            var right = new Vector3((float)Math.Cos(yaw), 0f, (float)Math.Sin(yaw));
            var across = new Vector3(-right.Z, 0f, right.X);
            Vector3 up = Vector3.Normalize(Vector3.UnitY * (float)Math.Cos(tilt) + across * (float)Math.Sin(tilt));
            Vector3 forward = Vector3.Normalize(Vector3.Cross(right, up));
            float halfHeight = random.Next(3) == 0 ? Range(random, 0.05f, 0.15f) : Range(random, 0.3f, 1.5f);
            var extents = new Vector3(Range(random, 0.3f, 2f), halfHeight, Range(random, 0.3f, 2f));
            float lift = tilted ? halfHeight + 1f + Range(random, 0.1f, 1f) + extents.Z * (float)Math.Abs(Math.Sin(tilt)) : halfHeight;
            return CollisionShape.MakeBox(center + new Vector3(0f, lift, 0f), right, up, forward, extents);
        }

        private static CollisionShape RandomPrism(Random random, Vector3 center) {
            int count = random.Next(3, 9);
            float radius = Range(random, 0.5f, 2f);
            var offsets = new Vector2[count];
            double start = Range(random, 0f, 6.2831855f);
            for (int index = 0; index < count; index++) {
                double angle = start + index * 2.0 * Math.PI / count + Range(random, -0.2f, 0.2f);
                offsets[index] = new Vector2((float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)));
            }

            float halfHeight = random.Next(3) == 0 ? Range(random, 0.05f, 0.14f) : Range(random, 0.3f, 1.5f);
            return CollisionShape.MakePrism(center + new Vector3(0f, halfHeight, 0f), halfHeight, offsets);
        }

        private static CollisionShape RandomRamp(Random random, Vector3 center) {
            float half = Range(random, 1f, 2.5f);
            var offsets = new[] { new Vector2(-half, -half), new Vector2(half, -half), new Vector2(half, half), new Vector2(-half, half) };
            float slopeX = Range(random, -0.8f, 0.8f);
            float slopeZ = Range(random, -0.3f, 0.3f);
            float top = (MathF.Abs(slopeX) + MathF.Abs(slopeZ)) * half + 0.05f;
            return CollisionShape.MakeSlopedPrism(center, -0.5f, top, slopeX, slopeZ, offsets);
        }

        private static CollisionShape RandomCapsule(Random random, Vector3 center) {
            bool lying = random.Next(2) == 0;
            float radius = Range(random, 0.2f, 0.7f);
            if (!lying) return CollisionShape.MakeCapsule(center + new Vector3(0f, 1f, 0f), Vector3.UnitY, 1f, radius);

            double yaw = Range(random, 0f, 6.2831855f);
            var axis = new Vector3((float)Math.Cos(yaw), 0f, (float)Math.Sin(yaw));
            return CollisionShape.MakeCapsule(center - new Vector3(0f, radius * Range(random, 0.1f, 0.9f), 0f), axis, Range(random, 0.5f, 2f), radius);
        }

        private static bool IsClear(List<CollisionShape> shapes, in CollisionShape candidate) {
            candidate.GetBoundsXZ(out float minX, out float maxX, out float minZ, out float maxZ);
            foreach (CollisionShape shape in shapes) {
                if (shape.Type == CollisionShapeType.Plane) continue;
                shape.GetBoundsXZ(out float otherMinX, out float otherMaxX, out float otherMinZ, out float otherMaxZ);
                bool apart = minX - Separation > otherMaxX || otherMinX - Separation > maxX || minZ - Separation > otherMaxZ || otherMinZ - Separation > maxZ;
                if (!apart) return false;
            }

            return true;
        }

        private static float Range(Random random, float minimum, float maximum) {
            return minimum + (float)random.NextDouble() * (maximum - minimum);
        }
    }
}
