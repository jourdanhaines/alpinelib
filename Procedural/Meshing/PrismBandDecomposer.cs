using System;
using System.Collections.Generic;
using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// Geometry helpers for <see cref="PrismBand"/>: slab clipping of look faces in double precision and the
    /// finishing of a prism plan (hull, outward enclosure to the vertex budget, outward cleanup, rectangle fallback).
    /// </summary>
    internal static class PrismBandDecomposer {
        /// <summary>The faces' parts between two heights in double precision, each a plan-convex polygon.</summary>
        public static List<List<SlabPoint>> SlabFragments(IReadOnlyList<Vector3> corners, double bottom, double top) {
            var fragments = new List<List<SlabPoint>>();
            var face = new List<SlabPoint>(3);
            for (int index = 0; index + 2 < corners.Count; index += 3) {
                face.Clear();
                for (int corner = 0; corner < 3; corner++) face.Add(new SlabPoint(corners[index + corner].X, corners[index + corner].Y, corners[index + corner].Z));
                List<SlabPoint> above = Clip(face, 0.0, -1.0, 0.0, -bottom);
                List<SlabPoint> slab = Clip(above, 0.0, 1.0, 0.0, top);
                if (slab.Count > 0) fragments.Add(slab);
            }
            return fragments;
        }

        /// <summary>
        /// Sutherland–Hodgman: the part of a fragment where <c>a·x + b·y + c·z ≤ offset</c>, consecutive repeats
        /// dropped. A fragment that only touches the boundary from outside gives nothing, so a cut never spawns a
        /// sliver; one lying wholly on the boundary is kept.
        /// </summary>
        public static List<SlabPoint> Clip(List<SlabPoint> fragment, double a, double b, double c, double offset) {
            var output = new List<SlabPoint>(fragment.Count + 1);
            if (!HasInside(fragment, a, b, c, offset)) return output;

            for (int index = 0; index < fragment.Count; index++) {
                SlabPoint current = fragment[index];
                SlabPoint next = fragment[(index + 1) % fragment.Count];
                double currentSide = a * current.X + b * current.Y + c * current.Z - offset;
                double nextSide = a * next.X + b * next.Y + c * next.Z - offset;
                if (currentSide <= 0.0) AddDistinct(output, current);
                if ((currentSide <= 0.0) == (nextSide <= 0.0)) continue;

                double share = currentSide / (currentSide - nextSide);
                AddDistinct(output, new SlabPoint(current.X + (next.X - current.X) * share, current.Y + (next.Y - current.Y) * share, current.Z + (next.Z - current.Z) * share));
            }
            if (output.Count > 1 && output[0].Equals(output[output.Count - 1])) output.RemoveAt(output.Count - 1);
            return output;
        }

        // True when some point is strictly inside, or every point lies on the boundary.
        private static bool HasInside(List<SlabPoint> fragment, double a, double b, double c, double offset) {
            bool allOn = true;
            foreach (SlabPoint point in fragment) {
                double side = a * point.X + b * point.Y + c * point.Z - offset;
                if (side < 0.0) return true;
                if (side != 0.0) allOn = false;
            }
            return allOn && fragment.Count > 0;
        }

        private static void AddDistinct(List<SlabPoint> output, SlabPoint point) {
            if (output.Count > 0 && output[output.Count - 1].Equals(point)) return;

            output.Add(point);
        }

        /// <summary>
        /// A prism plan of 3 to <see cref="PrismEnvelopeSettings.MaxVertices"/> vertices holding the points that
        /// survives rounding and a turn: hull, enclose, outward cleanup, else the least-area holding rectangle.
        /// </summary>
        /// <remarks><paramref name="fellBack"/> is set when cleanup could not fix the plan and the rectangle was used.</remarks>
        public static List<PlanPoint> FinishPlan(List<PlanPoint> points, PrismEnvelopeSettings settings, out bool fellBack) {
            fellBack = false;
            List<PlanPoint> hull = ConvexPlan.Hull(points);
            if (hull.Count < 3 || !(ConvexPlan.Area(hull) > settings.MinEdge * settings.MinEdge)) return ThinRectangle(hull, settings.MinEdge);

            List<PlanPoint> enclosed = ConvexPlan.EncloseMaxVertices(hull, settings.MaxVertices);
            List<PlanPoint> cleaned = ConvexPlan.Clean(enclosed, settings.MinTurn, settings.MinEdge, settings.MinEdge * 2.0);
            if (IsRobust(cleaned, settings)) return cleaned;

            fellBack = true;
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

        // Fragments fanned back into float triangles for the raster, which only guides cuts; a fragment of one or
        // two points becomes a flat sliver.
        public static List<Vector3> Triangles(List<List<SlabPoint>> fragments) {
            var triangles = new List<Vector3>();
            foreach (List<SlabPoint> fragment in fragments) {
                if (fragment.Count < 3) {
                    triangles.Add(ToFloat(fragment[0]));
                    triangles.Add(ToFloat(fragment[fragment.Count - 1]));
                    triangles.Add(ToFloat(fragment[0]));
                    continue;
                }
                for (int fan = 1; fan + 1 < fragment.Count; fan++) {
                    triangles.Add(ToFloat(fragment[0]));
                    triangles.Add(ToFloat(fragment[fan]));
                    triangles.Add(ToFloat(fragment[fan + 1]));
                }
            }
            return triangles;
        }

        private static Vector3 ToFloat(SlabPoint point) {
            return new Vector3((float)point.X, (float)point.Y, (float)point.Z);
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

        public static double MinWidth(List<PlanPoint> plan) {
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity, minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
            foreach (PlanPoint point in plan) {
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minZ = Math.Min(minZ, point.Z);
                maxZ = Math.Max(maxZ, point.Z);
            }
            return Math.Min(maxX - minX, maxZ - minZ);
        }

        public static double Diameter(List<PlanPoint> plan) {
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
