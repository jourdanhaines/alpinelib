using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// Nearest-face distance and highest-face-below queries over a triangle soup (three corners per face),
    /// faces bucketed on a plan grid so gates can sample thousands of points.
    /// </summary>
    public sealed class TriangleProximity {
        private readonly IReadOnlyList<Vector3> _corners;
        private readonly double _cell;
        private readonly Dictionary<(int, int), List<int>> _buckets = new Dictionary<(int, int), List<int>>();

        /// <summary>Buckets the faces of <paramref name="corners"/> on a plan grid of <paramref name="cell"/> metres.</summary>
        public TriangleProximity(IReadOnlyList<Vector3> corners, double cell) {
            if (corners == null) throw new ArgumentNullException(nameof(corners));
            if (!(cell > 0.0)) throw new ArgumentException("cell must be positive.", nameof(cell));

            _corners = corners;
            _cell = cell;
            for (int face = 0; face + 2 < corners.Count; face += 3) AddFace(face);
        }

        /// <summary>Distance from <paramref name="point"/> to the nearest face, at most <paramref name="cap"/>.</summary>
        public double Distance(Vector3 point, double cap) {
            int column = Bucket(point.X);
            int row = Bucket(point.Z);
            double best = cap;
            int rings = (int)Math.Ceiling(cap / _cell) + 1;
            for (int ring = 0; ring <= rings; ring++) {
                if ((ring - 1) * _cell > best) break;

                best = Math.Min(best, RingDistance(point, column, row, ring, best));
            }
            return best;
        }

        /// <summary>The highest face point straight under (x, z) no higher than <paramref name="ceiling"/>, or <paramref name="floor"/> when none is higher.</summary>
        public double HeightBelow(double x, double z, double ceiling, double floor) {
            double highest = floor;
            if (!_buckets.TryGetValue((Bucket(x), Bucket(z)), out List<int> faces)) return highest;

            foreach (int face in faces) {
                if (!PlanRaster.TryHeight(_corners[face], _corners[face + 1], _corners[face + 2], x, z, out double height)) continue;
                if (height <= ceiling && height > highest) highest = height;
            }
            return highest;
        }

        // Adds the face to every bucket its plan bounds touch.
        private void AddFace(int face) {
            Vector3 a = _corners[face], b = _corners[face + 1], c = _corners[face + 2];
            int minColumn = Bucket(Math.Min(a.X, Math.Min(b.X, c.X))), maxColumn = Bucket(Math.Max(a.X, Math.Max(b.X, c.X)));
            int minRow = Bucket(Math.Min(a.Z, Math.Min(b.Z, c.Z))), maxRow = Bucket(Math.Max(a.Z, Math.Max(b.Z, c.Z)));
            for (int row = minRow; row <= maxRow; row++) {
                for (int column = minColumn; column <= maxColumn; column++) Faces(column, row).Add(face);
            }
        }

        private List<int> Faces(int column, int row) {
            if (!_buckets.TryGetValue((column, row), out List<int> faces)) {
                faces = new List<int>();
                _buckets.Add((column, row), faces);
            }
            return faces;
        }

        private double RingDistance(Vector3 point, int column, int row, int ring, double best) {
            for (int offsetRow = -ring; offsetRow <= ring; offsetRow++) {
                for (int offsetColumn = -ring; offsetColumn <= ring; offsetColumn++) {
                    if (Math.Max(Math.Abs(offsetRow), Math.Abs(offsetColumn)) != ring) continue;

                    best = Math.Min(best, BucketDistance(point, column + offsetColumn, row + offsetRow, best));
                }
            }
            return best;
        }

        private double BucketDistance(Vector3 point, int column, int row, double best) {
            if (!_buckets.TryGetValue((column, row), out List<int> faces)) return best;

            foreach (int face in faces) best = Math.Min(best, PointTriangle(point, _corners[face], _corners[face + 1], _corners[face + 2]));
            return best;
        }

        private int Bucket(double value) => (int)Math.Floor(value / _cell);

        /// <summary>Distance from <paramref name="p"/> to triangle (a, b, c) (Ericson's closest point).</summary>
        public static double PointTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c) {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return (p - a).Length();

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return (p - b).Length();

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return (p - (a + ab * (d1 / (d1 - d3)))).Length();

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return (p - c).Length();

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return (p - (a + ac * (d2 / (d2 - d6)))).Length();

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return (p - (b + (c - b) * ((d4 - d3) / (d4 - d3 + d5 - d6)))).Length();

            float denominator = 1f / (va + vb + vc);
            return (p - (a + ab * (vb * denominator) + ac * (vc * denominator))).Length();
        }
    }
}
