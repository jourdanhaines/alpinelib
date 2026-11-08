using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// Fits stacked vertical convex prisms to a look so a walking capsule meets no invisible wall: one band at the
    /// feet (up to just under the step), fine bands through the walking band, coarse bands above it. Each band is
    /// cut into convex pieces by <see cref="PrismBandDecomposer"/>. Lowest first, a fine band halves while its side
    /// standoff p90, its lean or the air under a reachable cap is over target, down to the minimum band and within
    /// the prism budget. A containment proof samples the look and refuses a fit that leaves any of it outside.
    /// </summary>
    /// <remarks>
    /// Double maths, fixed orders and bounded loops only, no trigonometry; every plan vertex and height is rounded
    /// outward to <see cref="ConvexPlan.Grid"/>. Plans keep <see cref="PrismEnvelopeSettings.MinEdge"/> and
    /// <see cref="PrismEnvelopeSettings.MinTurn"/> so a turned, scaled and rounded copy is still a valid
    /// collision prism.
    /// </remarks>
    public static class PrismEnvelopeFitter {
        private const double StandoffCap = 2.0;
        private const double HeightEpsilon = 1e-6;

        /// <summary>The prisms around the faces in <paramref name="corners"/> (three per face).</summary>
        /// <exception cref="InvalidOperationException">A look sample would lie outside every prism.</exception>
        public static PrismEnvelopeFit Fit(IReadOnlyList<Vector3> corners, PrismEnvelopeSettings settings) {
            if (corners == null) throw new ArgumentNullException(nameof(corners));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (corners.Count % 3 != 0) throw new ArgumentException("Corners must come three per face.", nameof(corners));

            settings.Validate();
            List<double> edges = BandEdges(corners, settings);
            var bands = new List<List<PrismPiece>>();
            int used = 0;
            for (int band = 0; band + 1 < edges.Count; band++) {
                int budget = Math.Max(1, (settings.MaxPrisms - used) / (edges.Count - 1 - band));
                bands.Add(PrismBandDecomposer.Decompose(corners, edges[band], edges[band + 1], settings, budget));
                used += bands[band].Count;
            }

            HalveBands(corners, settings, edges, bands);
            return Finish(corners, settings, edges, bands);
        }

        // Feet band from the lowest look point, the fine edges, then equal coarse bands up to the highest point.
        private static List<double> BandEdges(IReadOnlyList<Vector3> corners, PrismEnvelopeSettings settings) {
            var edges = new List<double>();
            if (corners.Count == 0) return edges;

            double low = double.PositiveInfinity, high = double.NegativeInfinity;
            foreach (Vector3 corner in corners) {
                low = Math.Min(low, corner.Y);
                high = Math.Max(high, corner.Y);
            }
            low = Math.Floor(low / ConvexPlan.Grid) * ConvexPlan.Grid;
            high = Math.Ceiling(high / ConvexPlan.Grid) * ConvexPlan.Grid;
            var nominal = new List<double> { settings.LookHeight(settings.StepOffset - settings.FeetMargin) };
            foreach (double edge in settings.FineEdges) nominal.Add(settings.LookHeight(edge));
            nominal.Add(settings.LookHeight(settings.WalkingTop));

            edges.Add(low);
            foreach (double edge in nominal) {
                double rounded = ConvexPlan.Round(edge);
                if (rounded > edges[edges.Count - 1] && rounded < high) edges.Add(rounded);
            }
            AddCoarseEdges(edges, high, settings);
            return edges;
        }

        private static void AddCoarseEdges(List<double> edges, double high, PrismEnvelopeSettings settings) {
            double start = edges[edges.Count - 1];
            double coarse = settings.CoarseBand / settings.MaxScale;
            int count = Math.Max(1, (int)Math.Ceiling((high - start) / coarse - HeightEpsilon));
            count = Math.Max(1, Math.Min(count, settings.MaxPrisms - (edges.Count - 1)));
            for (int band = 1; band < count; band++) edges.Add(ConvexPlan.Round(start + (high - start) * band / count));
            edges.Add(high);
        }

        // Lowest first, halves the first fine band over a target, re-fitting both halves, until none is or the budget ends.
        private static void HalveBands(IReadOnlyList<Vector3> corners, PrismEnvelopeSettings settings, List<double> edges, List<List<PrismPiece>> bands) {
            int iterations = settings.MaxPrisms * 4;
            for (int iteration = 0; iteration < iterations; iteration++) {
                EnvelopeMetrics metrics = Metrics(corners, Prisms(bands), settings);
                int band = FirstOverTarget(corners, settings, edges, metrics);
                if (band < 0) return;

                int free = settings.MaxPrisms - (Count(bands) - bands[band].Count);
                if (free < 2) return;

                double middle = ConvexPlan.Round((edges[band] + edges[band + 1]) * 0.5);
                List<PrismPiece> lower = PrismBandDecomposer.Decompose(corners, edges[band], middle, settings, free / 2);
                List<PrismPiece> upper = PrismBandDecomposer.Decompose(corners, middle, edges[band + 1], settings, free - lower.Count);
                bands[band] = lower;
                bands.Insert(band + 1, upper);
                edges.Insert(band + 1, middle);
            }
        }

        private static int FirstOverTarget(IReadOnlyList<Vector3> corners, PrismEnvelopeSettings settings, List<double> edges, EnvelopeMetrics metrics) {
            double feetTop = ConvexPlan.Round(settings.LookHeight(settings.StepOffset - settings.FeetMargin)) + HeightEpsilon;
            double fineTop = ConvexPlan.Round(settings.LookHeight(settings.WalkingTop)) + HeightEpsilon;
            double minBand = settings.MinBand / settings.MaxScale;
            for (int band = 0; band + 1 < edges.Count; band++) {
                double bottom = edges[band], top = edges[band + 1];
                if (top <= feetTop || top > fineTop || (top - bottom) * 0.5 < minBand - HeightEpsilon) continue;
                if (IsOverTarget(corners, settings, metrics, bottom, top)) return band;
            }
            return -1;
        }

        private static bool IsOverTarget(IReadOnlyList<Vector3> corners, PrismEnvelopeSettings settings, EnvelopeMetrics metrics, double bottom, double top) {
            double low = Math.Max(bottom, settings.LookHeight(settings.WalkingLow));
            double high = Math.Min(top, settings.LookHeight(settings.WalkingTop));
            if (low < high && EnvelopeMetrics.Quantile(metrics.Standoff(low, high, StandoffCap), 0.9) > settings.StandoffTarget) return true;
            if (LeanSpread(corners, bottom, top) > settings.LeanError) return true;
            if (top > settings.LookHeight(settings.ReachableTop) + HeightEpsilon) return false;

            return EnvelopeMetrics.Quantile(metrics.LedgeAir(settings.Ground, bottom + HeightEpsilon, top + ConvexPlan.Grid), 1.0) > settings.LedgeAirTarget;
        }

        // How far the hulls of the band's lower and upper halves stand apart: the plan growth lean costs the band.
        private static double LeanSpread(IReadOnlyList<Vector3> corners, double bottom, double top) {
            double middle = (bottom + top) * 0.5;
            List<PlanPoint> lower = ConvexPlan.Hull(PlanPointsOf(PrismBandDecomposer.SlabFragments(corners, bottom, middle)));
            List<PlanPoint> upper = ConvexPlan.Hull(PlanPointsOf(PrismBandDecomposer.SlabFragments(corners, middle, top)));
            if (lower.Count < 3 || upper.Count < 3) return 0.0;

            return Math.Max(FarthestOutside(lower, upper), FarthestOutside(upper, lower));
        }

        private static double FarthestOutside(List<PlanPoint> points, List<PlanPoint> polygon) {
            double farthest = 0.0;
            foreach (PlanPoint point in points) farthest = Math.Max(farthest, ConvexPlan.Distance(polygon, point.X, point.Z));
            return farthest;
        }

        private static List<PlanPoint> PlanPointsOf(List<List<Vector3>> fragments) {
            var points = new List<PlanPoint>();
            foreach (List<Vector3> fragment in fragments) {
                foreach (Vector3 point in fragment) points.Add(new PlanPoint(point.X, point.Z));
            }
            return points;
        }

        private static PrismEnvelopeFit Finish(IReadOnlyList<Vector3> corners, PrismEnvelopeSettings settings, List<double> edges, List<List<PrismPiece>> bands) {
            List<EnvelopePrism> prisms = Prisms(bands);
            double worstOutside = WorstOutside(corners, prisms, settings.SampleSpacing);
            if (worstOutside > settings.ContainmentTolerance) {
                throw new InvalidOperationException("Prism fit leaves the look up to " + worstOutside.ToString("0.###", CultureInfo.InvariantCulture) + " outside its prisms.");
            }

            EnvelopeMetrics metrics = Metrics(corners, prisms, settings);
            List<double> standoff = metrics.Standoff(settings.LookHeight(settings.WalkingLow), settings.LookHeight(settings.GateTop), StandoffCap);
            List<double> air = metrics.LedgeAir(settings.Ground, settings.LookHeight(settings.StepOffset), settings.LookHeight(settings.ReachableTop));
            double standoffP90 = EnvelopeMetrics.Quantile(standoff, 0.9);
            double standoffMax = EnvelopeMetrics.Quantile(standoff, 1.0);
            double ledgeAirMax = EnvelopeMetrics.Quantile(air, 1.0);
            string report = Report(edges, bands, prisms.Count, standoffP90, standoffMax, ledgeAirMax, worstOutside, settings);
            return new PrismEnvelopeFit(prisms, edges, metrics, standoffP90, standoffMax, ledgeAirMax, worstOutside, report);
        }

        private static EnvelopeMetrics Metrics(IReadOnlyList<Vector3> corners, List<EnvelopePrism> prisms, PrismEnvelopeSettings settings) {
            return new EnvelopeMetrics(corners, null, null, prisms, settings.SampleSpacing) { LedgeLip = settings.LedgeLip };
        }

        private static List<EnvelopePrism> Prisms(List<List<PrismPiece>> bands) {
            var prisms = new List<EnvelopePrism>();
            foreach (List<PrismPiece> band in bands) {
                foreach (PrismPiece piece in band) prisms.Add(piece.ToPrism());
            }
            return prisms;
        }

        private static int Count(List<List<PrismPiece>> bands) {
            int count = 0;
            foreach (List<PrismPiece> band in bands) count += band.Count;
            return count;
        }

        /// <summary>The furthest any look vertex or triangle sample (every <paramref name="spacing"/>) lies outside every prism.</summary>
        public static double WorstOutside(IReadOnlyList<Vector3> corners, IReadOnlyList<EnvelopePrism> prisms, double spacing) {
            if (corners == null) throw new ArgumentNullException(nameof(corners));
            if (prisms == null) throw new ArgumentNullException(nameof(prisms));

            double worst = 0.0;
            for (int face = 0; face + 2 < corners.Count; face += 3) worst = Math.Max(worst, WorstOutsideFace(corners[face], corners[face + 1], corners[face + 2], prisms, spacing));
            return worst;
        }

        private static double WorstOutsideFace(Vector3 a, Vector3 b, Vector3 c, IReadOnlyList<EnvelopePrism> prisms, double spacing) {
            double longest = Math.Max(Vector3.Distance(a, b), Math.Max(Vector3.Distance(b, c), Vector3.Distance(c, a)));
            int steps = Math.Max(1, (int)Math.Ceiling(longest / spacing));
            double worst = 0.0;
            for (int first = 0; first <= steps; first++) {
                for (int second = 0; first + second <= steps; second++) {
                    float u = (float)first / steps, v = (float)second / steps;
                    worst = Math.Max(worst, Outside(a + (b - a) * u + (c - a) * v, prisms));
                }
            }
            return worst;
        }

        private static double Outside(Vector3 point, IReadOnlyList<EnvelopePrism> prisms) {
            double best = double.PositiveInfinity;
            foreach (EnvelopePrism prism in prisms) {
                best = Math.Min(best, prism.DistanceOutside(point));
                if (best <= 0.0) return 0.0;
            }
            return best;
        }

        private static string Report(List<double> edges, List<List<PrismPiece>> bands, int total, double standoffP90, double standoffMax, double ledgeAirMax, double worstOutside, PrismEnvelopeSettings settings) {
            var text = new StringBuilder();
            for (int band = 0; band < bands.Count; band++) {
                text.Append("band ").Append(Format(edges[band])).Append("–").Append(Format(edges[band + 1])).Append(": ").Append(bands[band].Count).Append(" prism(s)\n");
            }
            text.Append("prisms ").Append(total).Append('/').Append(settings.MaxPrisms)
                .Append(", standoff p90 ").Append(Format(standoffP90)).Append(" max ").Append(Format(standoffMax))
                .Append(", ledge air max ").Append(Format(ledgeAirMax)).Append(ledgeAirMax > settings.LedgeAirMax ? " (over)" : string.Empty)
                .Append(", worst outside ").Append(Format(worstOutside));
            return text.ToString();
        }

        private static string Format(double value) {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
