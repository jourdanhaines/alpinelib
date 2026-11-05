using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>Splits a triangle soup (three corners per face) into pieces whose faces share welded corners.</summary>
    public static class TrianglePieces {
        private const double WeldScale = 1e4;

        /// <summary>The face indices of each piece, pieces in order of their first face, faces ascending.</summary>
        public static List<List<int>> Split(IReadOnlyList<Vector3> corners) {
            if (corners == null) throw new ArgumentNullException(nameof(corners));

            int faces = corners.Count / 3;
            var parent = new int[faces];
            for (int face = 0; face < faces; face++) parent[face] = face;
            var owner = new Dictionary<(long, long, long), int>();
            for (int corner = 0; corner < faces * 3; corner++) {
                (long, long, long) key = Weld(corners[corner]);
                if (owner.TryGetValue(key, out int face)) Union(parent, face, corner / 3);
                else owner.Add(key, corner / 3);
            }

            var byRoot = new Dictionary<int, List<int>>();
            var pieces = new List<List<int>>();
            for (int face = 0; face < faces; face++) {
                int root = Find(parent, face);
                if (!byRoot.TryGetValue(root, out List<int> piece)) {
                    piece = new List<int>();
                    byRoot.Add(root, piece);
                    pieces.Add(piece);
                }
                piece.Add(face);
            }
            return pieces;
        }

        /// <summary>The corners of the faces in <paramref name="faces"/>, three per face.</summary>
        public static List<Vector3> Corners(IReadOnlyList<Vector3> corners, IEnumerable<int> faces) {
            var result = new List<Vector3>();
            foreach (int face in faces) {
                result.Add(corners[face * 3]);
                result.Add(corners[face * 3 + 1]);
                result.Add(corners[face * 3 + 2]);
            }
            return result;
        }

        private static (long, long, long) Weld(Vector3 point) {
            return ((long)Math.Round(point.X * WeldScale), (long)Math.Round(point.Y * WeldScale), (long)Math.Round(point.Z * WeldScale));
        }

        private static int Find(int[] parent, int face) {
            while (parent[face] != face) {
                parent[face] = parent[parent[face]];
                face = parent[face];
            }
            return face;
        }

        private static void Union(int[] parent, int first, int second) {
            int firstRoot = Find(parent, first);
            int secondRoot = Find(parent, second);
            if (firstRoot != secondRoot) parent[Math.Max(firstRoot, secondRoot)] = Math.Min(firstRoot, secondRoot);
        }
    }
}
