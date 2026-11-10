using System;
using System.Collections.Generic;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>Segment measures against convex counter-clockwise plans, shared by the talus apron builder.</summary>
    internal static class PlanSegments {
        private const double ParallelEpsilon = 1e-12;

        /// <summary>Plan length of segment start–end.</summary>
        public static double Length(PlanPoint start, PlanPoint end) {
            double deltaX = end.X - start.X, deltaZ = end.Z - start.Z;
            return Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        }

        /// <summary>Distance from the convex polygon to segment start–end; 0 when they touch or cross.</summary>
        public static double Distance(IReadOnlyList<PlanPoint> polygon, PlanPoint start, PlanPoint end) {
            if (polygon.Count == 0) return double.PositiveInfinity;

            double startDistance = ConvexPlan.Distance(polygon, start.X, start.Z);
            double endDistance = ConvexPlan.Distance(polygon, end.X, end.Z);
            if (startDistance <= 0.0 || endDistance <= 0.0 || TryClip(polygon, start, end, out double _, out double _)) return 0.0;

            double best = Math.Min(startDistance, endDistance);
            foreach (PlanPoint vertex in polygon) best = Math.Min(best, PointDistance(vertex.X, vertex.Z, start, end));
            return best;
        }

        /// <summary>The part of segment start–end inside the convex polygon as segment parameters (Cyrus–Beck); false when it misses.</summary>
        public static bool TryClip(IReadOnlyList<PlanPoint> polygon, PlanPoint start, PlanPoint end, out double enter, out double leave) {
            enter = 0.0;
            leave = 1.0;
            double deltaX = end.X - start.X, deltaZ = end.Z - start.Z;
            for (int index = 0; index < polygon.Count && enter <= leave; index++) {
                EdgeNormal(polygon, index, out double normalX, out double normalZ, out double length);
                if (length <= 0.0) continue;

                double offset = (start.X - polygon[index].X) * normalX + (start.Z - polygon[index].Z) * normalZ;
                double rate = deltaX * normalX + deltaZ * normalZ;
                ClipParameter(offset, rate, ref enter, ref leave);
            }
            return leave - enter > ParallelEpsilon;
        }

        /// <summary>Distance from (x, z) to segment start–end.</summary>
        public static double PointDistance(double x, double z, PlanPoint start, PlanPoint end) {
            double edgeX = end.X - start.X, edgeZ = end.Z - start.Z;
            double lengthSquared = edgeX * edgeX + edgeZ * edgeZ;
            double along = lengthSquared > 0.0 ? ((x - start.X) * edgeX + (z - start.Z) * edgeZ) / lengthSquared : 0.0;
            along = Math.Max(0.0, Math.Min(1.0, along));
            var closest = new PlanPoint(start.X + edgeX * along, start.Z + edgeZ * along);
            double deltaX = closest.X - x, deltaZ = closest.Z - z;
            return Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        }

        // Unit outward normal of edge `index` ((dz, −dx) / length) and the edge length.
        private static void EdgeNormal(IReadOnlyList<PlanPoint> polygon, int index, out double normalX, out double normalZ, out double length) {
            PlanPoint start = polygon[index];
            PlanPoint end = polygon[(index + 1) % polygon.Count];
            double edgeX = end.X - start.X, edgeZ = end.Z - start.Z;
            length = Math.Sqrt(edgeX * edgeX + edgeZ * edgeZ);
            normalX = length > 0.0 ? edgeZ / length : 0.0;
            normalZ = length > 0.0 ? -edgeX / length : 0.0;
        }

        // Inside means offset + rate·t ≤ 0 against the outward normal.
        private static void ClipParameter(double offset, double rate, ref double enter, ref double leave) {
            if (Math.Abs(rate) <= ParallelEpsilon) {
                if (offset > 0.0) leave = -1.0;
                return;
            }

            double crossing = -offset / rate;
            if (rate < 0.0) enter = Math.Max(enter, crossing);
            else leave = Math.Min(leave, crossing);
        }
    }
}
