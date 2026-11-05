using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Procedural.Meshing;

namespace AlpineLib.Server.Tests.Procedural.Meshing {
    /// <summary>Test looks for the envelope fitters: turned cuboids and lofted noisy prisms.</summary>
    internal static class EnvelopeShapes {
        /// <summary>A closed cuboid turned by <paramref name="yawDegrees"/>.</summary>
        public static List<Vector3> Cuboid(Vector3 centre, Vector3 half, int yawDegrees) {
            double radians = yawDegrees * Math.PI / 180.0;
            var right = new Vector3((float)Math.Cos(radians), 0f, (float)-Math.Sin(radians));
            var forward = new Vector3((float)Math.Sin(radians), 0f, (float)Math.Cos(radians));
            var ring = new List<Vector3>();
            foreach ((int x, int z) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) }) ring.Add(right * (half.X * x) + forward * (half.Z * z));
            return Prism(ring, centre.Y - half.Y, centre.Y + half.Y, new Vector3(centre.X, 0f, centre.Z));
        }

        /// <summary>A wall lofted straight up from a plan ring between two heights, capped top and bottom.</summary>
        public static List<Vector3> Prism(List<Vector3> ring, float bottom, float top, Vector3 offset) {
            var corners = new List<Vector3>();
            for (int index = 0; index < ring.Count; index++) {
                Vector3 a = ring[index] + offset, b = ring[(index + 1) % ring.Count] + offset;
                AddQuad(corners, At(a, bottom), At(b, bottom), At(b, top), At(a, top));
            }
            for (int index = 1; index + 1 < ring.Count; index++) {
                AddTriangle(corners, At(ring[0] + offset, top), At(ring[index + 1] + offset, top), At(ring[index] + offset, top));
                AddTriangle(corners, At(ring[0] + offset, bottom), At(ring[index] + offset, bottom), At(ring[index + 1] + offset, bottom));
            }
            return corners;
        }

        /// <summary>A noisy star-shaped plan ring of <paramref name="count"/> points around radius <paramref name="radius"/>.</summary>
        public static List<Vector3> NoisyRing(int count, float radius, float noise, int seed) {
            var random = new Random(seed);
            var ring = new List<Vector3>();
            for (int index = 0; index < count; index++) {
                double angle = 2.0 * Math.PI * index / count;
                float reach = radius * (1f + noise * (float)(random.NextDouble() * 2.0 - 1.0));
                ring.Add(new Vector3((float)Math.Cos(angle) * reach, 0f, (float)Math.Sin(angle) * reach * 0.7f));
            }
            return ring;
        }

        private static Vector3 At(Vector3 plan, float height) => new Vector3(plan.X, height, plan.Z);

        private static void AddQuad(List<Vector3> corners, Vector3 a, Vector3 b, Vector3 c, Vector3 d) {
            AddTriangle(corners, a, b, c);
            AddTriangle(corners, a, c, d);
        }

        private static void AddTriangle(List<Vector3> corners, Vector3 a, Vector3 b, Vector3 c) {
            corners.Add(a);
            corners.Add(b);
            corners.Add(c);
        }
    }
}
