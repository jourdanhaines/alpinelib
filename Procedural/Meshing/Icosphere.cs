using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// Unit icospheres at subdivision levels 0–2 (20, 80 and 320 faces), shared read-only by every blob.
    /// </summary>
    /// <remarks>
    /// Built once in the static constructor (thread-safe by the runtime) in double precision with
    /// literal constants only, so every runtime gets the same directions. Every face winds outward.
    /// </remarks>
    public static class Icosphere {
        /// <summary>The highest supported subdivision level.</summary>
        public const int MaxSubdivisions = 2;

        private const double Phi = 1.6180339887498949;

        private static readonly double[][] Directions;
        private static readonly int[][] Triangles;

        static Icosphere() {
            Directions = new double[MaxSubdivisions + 1][];
            Triangles = new int[MaxSubdivisions + 1][];
            List<double> directions = BaseDirections();
            List<int> triangles = BaseTriangles(directions);
            for (int level = 0; level <= MaxSubdivisions; level++) {
                Directions[level] = directions.ToArray();
                Triangles[level] = triangles.ToArray();
                if (level < MaxSubdivisions) {
                    triangles = Subdivide(directions, triangles);
                }
            }
        }

        /// <summary>Number of shared vertices at <paramref name="subdivisions"/>.</summary>
        public static int VertexCount(int subdivisions) {
            return GetDirections(subdivisions).Length / 3;
        }

        /// <summary>Number of faces at <paramref name="subdivisions"/>: 20 × 4ⁿ.</summary>
        public static int FaceCount(int subdivisions) {
            return GetTriangles(subdivisions).Length / 3;
        }

        /// <summary>Unit directions as x, y, z triples. Read-only: do not modify.</summary>
        public static double[] GetDirections(int subdivisions) {
            return Directions[CheckLevel(subdivisions)];
        }

        /// <summary>Vertex indices, three per outward-wound face. Read-only: do not modify.</summary>
        public static int[] GetTriangles(int subdivisions) {
            return Triangles[CheckLevel(subdivisions)];
        }

        private static int CheckLevel(int subdivisions) {
            if (subdivisions < 0 || subdivisions > MaxSubdivisions) {
                throw new ArgumentOutOfRangeException(nameof(subdivisions), subdivisions, $"Must be in [0, {MaxSubdivisions}].");
            }
            return subdivisions;
        }

        private static List<double> BaseDirections() {
            double[] raw = {
                -1, Phi, 0, 1, Phi, 0, -1, -Phi, 0, 1, -Phi, 0,
                0, -1, Phi, 0, 1, Phi, 0, -1, -Phi, 0, 1, -Phi,
                Phi, 0, -1, Phi, 0, 1, -Phi, 0, -1, -Phi, 0, 1,
            };
            List<double> directions = new List<double>(raw.Length);
            for (int vertex = 0; vertex < raw.Length; vertex += 3) {
                AddNormalised(directions, raw[vertex], raw[vertex + 1], raw[vertex + 2]);
            }
            return directions;
        }

        private static List<int> BaseTriangles(List<double> directions) {
            int[] raw = {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            List<int> triangles = new List<int>(raw.Length);
            for (int face = 0; face < raw.Length; face += 3) {
                AddOutward(directions, triangles, raw[face], raw[face + 1], raw[face + 2]);
            }
            return triangles;
        }

        private static List<int> Subdivide(List<double> directions, List<int> triangles) {
            Dictionary<long, int> midpoints = new Dictionary<long, int>();
            List<int> result = new List<int>(triangles.Count * 4);
            for (int face = 0; face < triangles.Count; face += 3) {
                int a = triangles[face];
                int b = triangles[face + 1];
                int c = triangles[face + 2];
                int ab = Midpoint(directions, midpoints, a, b);
                int bc = Midpoint(directions, midpoints, b, c);
                int ca = Midpoint(directions, midpoints, c, a);
                result.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            return result;
        }

        private static int Midpoint(List<double> directions, Dictionary<long, int> midpoints, int first, int second) {
            long key = ((long)Math.Min(first, second) << 32) | (uint)Math.Max(first, second);
            if (midpoints.TryGetValue(key, out int existing)) {
                return existing;
            }
            int index = directions.Count / 3;
            AddNormalised(directions,
                directions[first * 3] + directions[second * 3],
                directions[first * 3 + 1] + directions[second * 3 + 1],
                directions[first * 3 + 2] + directions[second * 3 + 2]);
            midpoints.Add(key, index);
            return index;
        }

        private static void AddNormalised(List<double> directions, double x, double y, double z) {
            double length = Math.Sqrt(x * x + y * y + z * z);
            directions.Add(x / length);
            directions.Add(y / length);
            directions.Add(z / length);
        }

        // Keeps the winding whose cross(b - a, c - a) points away from the centre.
        private static void AddOutward(List<double> directions, List<int> triangles, int a, int b, int c) {
            double abX = directions[b * 3] - directions[a * 3];
            double abY = directions[b * 3 + 1] - directions[a * 3 + 1];
            double abZ = directions[b * 3 + 2] - directions[a * 3 + 2];
            double acX = directions[c * 3] - directions[a * 3];
            double acY = directions[c * 3 + 1] - directions[a * 3 + 1];
            double acZ = directions[c * 3 + 2] - directions[a * 3 + 2];
            double crossX = abY * acZ - abZ * acY;
            double crossY = abZ * acX - abX * acZ;
            double crossZ = abX * acY - abY * acX;
            double outward = crossX * directions[a * 3] + crossY * directions[a * 3 + 1] + crossZ * directions[a * 3 + 2];
            triangles.Add(a);
            triangles.Add(outward >= 0.0 ? b : c);
            triangles.Add(outward >= 0.0 ? c : b);
        }
    }
}
