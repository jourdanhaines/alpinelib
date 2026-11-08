using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// Nearest-face distance and highest-face-below queries over a triangle soup (three corners per face),
    /// faces bucketed on a plan grid so gates can sample thousands of points.
    /// </summary>
    public sealed class TriangleProximity {
        private const double RimTolerance = 0.02;

        private readonly IReadOnlyList<Vector3> _corners;
        private readonly double _cell;
        private readonly Dictionary<(int, int), List<int>> _buckets = new Dictionary<(int, int), List<int>>();
        private readonly Dictionary<int, List<int>> _rows = new Dictionary<int, List<int>>();

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
                if (!PlanRaster.TryHeight(_corners[face], _corners[face + 1], _corners[face + 2], x, z, out double height)
                    && !TryRimHeight(_corners[face], _corners[face + 1], _corners[face + 2], x, z, out height)) continue;
                if (height <= ceiling && height > highest) highest = height;
            }
            return highest;
        }

        /// <summary>
        /// True when (x, y, z) lies inside the look's solid: along a ray toward +x at that height the faces crossed
        /// leaving (normal toward +x) and entering differ in number, either winding as long as it is consistent.
        /// Overlapping shells add up, and shells need no caps since only walls are crossed.
        /// </summary>
        public bool IsInside(double x, double y, double z) {
            int winding = 0;
            foreach (int face in RowFaces(Bucket(z))) winding += Crossing(face, x, y, z);
            return winding != 0;
        }

        // Every face touching a bucket row once, in face order, built on first use.
        private List<int> RowFaces(int row) {
            if (_rows.TryGetValue(row, out List<int> faces)) return faces;

            faces = new List<int>();
            for (int face = 0; face + 2 < _corners.Count; face += 3) {
                Vector3 a = _corners[face], b = _corners[face + 1], c = _corners[face + 2];
                if (Bucket(Math.Min(a.Z, Math.Min(b.Z, c.Z))) <= row && Bucket(Math.Max(a.Z, Math.Max(b.Z, c.Z))) >= row) faces.Add(face);
            }
            _rows.Add(row, faces);
            return faces;
        }

        // ±1 when the face's slice at height y crosses the ray z = const beyond x, else 0.
        private int Crossing(int face, double x, double y, double z) {
            Vector3 a = _corners[face], b = _corners[face + 1], c = _corners[face + 2];
            if (!TrySliceCrossing(a, b, c, y, z, out double crossX) || !(crossX > x)) return 0;

            double normalX = ((double)b.Y - a.Y) * ((double)c.Z - a.Z) - ((double)b.Z - a.Z) * ((double)c.Y - a.Y);
            return normalX > 0.0 ? 1 : normalX < 0.0 ? -1 : 0;
        }

        // Where the triangle's slice at height y meets the line z = const, when it does.
        private static bool TrySliceCrossing(Vector3 a, Vector3 b, Vector3 c, double y, double z, out double crossX) {
            crossX = 0.0;
            int found = 0;
            double firstX = 0.0, firstZ = 0.0, secondX = 0.0, secondZ = 0.0;
            foreach ((Vector3 start, Vector3 end) in new[] { (a, b), (b, c), (c, a) }) {
                if ((start.Y - y) * (end.Y - y) >= 0.0 && !(start.Y < y && end.Y >= y) && !(end.Y < y && start.Y >= y)) continue;
                if ((start.Y < y) == (end.Y < y)) continue;

                double share = (y - start.Y) / ((double)end.Y - start.Y);
                double pointX = start.X + ((double)end.X - start.X) * share, pointZ = start.Z + ((double)end.Z - start.Z) * share;
                if (found == 0) {
                    firstX = pointX;
                    firstZ = pointZ;
                } else {
                    secondX = pointX;
                    secondZ = pointZ;
                }
                found++;
            }
            if (found != 2 || (firstZ < z) == (secondZ < z)) return false;

            crossX = firstX + (secondX - firstX) * (z - firstZ) / (secondZ - firstZ);
            return true;
        }

        // A point within 2 cm of a face's plan outline (the metrics' covered tolerance; the barycentric test misses
        // sliver faces): the height of the outline there, so a column grazing a look edge still finds it.
        private static bool TryRimHeight(Vector3 a, Vector3 b, Vector3 c, double x, double z, out double height) {
            height = double.NegativeInfinity;
            bool found = false;
            foreach ((Vector3 start, Vector3 end) in new[] { (a, b), (b, c), (c, a) }) {
                double edgeX = (double)end.X - start.X, edgeZ = (double)end.Z - start.Z;
                double lengthSquared = edgeX * edgeX + edgeZ * edgeZ;
                double along = lengthSquared > 0.0 ? Math.Max(0.0, Math.Min(1.0, ((x - start.X) * edgeX + (z - start.Z) * edgeZ) / lengthSquared)) : 0.0;
                double gapX = x - (start.X + edgeX * along), gapZ = z - (start.Z + edgeZ * along);
                if (gapX * gapX + gapZ * gapZ > RimTolerance * RimTolerance) continue;

                height = Math.Max(height, start.Y + ((double)end.Y - start.Y) * along);
                found = true;
            }
            return found;
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
