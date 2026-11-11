using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Collision;

namespace AlpineLib.Server.Tests.Motor {
    /// <summary>
    /// Seeded scenes full of acute corners: thirty overlapping yawed boxes, spheres, sloped prisms and prisms in a
    /// 12 m square, and a sloped talus running down into a cliff (a V ditch) with a boulder sunk in it.
    /// </summary>
    public static class ClutterFields {
        /// <summary>A floor and thirty overlapping shapes from <paramref name="seed"/>.</summary>
        public static CollisionShape[] Dense(int seed) {
            var random = new Random(seed);
            var shapes = new List<CollisionShape> { CollisionShape.MakePlane(0f) };
            for (int count = 0; count < 30; count++) {
                var center = new Vector3((float)(random.NextDouble() * 12 - 6), 0f, (float)(random.NextDouble() * 12 - 6));
                shapes.Add(RandomShape(random, center));
            }

            return shapes.ToArray();
        }

        /// <summary>
        /// A floor, a talus 7 m long falling at <paramref name="slope"/> per metre toward +X, and a 12 m cliff whose face
        /// stands at x = 6 where the talus bottoms out, and a 0.9 m boulder in the ditch at z = 2.5.
        /// </summary>
        public static CollisionShape[] Ditch(float slope) {
            float top = 7f * slope + 0.1f;
            CollisionShape talus = CollisionShape.MakeSlopedPrism(Vector3.Zero, -0.5f, top, -slope, 0f, new[] { new Vector2(0f, -6f), new Vector2(7f, -6f), new Vector2(7f, 6f), new Vector2(0f, 6f) });
            CollisionShape cliff = CollisionShape.MakeBox(new Vector3(8.5f, 6f, 0f), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(2.5f, 6f, 8f));
            CollisionShape boulder = CollisionShape.MakeSphere(new Vector3(5f, 0.5f, 2.5f), 0.9f);
            return new[] { CollisionShape.MakePlane(0f), talus, cliff, boulder };
        }

        /// <summary>The ditch talus's height at <paramref name="x"/>.</summary>
        public static float DitchHeight(float slope, float x) {
            return 7f * slope + 0.1f - slope * x;
        }

        private static CollisionShape RandomShape(Random random, Vector3 center) {
            switch (random.Next(4)) {
                case 0: return YawBox(center + new Vector3(0f, 0.75f, 0f), (float)(random.NextDouble() * 360), new Vector3(0.3f + (float)random.NextDouble(), 0.1f + (float)random.NextDouble() * 1.4f, 0.3f + (float)random.NextDouble()));
                case 1: return CollisionShape.MakeSphere(center + new Vector3(0f, (float)random.NextDouble(), 0f), 0.3f + (float)random.NextDouble());
                case 2: return CollisionShape.MakeSlopedPrism(center, -0.5f, 1.3f, (float)(random.NextDouble() * 1.1 - 0.55), (float)(random.NextDouble() * 1.1 - 0.55), new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) });
                default: return RandomPrism(random, center);
            }
        }

        private static CollisionShape RandomPrism(Random random, Vector3 center) {
            int corners = random.Next(3, 7);
            var offsets = new Vector2[corners];
            double start = random.NextDouble() * 6;
            float radius = 0.4f + (float)random.NextDouble();
            for (int corner = 0; corner < corners; corner++) {
                double angle = start + corner * 2 * Math.PI / corners;
                offsets[corner] = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
            }

            float halfHeight = 0.1f + (float)random.NextDouble() * 1.2f;
            return CollisionShape.MakePrism(center + new Vector3(0f, halfHeight, 0f), halfHeight, offsets);
        }

        private static CollisionShape YawBox(Vector3 center, float yawDegrees, Vector3 extents) {
            float angle = yawDegrees * MathF.PI / 180f;
            var right = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
            return CollisionShape.MakeBox(center, right, Vector3.UnitY, Vector3.Cross(right, Vector3.UnitY), extents);
        }
    }
}
