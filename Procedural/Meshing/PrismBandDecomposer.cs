using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// Splits one height band of a look into convex prism pieces for <see cref="PrismEnvelopeFitter"/>. The faces
    /// are clipped to the slab; a piece's plan is the exact hull of its fragments, enclosed to the vertex budget
    /// and cleaned outward. While the deepest inward gap from a plan edge to the first occupied raster cell
    /// exceeds the notch tolerance, the piece is cut on the line through that notch perpendicular to the edge
    /// (fragments clipped to both sides, larger side first). Then pieces merge while the merged plan still passes.
    /// </summary>
    /// <remarks>Every fragment lands in some piece and every piece's plan holds its fragments, so the union holds the band.</remarks>
    internal sealed class PrismBandDecomposer {
        private readonly PrismEnvelopeSettings _settings;
        private readonly PlanFrame _frame;
        private readonly bool[] _occupied;
        private int _splitsLeft;

        private PrismBandDecomposer(PrismEnvelopeSettings settings, PlanFrame frame, bool[] occupied, int budget) {
            _settings = settings;
            _frame = frame;
            _occupied = occupied;
            _splitsLeft = budget - 1;
        }

        /// <summary>At most <paramref name="budget"/> pieces holding the faces of <paramref name="corners"/> between two heights.</summary>
        public static List<PrismPiece> Decompose(IReadOnlyList<Vector3> corners, double bottom, double top, PrismEnvelopeSettings settings, int budget) {
            List<List<Vector3>> fragments = SlabFragments(corners, bottom, top);
            var pieces = new List<PrismPiece>();
            if (fragments.Count == 0) return pieces;

            PlanFrame frame = FrameAround(fragments, settings.Cell);
            PlanRaster raster = PlanRaster.Build(Triangles(fragments), frame, top + 1.0, true);
            var occupied = new bool[frame.Count];
            var all = new bool[frame.Count];
            for (int index = 0; index < frame.Count; index++) {
                occupied[index] = raster.IsOccupied(index);
                all[index] = true;
            }

            var decomposer = new PrismBandDecomposer(settings, frame, occupied, Math.Max(1, budget));
            decomposer.Split(new PrismPiece(fragments, all), pieces);
            decomposer.Merge(pieces);
            return pieces;
        }

        /// <summary>The faces' parts between two heights, each a plan-convex polygon.</summary>
        public static List<List<Vector3>> SlabFragments(IReadOnlyList<Vector3> corners, double bottom, double top) {
            var fragments = new List<List<Vector3>>();
            var face = new List<Vector3>(3);
            var above = new List<Vector3>(8);
            for (int index = 0; index + 2 < corners.Count; index += 3) {
                face.Clear();
                face.Add(corners[index]);
                face.Add(corners[index + 1]);
                face.Add(corners[index + 2]);
                PolygonClip.Clip(face, above, 1, bottom, true);
                var slab = new List<Vector3>(8);
                PolygonClip.Clip(above, slab, 1, top, false);
                if (slab.Count > 0) fragments.Add(slab);
            }
            return fragments;
        }

        /// <summary>
        /// A prism plan of 3 to <see cref="PrismEnvelopeSettings.MaxVertices"/> vertices holding the points that
        /// survives rounding and a turn: hull, enclose, outward cleanup, else the least-area holding rectangle.
        /// </summary>
        public static List<PlanPoint> FinishPlan(List<PlanPoint> points, PrismEnvelopeSettings settings) {
            List<PlanPoint> hull = ConvexPlan.Hull(points);
            if (hull.Count < 3 || !(ConvexPlan.Area(hull) > settings.MinEdge * settings.MinEdge)) return ThinRectangle(hull, settings.MinEdge);

            List<PlanPoint> enclosed = ConvexPlan.EncloseMaxVertices(hull, settings.MaxVertices);
            List<PlanPoint> cleaned = ConvexPlan.Clean(enclosed, settings.MinTurn, settings.MinEdge, settings.MinEdge * 2.0);
            if (IsRobust(cleaned, settings)) return cleaned;

            return LeastRectangle(hull, settings.MinEdge);
        }

        /// <summary>True when the plan has 3 to max vertices, every edge at least the minimum and every turn above it.</summary>
        public static bool IsRobust(List<PlanPoint> plan, PrismEnvelopeSettings settings) {
            if (plan.Count < 3 || plan.Count > settings.MaxVertices || !ConvexPlan.IsStrictlyConvexCcw(plan, settings.MinTurn)) return false;

            for (int index = 0; index < plan.Count; index++) {
                PlanPoint start = plan[index], end = plan[(index + 1) % plan.Count];
                if (Length(end.X - start.X, end.Z - start.Z) < settings.MinEdge) return false;
            }
            return true;
        }

        // Accepts the piece or cuts it at its deepest notch and recurses, larger side first.
        private void Split(PrismPiece piece, List<PrismPiece> accepted) {
            piece.Plan = FinishPlan(piece.PlanPoints(), _settings);
            if (_splitsLeft <= 0 || !TryWorstGap(piece, out double startX, out double startZ, out double inwardX, out double inwardZ)) {
                accepted.Add(piece);
                return;
            }

            // The cut line runs inward from the notch's edge point; its normal is the edge direction.
            double normalX = -inwardZ, normalZ = inwardX;
            double offset = normalX * startX + normalZ * startZ;
            PrismPiece first = Side(piece, normalX, normalZ, offset);
            PrismPiece second = Side(piece, -normalX, -normalZ, -offset);
            if (first == null || second == null) {
                accepted.Add(piece);
                return;
            }

            _splitsLeft--;
            bool firstLarger = ConvexPlan.Area(ConvexPlan.Hull(first.PlanPoints())) >= ConvexPlan.Area(ConvexPlan.Hull(second.PlanPoints()));
            Split(firstLarger ? first : second, accepted);
            Split(firstLarger ? second : first, accepted);
        }

        // Pieces merge pairwise, earliest pair first, while the merged plan holds no notch over the tolerance.
        private void Merge(List<PrismPiece> pieces) {
            for (int first = 0; first < pieces.Count; first++) {
                for (int second = first + 1; second < pieces.Count; second++) {
                    PrismPiece merged = PrismPiece.Merge(pieces[first], pieces[second]);
                    merged.Plan = FinishPlan(merged.PlanPoints(), _settings);
                    if (WorstGap(merged, out double _, out double _, out double _, out double _) > _settings.NotchTolerance) continue;

                    pieces[first] = merged;
                    pieces.RemoveAt(second);
                    second = first;
                }
            }
        }

        // True when the deepest notch exceeds the tolerance and the piece is wide enough to cut.
        private bool TryWorstGap(PrismPiece piece, out double startX, out double startZ, out double inwardX, out double inwardZ) {
            double gap = WorstGap(piece, out startX, out startZ, out inwardX, out inwardZ);
            return gap > _settings.NotchTolerance && MinWidth(piece.Plan) >= 2.0 * _settings.Cell;
        }

        /// <summary>
        /// The deepest inward march from a plan edge to the first occupied cell the piece owns: samples every half
        /// cell along each edge, steps a quarter cell inward; leaving the plan unoccupied counts the whole chord.
        /// </summary>
        private double WorstGap(PrismPiece piece, out double worstX, out double worstZ, out double worstInX, out double worstInZ) {
            List<PlanPoint> plan = piece.Plan;
            double step = _frame.Cell * 0.25;
            int maxSteps = (int)Math.Ceiling(Diameter(plan) / step) + 1;
            double worst = -1.0;
            worstX = worstZ = worstInX = worstInZ = 0.0;
            for (int edge = 0; edge < plan.Count; edge++) {
                PlanPoint start = plan[edge], end = plan[(edge + 1) % plan.Count];
                double edgeX = end.X - start.X, edgeZ = end.Z - start.Z;
                double length = Length(edgeX, edgeZ);
                double inX = -edgeZ / length, inZ = edgeX / length;
                int samples = (int)Math.Ceiling(length / (_frame.Cell * 0.5)) + 1;
                for (int sample = 0; sample < samples; sample++) {
                    double share = samples > 1 ? (double)sample / (samples - 1) : 0.5;
                    double x = start.X + edgeX * share, z = start.Z + edgeZ * share;
                    double gap = MarchInward(piece, plan, x, z, inX, inZ, step, maxSteps);
                    if (!(gap > worst)) continue;

                    worst = gap;
                    worstX = x;
                    worstZ = z;
                    worstInX = inX;
                    worstInZ = inZ;
                }
            }
            return worst;
        }

        private double MarchInward(PrismPiece piece, List<PlanPoint> plan, double x, double z, double inX, double inZ, double step, int maxSteps) {
            for (int index = 0; index < maxSteps; index++) {
                double depth = index * step;
                double pointX = x + inX * depth, pointZ = z + inZ * depth;
                if (index > 0 && ConvexPlan.Distance(plan, pointX, pointZ) > 0.0) return depth;

                int cell = _frame.IndexAt(pointX, pointZ);
                if (cell >= 0 && piece.Cells[cell] && _occupied[cell]) return depth;
            }
            return maxSteps * step;
        }

        // The piece's part on the side normal·p ≤ offset, or null when nothing of it is there.
        private PrismPiece Side(PrismPiece piece, double normalX, double normalZ, double offset) {
            var fragments = new List<List<Vector3>>();
            foreach (List<Vector3> fragment in piece.Fragments) {
                List<Vector3> clipped = ClipFragment(fragment, normalX, normalZ, offset);
                if (clipped.Count > 0) fragments.Add(clipped);
            }
            if (fragments.Count == 0) return null;

            var cells = new bool[piece.Cells.Length];
            for (int cell = 0; cell < cells.Length; cell++) {
                if (!piece.Cells[cell]) continue;

                _frame.PointIn(cell % _frame.Columns, cell / _frame.Columns, 0.5, 0.5, out double x, out double z);
                cells[cell] = normalX * x + normalZ * z <= offset;
            }
            return new PrismPiece(fragments, cells);
        }

        // Sutherland–Hodgman against a vertical half-space, heights interpolated along the crossing edges.
        private static List<Vector3> ClipFragment(List<Vector3> fragment, double normalX, double normalZ, double offset) {
            var output = new List<Vector3>(fragment.Count + 1);
            for (int index = 0; index < fragment.Count; index++) {
                Vector3 current = fragment[index];
                Vector3 next = fragment[(index + 1) % fragment.Count];
                double currentSide = normalX * current.X + normalZ * current.Z - offset;
                double nextSide = normalX * next.X + normalZ * next.Z - offset;
                if (currentSide <= 0.0) output.Add(current);
                if ((currentSide <= 0.0) == (nextSide <= 0.0)) continue;

                double share = currentSide / (currentSide - nextSide);
                output.Add(new Vector3(
                    (float)(current.X + (next.X - (double)current.X) * share),
                    (float)(current.Y + (next.Y - (double)current.Y) * share),
                    (float)(current.Z + (next.Z - (double)current.Z) * share)));
            }
            return output;
        }

        private static PlanFrame FrameAround(List<List<Vector3>> fragments, double cell) {
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
            foreach (List<Vector3> fragment in fragments) {
                foreach (Vector3 point in fragment) {
                    minX = Math.Min(minX, point.X);
                    maxX = Math.Max(maxX, point.X);
                    minZ = Math.Min(minZ, point.Z);
                    maxZ = Math.Max(maxZ, point.Z);
                }
            }
            double pad = cell * 2.0;
            return PlanFrame.Around(0, cell, minX - pad, maxX + pad, minZ - pad, maxZ + pad);
        }

        // Fragments fanned back into triangles; a fragment of one or two points becomes a flat sliver.
        private static List<Vector3> Triangles(List<List<Vector3>> fragments) {
            var triangles = new List<Vector3>();
            foreach (List<Vector3> fragment in fragments) {
                if (fragment.Count < 3) {
                    triangles.Add(fragment[0]);
                    triangles.Add(fragment[fragment.Count - 1]);
                    triangles.Add(fragment[0]);
                    continue;
                }
                for (int fan = 1; fan + 1 < fragment.Count; fan++) {
                    triangles.Add(fragment[0]);
                    triangles.Add(fragment[fan]);
                    triangles.Add(fragment[fan + 1]);
                }
            }
            return triangles;
        }

        // A rectangle half a minimum edge either side of a degenerate hull (a point or a segment).
        private static List<PlanPoint> ThinRectangle(List<PlanPoint> hull, double minEdge) {
            PlanPoint first = hull[0];
            PlanPoint last = FarthestFrom(hull, first);
            double alongX = last.X - first.X, alongZ = last.Z - first.Z;
            double length = Length(alongX, alongZ);
            double unitX = length > 0.0 ? alongX / length : 1.0, unitZ = length > 0.0 ? alongZ / length : 0.0;
            return Rectangle(first, unitX, unitZ, -minEdge * 0.5, length + minEdge * 0.5, -minEdge * 0.5, minEdge * 0.5);
        }

        private static PlanPoint FarthestFrom(List<PlanPoint> points, PlanPoint origin) {
            PlanPoint farthest = origin;
            double best = -1.0;
            foreach (PlanPoint point in points) {
                double distance = Length(point.X - origin.X, point.Z - origin.Z);
                if (!(distance > best)) continue;

                best = distance;
                farthest = point;
            }
            return farthest;
        }

        // The least-area rectangle holding the hull with a side on one of its edges, sides at least a minimum edge.
        private static List<PlanPoint> LeastRectangle(List<PlanPoint> hull, double minEdge) {
            double bestArea = double.PositiveInfinity;
            List<PlanPoint> best = null;
            for (int edge = 0; edge < hull.Count; edge++) {
                PlanPoint start = hull[edge], end = hull[(edge + 1) % hull.Count];
                double length = Length(end.X - start.X, end.Z - start.Z);
                double unitX = (end.X - start.X) / length, unitZ = (end.Z - start.Z) / length;
                Extent(hull, start, unitX, unitZ, out double minAlong, out double maxAlong, out double minAcross, out double maxAcross);
                Widen(ref minAlong, ref maxAlong, minEdge);
                Widen(ref minAcross, ref maxAcross, minEdge);
                double area = (maxAlong - minAlong) * (maxAcross - minAcross);
                if (!(area < bestArea)) continue;

                bestArea = area;
                best = Rectangle(start, unitX, unitZ, minAlong, maxAlong, minAcross, maxAcross);
            }
            return best;
        }

        private static void Extent(List<PlanPoint> hull, PlanPoint origin, double unitX, double unitZ, out double minAlong, out double maxAlong, out double minAcross, out double maxAcross) {
            minAlong = minAcross = double.PositiveInfinity;
            maxAlong = maxAcross = double.NegativeInfinity;
            foreach (PlanPoint point in hull) {
                double offsetX = point.X - origin.X, offsetZ = point.Z - origin.Z;
                double along = offsetX * unitX + offsetZ * unitZ;
                double across = offsetZ * unitX - offsetX * unitZ;
                minAlong = Math.Min(minAlong, along);
                maxAlong = Math.Max(maxAlong, along);
                minAcross = Math.Min(minAcross, across);
                maxAcross = Math.Max(maxAcross, across);
            }
        }

        private static void Widen(ref double low, ref double high, double minimum) {
            double grow = Math.Max(0.0, minimum - (high - low)) * 0.5 + ConvexPlan.Grid;
            low -= grow;
            high += grow;
        }

        // Counter-clockwise corners of the rectangle [minAlong, maxAlong] × [minAcross, maxAcross] in the frame
        // whose x runs along (unitX, unitZ) and whose z runs along (−unitZ, unitX), rounded outward by a grid step.
        private static List<PlanPoint> Rectangle(PlanPoint origin, double unitX, double unitZ, double minAlong, double maxAlong, double minAcross, double maxAcross) {
            var corners = new List<PlanPoint>(4);
            foreach ((double along, double across) in new[] { (minAlong, minAcross), (maxAlong, minAcross), (maxAlong, maxAcross), (minAlong, maxAcross) }) {
                corners.Add(ConvexPlan.Round(new PlanPoint(origin.X + unitX * along - unitZ * across, origin.Z + unitZ * along + unitX * across)));
            }
            return corners;
        }

        private static double MinWidth(List<PlanPoint> plan) {
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
            foreach (PlanPoint point in plan) {
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minZ = Math.Min(minZ, point.Z);
                maxZ = Math.Max(maxZ, point.Z);
            }
            return Math.Min(maxX - minX, maxZ - minZ);
        }

        private static double Diameter(List<PlanPoint> plan) {
            double best = 0.0;
            for (int first = 0; first < plan.Count; first++) {
                for (int second = first + 1; second < plan.Count; second++) best = Math.Max(best, Length(plan[second].X - plan[first].X, plan[second].Z - plan[first].Z));
            }
            return best;
        }

        private static double Length(double x, double z) {
            return Math.Sqrt(x * x + z * z);
        }
    }
}
