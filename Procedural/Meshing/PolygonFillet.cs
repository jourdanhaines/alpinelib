using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// Rounded corners for a counter-clockwise convex polygon: every corner turning sharper than the settings allow becomes
    /// its two tangent points (and optionally the arc's midpoint) on an arc tangent to both edges, the tangent length capped
    /// so neighbouring fillets never meet. Square roots only; vertices rounded to <see cref="ConvexPlan.Grid"/>.
    /// </summary>
    public static class PolygonFillet {
        /// <summary>
        /// <paramref name="polygon"/> with its sharp convex corners rounded per <paramref name="corners"/> (one per vertex),
        /// hulled; fewer than three vertices come back as a copy.
        /// </summary>
        public static List<PlanPoint> Round(IReadOnlyList<PlanPoint> polygon, IReadOnlyList<FilletCorner> corners, in PolygonFilletSettings settings) {
            if (polygon == null) throw new ArgumentNullException(nameof(polygon));
            if (corners == null) throw new ArgumentNullException(nameof(corners));
            if (!settings.IsBuilt) throw new ArgumentException("Settings were never constructed.", nameof(settings));

            int count = polygon.Count;
            if (count < 3) return new List<PlanPoint>(polygon);
            if (corners.Count != count) throw new ArgumentException("One corner is needed per polygon vertex.", nameof(corners));

            var rounded = new List<PlanPoint>(count * 3);
            for (int index = 0; index < count; index++) {
                PlanPoint before = polygon[(index + count - 1) % count], at = polygon[index], after = polygon[(index + 1) % count];
                AddCorner(rounded, before, at, after, corners[index], settings);
            }
            return ConvexPlan.Hull(rounded);
        }

        /// <summary>
        /// The largest radius the corner at <paramref name="index"/> takes before its tangent points pass
        /// <paramref name="edgeShare"/> of either neighbouring edge; 0 for a straight, reflex or degenerate corner.
        /// </summary>
        public static double MaxRadius(IReadOnlyList<PlanPoint> polygon, int index, double edgeShare) {
            if (polygon == null) throw new ArgumentNullException(nameof(polygon));
            if (polygon.Count < 3) throw new ArgumentException("A corner needs a polygon of at least three vertices.", nameof(polygon));
            if (index < 0 || index >= polygon.Count) throw new ArgumentOutOfRangeException(nameof(index), index, "Index must name a vertex.");
            if (!(edgeShare > 0.0)) throw new ArgumentOutOfRangeException(nameof(edgeShare), edgeShare, "Edge share must be positive.");

            int count = polygon.Count;
            PlanPoint before = polygon[(index + count - 1) % count], at = polygon[index], after = polygon[(index + 1) % count];
            if (!TryCorner(before, at, after, out double _, out double _, out double _, out double _, out double cosine) || cosine >= 1.0 - 1e-12) return 0.0;

            double halfTan = Math.Sqrt((1.0 - cosine) / (1.0 + cosine));
            return edgeShare * Math.Min(Length(before, at), Length(at, after)) / halfTan;
        }

        // The corner at `at` as itself (blunt enough) or as its tangent points (and midpoint) on the corner's arc.
        private static void AddCorner(List<PlanPoint> into, PlanPoint before, PlanPoint at, PlanPoint after, FilletCorner corner, in PolygonFilletSettings settings) {
            if (!TryCorner(before, at, after, out double inX, out double inZ, out double outX, out double outZ, out double cosine) || cosine >= settings.SharpTurnCosine) {
                into.Add(at);
                return;
            }
            // tan(turn / 2) from the turn's cosine; the tangent length is capped so neighbouring fillets never meet.
            double halfTan = Math.Sqrt((1.0 - cosine) / (1.0 + cosine));
            double tangent = Math.Min(corner.Radius * halfTan, settings.EdgeShare * Math.Min(Length(before, at), Length(at, after)));
            double radius = tangent / halfTan;
            var first = new PlanPoint(at.X - inX * tangent, at.Z - inZ * tangent);
            var last = new PlanPoint(at.X + outX * tangent, at.Z + outZ * tangent);
            into.Add(ConvexPlan.Round(first));
            if (corner.HasMidpoint) AddMidpoint(into, at, first, inX, inZ, radius);
            into.Add(ConvexPlan.Round(last));
        }

        // The arc centre lies `radius` inward (left of a counter-clockwise edge) of the first tangent point.
        private static void AddMidpoint(List<PlanPoint> into, PlanPoint at, PlanPoint first, double inX, double inZ, double radius) {
            double centreX = first.X - inZ * radius, centreZ = first.Z + inX * radius;
            double towardX = at.X - centreX, towardZ = at.Z - centreZ;
            double reach = Math.Sqrt(towardX * towardX + towardZ * towardZ);
            if (reach > 1e-9) into.Add(ConvexPlan.Round(new PlanPoint(centreX + towardX / reach * radius, centreZ + towardZ / reach * radius)));
        }

        // Unit edge directions into and out of `at` and the cosine of the turn; false when an edge is degenerate or the corner is not convex.
        private static bool TryCorner(PlanPoint before, PlanPoint at, PlanPoint after, out double inX, out double inZ, out double outX, out double outZ, out double cosine) {
            inX = at.X - before.X;
            inZ = at.Z - before.Z;
            outX = after.X - at.X;
            outZ = after.Z - at.Z;
            double inLength = Math.Sqrt(inX * inX + inZ * inZ), outLength = Math.Sqrt(outX * outX + outZ * outZ);
            cosine = 1.0;
            if (inLength < 1e-9 || outLength < 1e-9) return false;

            inX /= inLength;
            inZ /= inLength;
            outX /= outLength;
            outZ /= outLength;
            cosine = inX * outX + inZ * outZ;
            // Convex only: a counter-clockwise polygon turns left.
            return inX * outZ - inZ * outX > 0.0;
        }

        private static double Length(PlanPoint start, PlanPoint end) {
            double deltaX = end.X - start.X, deltaZ = end.Z - start.Z;
            return Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        }
    }
}
