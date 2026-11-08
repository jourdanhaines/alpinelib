using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// The plan footprint of one band of a look: a fine raster (touched and walled-in cells) and the fragments'
    /// plan polygons bucketed for exact distance queries. Drives the standoff measure and the cuts of
    /// <see cref="PrismBand"/>.
    /// </summary>
    internal sealed class PlanFootprint {
        private const double BucketSize = 1.0;

        private readonly PlanRaster _raster;
        private readonly List<List<PlanPoint>> _polygons = new List<List<PlanPoint>>();
        private readonly List<bool> _solid = new List<bool>();
        private readonly Dictionary<(int, int), List<int>> _buckets = new Dictionary<(int, int), List<int>>();

        public PlanFootprint(List<List<SlabPoint>> fragments, double top, double cell)
            : this(fragments, top, FrameAround(fragments, cell)) { }

        /// <summary>A footprint on a given frame, so footprints of several slabs share cell indices.</summary>
        public PlanFootprint(List<List<SlabPoint>> fragments, double top, PlanFrame frame) {
            Frame = frame;
            _raster = PlanRaster.Build(PrismBandDecomposer.Triangles(fragments), Frame, top + 1.0, true);
            foreach (List<SlabPoint> fragment in fragments) AddPolygon(fragment);
            _interiorRuns = InteriorRuns();
        }

        private readonly List<int>[] _interiorRuns;

        /// <summary>
        /// The first and last walled-in column of <paramref name="row"/> within [<paramref name="fromColumn"/>,
        /// <paramref name="toColumn"/>]; false when there is none.
        /// </summary>
        public bool TryInteriorSpan(int row, int fromColumn, int toColumn, out int first, out int last) {
            first = int.MaxValue;
            last = int.MinValue;
            List<int> runs = _interiorRuns[row];
            for (int run = 0; run + 1 < runs.Count; run += 2) {
                int start = Math.Max(runs[run], fromColumn), end = Math.Min(runs[run + 1], toColumn);
                if (start > end) continue;

                first = Math.Min(first, start);
                last = Math.Max(last, end);
            }
            return first <= last;
        }

        // Per row, the walled-in cells as (start, end) column pairs.
        private List<int>[] InteriorRuns() {
            var runs = new List<int>[Frame.Rows];
            for (int row = 0; row < Frame.Rows; row++) {
                runs[row] = new List<int>();
                int start = -1;
                for (int column = 0; column <= Frame.Columns; column++) {
                    bool inside = column < Frame.Columns && _raster.IsInterior(row * Frame.Columns + column);
                    if (inside && start < 0) start = column;
                    if (inside || start < 0) continue;

                    runs[row].Add(start);
                    runs[row].Add(column - 1);
                    start = -1;
                }
            }
            return runs;
        }

        public PlanFrame Frame { get; }

        /// <summary>True when the cell is touched by a fragment or walled in.</summary>
        public bool IsOccupied(int cell) => _raster.IsOccupied(cell);

        /// <summary>True when the cell is walled in, so wholly inside the look.</summary>
        public bool IsInterior(int cell) => _raster.IsInterior(cell);

        /// <summary>True when the world point lies in a walled-in cell.</summary>
        public bool IsInteriorAt(double x, double z) {
            int cell = Frame.IndexAt(x, z);
            return cell >= 0 && _raster.IsInterior(cell);
        }

        /// <summary>True when the world point lies in a touched or walled-in cell.</summary>
        public bool IsOccupiedAt(double x, double z) {
            int cell = Frame.IndexAt(x, z);
            return cell >= 0 && _raster.IsOccupied(cell);
        }

        /// <summary>Plan distance from (x, z) to the nearest fragment (0 inside a flat one), at most <paramref name="cap"/>.</summary>
        public double Distance(double x, double z, double cap) {
            int column = Bucket(x), row = Bucket(z);
            double best = cap;
            int rings = (int)Math.Ceiling(cap / BucketSize) + 1;
            for (int ring = 0; ring <= rings; ring++) {
                if ((ring - 1) * BucketSize > best) break;

                best = RingDistance(x, z, column, row, ring, best);
            }
            return best;
        }

        private double RingDistance(double x, double z, int column, int row, int ring, double best) {
            for (int offsetRow = -ring; offsetRow <= ring; offsetRow++) {
                for (int offsetColumn = -ring; offsetColumn <= ring; offsetColumn++) {
                    if (Math.Max(Math.Abs(offsetRow), Math.Abs(offsetColumn)) != ring) continue;
                    if (!_buckets.TryGetValue((column + offsetColumn, row + offsetRow), out List<int> polygons)) continue;

                    foreach (int polygon in polygons) best = Math.Min(best, PolygonDistance(polygon, x, z));
                }
            }
            return best;
        }

        private double PolygonDistance(int index, double x, double z) {
            List<PlanPoint> polygon = _polygons[index];
            if (_solid[index]) return ConvexPlan.Distance(polygon, x, z);

            double best = double.PositiveInfinity;
            for (int edge = 0; edge < polygon.Count; edge++) {
                PlanPoint start = polygon[edge], end = polygon[(edge + 1) % polygon.Count];
                best = Math.Min(best, SegmentDistance(start, end, x, z));
            }
            return best;
        }

        private static double SegmentDistance(PlanPoint start, PlanPoint end, double x, double z) {
            double edgeX = end.X - start.X, edgeZ = end.Z - start.Z;
            double offsetX = x - start.X, offsetZ = z - start.Z;
            double lengthSquared = edgeX * edgeX + edgeZ * edgeZ;
            double along = lengthSquared > 0.0 ? Math.Max(0.0, Math.Min(1.0, (offsetX * edgeX + offsetZ * edgeZ) / lengthSquared)) : 0.0;
            double gapX = offsetX - edgeX * along, gapZ = offsetZ - edgeZ * along;
            return Math.Sqrt(gapX * gapX + gapZ * gapZ);
        }

        // A fragment's plan projection: a convex polygon when it has area (counter-clockwise), else its outline.
        private void AddPolygon(List<SlabPoint> fragment) {
            var polygon = new List<PlanPoint>(fragment.Count);
            foreach (SlabPoint point in fragment) polygon.Add(new PlanPoint(point.X, point.Z));
            double area = ConvexPlan.Area(polygon);
            if (area < 0.0) polygon.Reverse();
            int index = _polygons.Count;
            _polygons.Add(polygon);
            _solid.Add(Math.Abs(area) > 1e-9 && polygon.Count >= 3);
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
            foreach (PlanPoint point in polygon) {
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minZ = Math.Min(minZ, point.Z);
                maxZ = Math.Max(maxZ, point.Z);
            }
            for (int row = Bucket(minZ); row <= Bucket(maxZ); row++) {
                for (int column = Bucket(minX); column <= Bucket(maxX); column++) Polygons(column, row).Add(index);
            }
        }

        private List<int> Polygons(int column, int row) {
            if (!_buckets.TryGetValue((column, row), out List<int> polygons)) {
                polygons = new List<int>();
                _buckets.Add((column, row), polygons);
            }
            return polygons;
        }

        private static int Bucket(double value) => (int)Math.Floor(value / BucketSize);

        /// <summary>A yaw-0 frame holding the fragments with a two-cell margin.</summary>
        public static PlanFrame FrameAround(List<List<SlabPoint>> fragments, double cell) {
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
            foreach (List<SlabPoint> fragment in fragments) {
                foreach (SlabPoint point in fragment) {
                    minX = Math.Min(minX, point.X);
                    maxX = Math.Max(maxX, point.X);
                    minZ = Math.Min(minZ, point.Z);
                    maxZ = Math.Max(maxZ, point.Z);
                }
            }
            double pad = cell * 2.0;
            return PlanFrame.Around(0, cell, minX - pad, maxX + pad, minZ - pad, maxZ + pad);
        }
    }
}
