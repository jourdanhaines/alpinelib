using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// Convex plan polygons in double precision: hulls, outward-only vertex reduction and cleanup, half-plane
    /// and rectangle clips, fan splits and point queries. Polygons are counter-clockwise by sign (shoelace area
    /// &gt; 0 in (x, z)); the outward normal of edge (dx, dz) is (dz, −dx), as for a collision prism.
    /// </summary>
    /// <remarks>
    /// Deterministic: fixed iteration order, every loop bounded by the vertex count, no trigonometry, and every
    /// stored vertex rounded to <see cref="Grid"/>. A turn is measured as the sine of the angle between two
    /// edges, <c>cross(e, f) / (|e| |f|)</c>.
    /// </remarks>
    public static class ConvexPlan {
        /// <summary>The grid every stored vertex is rounded to, metres.</summary>
        public const double Grid = 1e-4;

        private const double ParallelEpsilon = 1e-12;

        /// <summary>The value rounded to <see cref="Grid"/>.</summary>
        public static double Round(double value) {
            return Math.Round(value / Grid) * Grid;
        }

        /// <summary>The point rounded to <see cref="Grid"/>.</summary>
        public static PlanPoint Round(PlanPoint point) {
            return new PlanPoint(Round(point.X), Round(point.Z));
        }

        /// <summary>
        /// The convex hull (monotone chain) of <paramref name="points"/> rounded to the grid, counter-clockwise,
        /// collinear points dropped. Fewer than three distinct points give what is left.
        /// </summary>
        public static List<PlanPoint> Hull(IReadOnlyList<PlanPoint> points) {
            if (points == null) throw new ArgumentNullException(nameof(points));

            var sorted = new List<PlanPoint>(points.Count);
            foreach (PlanPoint point in points) sorted.Add(Round(point));
            sorted.Sort(CompareXThenZ);
            for (int index = sorted.Count - 1; index > 0; index--) {
                if (sorted[index].Equals(sorted[index - 1])) sorted.RemoveAt(index);
            }
            var hull = new List<PlanPoint>(sorted.Count + 1);
            for (int index = 0; index < sorted.Count; index++) AddToChain(hull, sorted[index], 2);
            int lowerCount = hull.Count + 1;
            for (int index = sorted.Count - 2; index >= 0; index--) AddToChain(hull, sorted[index], lowerCount);
            if (hull.Count > 1) hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        /// <summary>
        /// A convex polygon of at most <paramref name="maxVertices"/> (≥ 3) vertices holding <paramref name="hull"/> up to
        /// grid rounding (meets round to nearest, so a hull point may sit about 1e-4 outside): repeatedly the edge
        /// whose removal adds least area is collapsed, its two neighbouring edges extended until they meet. Collapses
        /// whose neighbours do not meet in front of the polygon are skipped.
        /// </summary>
        public static List<PlanPoint> EncloseMaxVertices(IReadOnlyList<PlanPoint> hull, int maxVertices) {
            return Enclose(hull, maxVertices, false);
        }

        /// <summary>
        /// As <see cref="EncloseMaxVertices"/>, but each collapse is the one whose new corner protrudes least from the
        /// removed edge (least area on a tie), so no thin spike grows where a cheap-area one would. The prism fitter
        /// uses this to keep standoff low.
        /// </summary>
        public static List<PlanPoint> EncloseMaxVerticesByProtrusion(IReadOnlyList<PlanPoint> hull, int maxVertices) {
            return Enclose(hull, maxVertices, true);
        }

        private static List<PlanPoint> Enclose(IReadOnlyList<PlanPoint> hull, int maxVertices, bool byProtrusion) {
            if (hull == null) throw new ArgumentNullException(nameof(hull));
            if (maxVertices < 3) throw new ArgumentOutOfRangeException(nameof(maxVertices), "At least three vertices.");

            var polygon = Rounded(hull);
            int passes = polygon.Count;
            for (int pass = 0; pass < passes && polygon.Count > maxVertices; pass++) {
                int edge;
                PlanPoint meet;
                bool found = byProtrusion ? TryLowestCollapse(polygon, out edge, out meet) : TryCheapestCollapse(polygon, -1, double.PositiveInfinity, out edge, out meet);
                if (!found) throw new InvalidOperationException("No edge of the polygon can be collapsed outward.");

                Collapse(polygon, edge, meet);
            }
            return polygon;
        }

        /// <summary>
        /// Outward-only cleanup: while a vertex turns less than <paramref name="minTurn"/> (sine) or an edge is
        /// shorter than <paramref name="minEdge"/>, the cheapest neighbouring edge collapse that fixes it and
        /// adds no point further than <paramref name="maxSpike"/> from the old boundary is applied.
        /// </summary>
        /// <remarks>Offenders no collapse can fix within the spike limit are left in place.</remarks>
        public static List<PlanPoint> Clean(IReadOnlyList<PlanPoint> polygon, double minTurn, double minEdge, double maxSpike) {
            return Clean(polygon, minTurn, minEdge, maxSpike, 0.0);
        }

        /// <summary>
        /// <see cref="Clean(IReadOnlyList{PlanPoint}, double, double, double)"/> that also collapses, outward, any vertex
        /// lying closer than <paramref name="minChordOffset"/> to the line through its neighbours. Convex hull cookers
        /// (PhysX) drop such vertices; <see cref="ChordOffsetFloor"/> gives the relative floor they need.
        /// </summary>
        public static List<PlanPoint> Clean(IReadOnlyList<PlanPoint> polygon, double minTurn, double minEdge, double maxSpike, double minChordOffset) {
            if (polygon == null) throw new ArgumentNullException(nameof(polygon));

            var result = Rounded(polygon);
            int passes = result.Count;
            for (int pass = 0; pass < passes && result.Count > 3; pass++) {
                if (!TryFixOffender(result, minTurn, minEdge, maxSpike, minChordOffset, out int edge, out PlanPoint meet)) break;

                Collapse(result, edge, meet);
            }
            return result;
        }

        /// <summary>
        /// The part of <paramref name="polygon"/> where <c>normalX·x + normalZ·z ≤ offset</c> (Sutherland–Hodgman).
        /// Repeated points are dropped and a polygon only touching the line from outside gives nothing.
        /// Crossing points are not rounded; whatever stores the result rounds it.
        /// </summary>
        public static List<PlanPoint> ClipHalfPlane(IReadOnlyList<PlanPoint> polygon, double normalX, double normalZ, double offset) {
            if (polygon == null) throw new ArgumentNullException(nameof(polygon));

            var output = new List<PlanPoint>(polygon.Count + 1);
            if (!HasInside(polygon, normalX, normalZ, offset)) return output;

            for (int index = 0; index < polygon.Count; index++) {
                PlanPoint current = polygon[index];
                PlanPoint next = polygon[(index + 1) % polygon.Count];
                double currentSide = normalX * current.X + normalZ * current.Z - offset;
                double nextSide = normalX * next.X + normalZ * next.Z - offset;
                if (currentSide <= 0.0) AddDistinct(output, current);
                if ((currentSide <= 0.0) == (nextSide <= 0.0)) continue;

                double share = currentSide / (currentSide - nextSide);
                AddDistinct(output, new PlanPoint(current.X + (next.X - current.X) * share, current.Z + (next.Z - current.Z) * share));
            }
            if (output.Count > 1 && output[0].Equals(output[output.Count - 1])) output.RemoveAt(output.Count - 1);
            return output;
        }

        /// <summary>The part of <paramref name="polygon"/> inside the axis-aligned rectangle [minX, maxX] × [minZ, maxZ].</summary>
        public static List<PlanPoint> ClipRect(IReadOnlyList<PlanPoint> polygon, double minX, double maxX, double minZ, double maxZ) {
            List<PlanPoint> clipped = ClipHalfPlane(polygon, 1.0, 0.0, maxX);
            clipped = ClipHalfPlane(clipped, -1.0, 0.0, -minX);
            clipped = ClipHalfPlane(clipped, 0.0, 1.0, maxZ);
            return ClipHalfPlane(clipped, 0.0, -1.0, -minZ);
        }

        /// <summary>
        /// A convex polygon fanned from vertex 0 into convex pieces of at most <paramref name="maxVertices"/> (≥ 3)
        /// vertices each, in order; their union is the polygon. A polygon already small enough comes back alone.
        /// </summary>
        public static List<List<PlanPoint>> SplitToMaxVertices(IReadOnlyList<PlanPoint> polygon, int maxVertices) {
            if (polygon == null) throw new ArgumentNullException(nameof(polygon));
            if (maxVertices < 3) throw new ArgumentOutOfRangeException(nameof(maxVertices), "At least three vertices.");

            var pieces = new List<List<PlanPoint>>();
            int start = 1;
            while (start < polygon.Count - 1) {
                int end = Math.Min(polygon.Count - 1, start + maxVertices - 2);
                var piece = new List<PlanPoint> { polygon[0] };
                for (int index = start; index <= end; index++) piece.Add(polygon[index]);
                pieces.Add(piece);
                start = end;
            }
            return pieces;
        }

        /// <summary>Distance from (x, z) to the convex polygon, 0 on or inside it.</summary>
        public static double Distance(IReadOnlyList<PlanPoint> polygon, double x, double z) {
            if (polygon == null) throw new ArgumentNullException(nameof(polygon));
            if (polygon.Count == 0) return double.PositiveInfinity;

            bool inside = polygon.Count >= 3;
            double best = double.PositiveInfinity;
            for (int index = 0; index < polygon.Count; index++) {
                PlanPoint start = polygon[index];
                PlanPoint end = polygon[(index + 1) % polygon.Count];
                double edgeX = end.X - start.X, edgeZ = end.Z - start.Z;
                double offsetX = x - start.X, offsetZ = z - start.Z;
                if (offsetX * edgeZ - offsetZ * edgeX > 0.0) inside = false;

                best = Math.Min(best, SegmentSquared(offsetX, offsetZ, edgeX, edgeZ));
            }
            return inside ? 0.0 : Math.Sqrt(best);
        }

        /// <summary>True when (x, z) lies within <paramref name="tolerance"/> of the convex polygon.</summary>
        public static bool Contains(IReadOnlyList<PlanPoint> polygon, double x, double z, double tolerance) {
            return Distance(polygon, x, z) <= tolerance;
        }

        /// <summary>Shoelace area, positive for a counter-clockwise polygon.</summary>
        public static double Area(IReadOnlyList<PlanPoint> polygon) {
            if (polygon == null) throw new ArgumentNullException(nameof(polygon));

            double doubled = 0.0;
            for (int index = 0; index < polygon.Count; index++) {
                PlanPoint current = polygon[index];
                PlanPoint next = polygon[(index + 1) % polygon.Count];
                doubled += current.X * next.Z - next.X * current.Z;
            }
            return doubled * 0.5;
        }

        /// <summary>True when the polygon has at least three vertices, positive area and every turn's sine above <paramref name="minTurn"/>.</summary>
        public static bool IsStrictlyConvexCcw(IReadOnlyList<PlanPoint> polygon, double minTurn) {
            if (polygon == null || polygon.Count < 3 || !(Area(polygon) > 0.0)) return false;

            for (int index = 0; index < polygon.Count; index++) {
                if (!(Turn(polygon, index) > minTurn)) return false;
            }
            return true;
        }

        /// <summary>
        /// How far vertex <paramref name="index"/> lies off the line through its two neighbours (negative when it turns
        /// the wrong way); 0 when the neighbours coincide.
        /// </summary>
        public static double ChordOffset(IReadOnlyList<PlanPoint> polygon, int index) {
            if (polygon == null) throw new ArgumentNullException(nameof(polygon));

            int count = polygon.Count;
            PlanPoint previous = polygon[(index + count - 1) % count], current = polygon[index], next = polygon[(index + 1) % count];
            double chordX = next.X - previous.X, chordZ = next.Z - previous.Z;
            double length = Math.Sqrt(chordX * chordX + chordZ * chordZ);
            return length > 0.0 ? Cross(previous, next, current) / -length : 0.0;
        }

        /// <summary>
        /// The least chord offset a prism of this plan and <paramref name="height"/> needs:
        /// <c>max(floor, share · diagonal)</c>, the diagonal of its 3D bounding box.
        /// </summary>
        public static double ChordOffsetFloor(IReadOnlyList<PlanPoint> plan, double height, double share, double floor) {
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
            foreach (PlanPoint point in plan) {
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minZ = Math.Min(minZ, point.Z);
                maxZ = Math.Max(maxZ, point.Z);
            }
            double width = maxX - minX, depth = maxZ - minZ;
            return Math.Max(floor, share * Math.Sqrt(width * width + depth * depth + height * height));
        }

        /// <summary>Sine of the turn at vertex <paramref name="index"/>; negative turns right, −1 for a zero-length edge.</summary>
        public static double Turn(IReadOnlyList<PlanPoint> polygon, int index) {
            int count = polygon.Count;
            PlanPoint previous = polygon[(index + count - 1) % count];
            PlanPoint current = polygon[index];
            PlanPoint next = polygon[(index + 1) % count];
            double inX = current.X - previous.X, inZ = current.Z - previous.Z;
            double outX = next.X - current.X, outZ = next.Z - current.Z;
            double lengths = Math.Sqrt((inX * inX + inZ * inZ) * (outX * outX + outZ * outZ));
            return lengths > 0.0 ? (inX * outZ - inZ * outX) / lengths : -1.0;
        }

        // True when some vertex is strictly inside, or every vertex lies on the line.
        private static bool HasInside(IReadOnlyList<PlanPoint> polygon, double normalX, double normalZ, double offset) {
            bool allOn = true;
            foreach (PlanPoint point in polygon) {
                double side = normalX * point.X + normalZ * point.Z - offset;
                if (side < 0.0) return true;
                if (side != 0.0) allOn = false;
            }
            return allOn && polygon.Count > 0;
        }

        private static void AddDistinct(List<PlanPoint> output, PlanPoint point) {
            if (output.Count > 0 && output[output.Count - 1].Equals(point)) return;

            output.Add(point);
        }

        private static int CompareXThenZ(PlanPoint left, PlanPoint right) {
            int byX = left.X.CompareTo(right.X);
            return byX != 0 ? byX : left.Z.CompareTo(right.Z);
        }

        // Adds a point to a monotone chain, popping points that would not turn strictly left.
        private static void AddToChain(List<PlanPoint> chain, PlanPoint point, int floor) {
            while (chain.Count >= floor && Cross(chain[chain.Count - 2], chain[chain.Count - 1], point) <= 0.0) chain.RemoveAt(chain.Count - 1);
            chain.Add(point);
        }

        private static double Cross(PlanPoint origin, PlanPoint first, PlanPoint second) {
            return (first.X - origin.X) * (second.Z - origin.Z) - (first.Z - origin.Z) * (second.X - origin.X);
        }

        private static List<PlanPoint> Rounded(IReadOnlyList<PlanPoint> polygon) {
            var result = new List<PlanPoint>(polygon.Count);
            foreach (PlanPoint point in polygon) result.Add(Round(point));
            return result;
        }

        private static double SegmentSquared(double offsetX, double offsetZ, double edgeX, double edgeZ) {
            double lengthSquared = edgeX * edgeX + edgeZ * edgeZ;
            double along = lengthSquared > 0.0 ? Math.Max(0.0, Math.Min(1.0, (offsetX * edgeX + offsetZ * edgeZ) / lengthSquared)) : 0.0;
            double gapX = offsetX - edgeX * along, gapZ = offsetZ - edgeZ * along;
            return gapX * gapX + gapZ * gapZ;
        }

        // The valid collapse adding least area over every edge (or only `onlyEdge` when ≥ 0), lowest index on a tie.
        private static bool TryCheapestCollapse(List<PlanPoint> polygon, int onlyEdge, double maxSpike, out int bestEdge, out PlanPoint bestMeet) {
            bestEdge = -1;
            bestMeet = default;
            double bestArea = double.PositiveInfinity;
            for (int edge = 0; edge < polygon.Count; edge++) {
                if (onlyEdge >= 0 && edge != onlyEdge) continue;
                if (!TryMeet(polygon, edge, out PlanPoint meet, out double area, out double spike) || spike > maxSpike || !(area < bestArea)) continue;

                bestArea = area;
                bestEdge = edge;
                bestMeet = meet;
            }
            return bestEdge >= 0;
        }

        // The valid collapse whose meet stands least far off the removed edge, least area then lowest index on a tie.
        private static bool TryLowestCollapse(List<PlanPoint> polygon, out int bestEdge, out PlanPoint bestMeet) {
            bestEdge = -1;
            bestMeet = default;
            double bestSpike = double.PositiveInfinity, bestArea = double.PositiveInfinity;
            for (int edge = 0; edge < polygon.Count; edge++) {
                if (!TryMeet(polygon, edge, out PlanPoint meet, out double area, out double spike)) continue;
                if (spike > bestSpike || (spike == bestSpike && !(area < bestArea))) continue;

                bestSpike = spike;
                bestArea = area;
                bestEdge = edge;
                bestMeet = meet;
            }
            return bestEdge >= 0;
        }

        // Where edge `edge`'s neighbours meet when extended, the area that adds and how far it stands off the edge.
        private static bool TryMeet(List<PlanPoint> polygon, int edge, out PlanPoint meet, out double area, out double spike) {
            int count = polygon.Count;
            meet = default;
            area = double.PositiveInfinity;
            spike = double.PositiveInfinity;
            if (count < 4) return false;

            PlanPoint before = polygon[(edge + count - 1) % count];
            PlanPoint start = polygon[edge];
            PlanPoint end = polygon[(edge + 1) % count];
            PlanPoint after = polygon[(edge + 2) % count];
            double incomingX = start.X - before.X, incomingZ = start.Z - before.Z;
            double backwardX = end.X - after.X, backwardZ = end.Z - after.Z;
            double edgeX = end.X - start.X, edgeZ = end.Z - start.Z;
            double denominator = incomingX * backwardZ - incomingZ * backwardX;
            if (Math.Abs(denominator) <= ParallelEpsilon) return false;

            double forward = (edgeX * backwardZ - edgeZ * backwardX) / denominator;
            double backward = (edgeX * incomingZ - edgeZ * incomingX) / denominator;
            if (forward < 0.0 || backward < 0.0) return false;

            meet = Round(new PlanPoint(start.X + incomingX * forward, start.Z + incomingZ * forward));
            double outward = Math.Max(0.0, (meet.X - start.X) * edgeZ - (meet.Z - start.Z) * edgeX);
            area = 0.5 * outward;
            // The new point's distance from the removed edge segment, not its line, so an obtuse end counts in full.
            spike = Math.Sqrt(SegmentSquared(meet.X - start.X, meet.Z - start.Z, edgeX, edgeZ));
            return true;
        }

        private static void Collapse(List<PlanPoint> polygon, int edge, PlanPoint meet) {
            int next = (edge + 1) % polygon.Count;
            polygon[edge] = meet;
            polygon.RemoveAt(next);
        }

        // The cheapest collapse touching any offender: a short edge (it or a neighbour) or a near-straight vertex (an edge at it).
        private static bool TryFixOffender(List<PlanPoint> polygon, double minTurn, double minEdge, double maxSpike, double minChordOffset, out int bestEdge, out PlanPoint bestMeet) {
            bestEdge = -1;
            bestMeet = default;
            double bestArea = double.PositiveInfinity;
            int count = polygon.Count;
            for (int index = 0; index < count; index++) {
                bool shortEdge = EdgeLength(polygon, index) < minEdge;
                bool straight = Turn(polygon, index) < minTurn || ChordOffset(polygon, index) < minChordOffset;
                if (!shortEdge && !straight) continue;

                int last = shortEdge ? index + 1 : index;
                for (int candidate = index - 1; candidate <= last; candidate++) {
                    int edge = (candidate + count) % count;
                    if (!TryMeet(polygon, edge, out PlanPoint meet, out double area, out double spike) || spike > maxSpike || !(area < bestArea)) continue;

                    bestArea = area;
                    bestEdge = edge;
                    bestMeet = meet;
                }
            }
            return bestEdge >= 0;
        }

        private static double EdgeLength(List<PlanPoint> polygon, int edge) {
            PlanPoint start = polygon[edge];
            PlanPoint end = polygon[(edge + 1) % polygon.Count];
            double edgeX = end.X - start.X, edgeZ = end.Z - start.Z;
            return Math.Sqrt(edgeX * edgeX + edgeZ * edgeZ);
        }
    }
}
