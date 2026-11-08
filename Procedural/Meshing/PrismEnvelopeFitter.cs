using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>
    /// Fits stacked vertical convex prisms (up to 16 plan vertices) to a look so a walking capsule meets no invisible
    /// wall. Bands: feet (just under the motor's real step), fine bands to the walking top (halved where the look
    /// leans), one band to the jump reach, then one containment-only band per plateau tier. Each band starts as
    /// one <see cref="PrismBand"/> piece; the piece with the worst exposed plan standoff over target is cut at its
    /// notch, walking bands before the reach band, until every piece meets target or the budget is spent, then
    /// nearby pieces merge where the merge stays within target. A sampled containment check refuses a fit that
    /// leaves the look outside.
    /// </summary>
    /// <remarks>
    /// Double maths, fixed orders and bounded loops only, no trigonometry. Heights round outward to
    /// <see cref="ConvexPlan.Grid"/>; plan vertices round to the nearest grid point, so the look may sit about 1e-4
    /// outside a plan. The containment check samples vertices and faces every
    /// <see cref="PrismEnvelopeSettings.SampleSpacing"/> against <see cref="PrismEnvelopeSettings.ContainmentTolerance"/>,
    /// so it is a gate, not a proof: a point between samples could sit up to about a spacing out. Plans keep
    /// <see cref="PrismEnvelopeSettings.MinEdge"/> and <see cref="PrismEnvelopeSettings.MinTurn"/> so a turned, scaled
    /// and rounded copy is still a valid collision prism.
    /// </remarks>
    public static class PrismEnvelopeFitter {
        private const double StandoffCap = 2.0;
        private const double HeightEpsilon = 1e-6;
        private const double FlatNormal = 0.7;
        private const int MergeRounds = 3;

        /// <summary>The prisms around the faces in <paramref name="corners"/> (three per face).</summary>
        /// <exception cref="InvalidOperationException">
        /// A look sample would lie outside every prism, or <see cref="PrismEnvelopeSettings.Strict"/> is set and the fit
        /// misses a target (the message lists which, and where the budget ran out).
        /// </exception>
        public static PrismEnvelopeFit Fit(IReadOnlyList<Vector3> corners, PrismEnvelopeSettings settings) {
            if (corners == null) throw new ArgumentNullException(nameof(corners));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (corners.Count % 3 != 0) throw new ArgumentException("Corners must come three per face.", nameof(corners));

            settings.Validate();
            var edges = new List<double>();
            var coarse = new List<PrismPiece>();
            PrismRegion region = null;
            string ranOut = string.Empty;
            if (corners.Count > 0) {
                LookRange(corners, out double low, out double high);
                double reachTop = Math.Min(high, ConvexPlan.Round(settings.LookHeight(settings.JumpReach)));
                bool hasUpper = high > reachTop + HeightEpsilon;
                List<(double Bottom, double Top, int Kind, int Group)> slabs = RegionSlabs(settings, low, reachTop);
                double coverTop = Math.Min(high, reachTop + settings.ReachLayer / settings.MaxScale);
                region = new PrismRegion(corners, slabs, coverTop, settings, new TriangleProximity(corners, 0.5));
                foreach ((double bottom, double _, int _, int _) in slabs) edges.Add(bottom);
                edges.Add(reachTop);
                List<double> single = hasUpper ? new List<double> { reachTop, high } : new List<double>();
                int budget = settings.MaxPrisms - (hasUpper ? 1 : 0);
                if (region.HasLook) {
                    region.Start(budget);
                    ranOut = Refine(region, settings, budget);
                    RepairLedges(corners, settings, region, CoarsePieces(corners, settings, single), budget);
                }

                // The budget the walking band left buys the plateau tiers above the jump reach, at least one band.
                int coarseBands = Math.Max(1, Math.Min(settings.MaxCoarseBands, settings.MaxPrisms - region.Pieces.Count));
                List<double> tiers = hasUpper ? TierEdges(corners, settings, reachTop, high, coarseBands) : new List<double>();
                coarse = CoarsePieces(corners, settings, tiers);
                for (int tier = 1; tier < tiers.Count; tier++) edges.Add(tiers[tier]);
            }
            return Finish(corners, settings, edges, region, coarse, ranOut);
        }

        private static void LookRange(IReadOnlyList<Vector3> corners, out double low, out double high) {
            low = double.PositiveInfinity;
            high = double.NegativeInfinity;
            foreach (Vector3 corner in corners) {
                low = Math.Min(low, corner.Y);
                high = Math.Max(high, corner.Y);
            }
            low = Math.Floor(low / ConvexPlan.Grid) * ConvexPlan.Grid;
            high = Math.Ceiling(high / ConvexPlan.Grid) * ConvexPlan.Grid;
        }

        // The feet layer, the fine bands cut into layers no taller than the minimum band, the reach band in coarser
        // layers; each band is a group that starts as one piece.
        private static List<(double Bottom, double Top, int Kind, int Group)> RegionSlabs(PrismEnvelopeSettings settings, double low, double top) {
            var slabs = new List<(double Bottom, double Top, int Kind, int Group)>();
            double feetTop = ConvexPlan.Round(settings.LookHeight(settings.FeetTop));
            double walkingTop = ConvexPlan.Round(settings.LookHeight(settings.WalkingTop));
            if (feetTop > low) slabs.Add((low, Math.Min(feetTop, top), PrismLayer.Feet, 0));
            if (top <= feetTop) return slabs;

            var fine = new List<double> { Math.Max(low, feetTop) };
            foreach (double edge in settings.FineEdges) fine.Add(ConvexPlan.Round(settings.LookHeight(edge)));
            fine.Add(walkingTop);
            for (int band = 0; band + 1 < fine.Count; band++) AddLayers(slabs, settings, fine[band], Math.Min(fine[band + 1], top), settings.MinBand, band + 1);
            if (top > walkingTop) AddLayers(slabs, settings, Math.Max(walkingTop, slabs.Count > 0 ? slabs[slabs.Count - 1].Top : low), top, settings.ReachLayer, fine.Count);
            return slabs;
        }

        // Equal layers no taller than the world height given, each tagged by what the gates ask of it.
        private static void AddLayers(List<(double Bottom, double Top, int Kind, int Group)> slabs, PrismEnvelopeSettings settings, double bottom, double top, double worldHeight, int group) {
            if (!(top > bottom + HeightEpsilon)) return;

            int count = Math.Max(1, (int)Math.Ceiling((top - bottom) / (worldHeight / settings.MaxScale) - 1e-9));
            double walkingTop = settings.LookHeight(settings.WalkingTop) + HeightEpsilon;
            double gateLow = settings.LookHeight(settings.WalkingLow), gateHigh = settings.LookHeight(Math.Max(settings.GateTop, settings.ReachableTop));
            double previous = bottom;
            for (int layer = 1; layer <= count; layer++) {
                double next = layer == count ? top : ConvexPlan.Round(bottom + (top - bottom) * layer / count);
                int kind = next > walkingTop ? PrismLayer.Reach : previous < gateHigh && next > gateLow ? PrismLayer.Gate : PrismLayer.Walking;
                slabs.Add((previous, next, kind, group));
                previous = next;
            }
        }

        // One containment-only prism per tier band: the finished hull of its slab.
        private static List<PrismPiece> CoarsePieces(IReadOnlyList<Vector3> corners, PrismEnvelopeSettings settings, List<double> tiers) {
            var pieces = new List<PrismPiece>();
            for (int band = 0; band + 1 < tiers.Count; band++) {
                List<List<SlabPoint>> fragments = PrismBandDecomposer.SlabFragments(corners, tiers[band], tiers[band + 1]);
                if (fragments.Count == 0) continue;

                var points = new List<PlanPoint>();
                double bottom = double.PositiveInfinity, top = double.NegativeInfinity;
                foreach (List<SlabPoint> fragment in fragments) {
                    foreach (SlabPoint point in fragment) {
                        points.Add(new PlanPoint(point.X, point.Z));
                        bottom = Math.Min(bottom, point.Y);
                        top = Math.Max(top, point.Y);
                    }
                }
                var piece = new PrismPiece(0, new List<List<List<SlabPoint>>> { fragments }, new List<List<PlanPoint>>());
                piece.Plan = PrismBandDecomposer.FinishPlan(points, settings, out bool fellBack);
                piece.FellBack = fellBack;
                piece.Bottom = Math.Floor(bottom / ConvexPlan.Grid) * ConvexPlan.Grid;
                piece.Top = Math.Ceiling(top / ConvexPlan.Grid) * ConvexPlan.Grid;
                pieces.Add(piece);
            }
            return pieces;
        }

        // Plateau steps above the jump reach: flat faces grouped by height, a group with enough area starting a band.
        private static List<double> TierEdges(IReadOnlyList<Vector3> corners, PrismEnvelopeSettings settings, double bottom, double high, int budget) {
            var flats = new List<(double Height, double Area)>();
            double totalArea = 0.0;
            for (int face = 0; face + 2 < corners.Count; face += 3) {
                Vector3 a = corners[face], b = corners[face + 1], c = corners[face + 2];
                double planArea = 0.5 * (((double)b.X - a.X) * ((double)c.Z - a.Z) - ((double)b.Z - a.Z) * ((double)c.X - a.X));
                double height = Math.Max(a.Y, Math.Max(b.Y, c.Y));
                if (!IsFlatUp(a, b, c) || height <= bottom || height >= high) continue;

                flats.Add((height, Math.Abs(planArea)));
                totalArea += Math.Abs(planArea);
            }
            flats.Sort((first, second) => first.Height.CompareTo(second.Height));
            List<(double Height, double Area)> tiers = GroupTiers(flats, settings.TierShare * totalArea, 0.5 / settings.MaxScale);
            while (tiers.Count + 1 > Math.Max(1, budget)) tiers.RemoveAt(SmallestTier(tiers));

            var edges = new List<double> { bottom };
            double minBand = settings.MinBand / settings.MaxScale;
            foreach ((double height, double _) in tiers) {
                double rounded = Math.Ceiling(height / ConvexPlan.Grid) * ConvexPlan.Grid;
                if (rounded > edges[edges.Count - 1] + minBand && rounded < high - minBand) edges.Add(rounded);
            }
            edges.Add(high);
            return edges;
        }

        private static bool IsFlatUp(Vector3 a, Vector3 b, Vector3 c) {
            double abX = (double)b.X - a.X, abY = (double)b.Y - a.Y, abZ = (double)b.Z - a.Z;
            double acX = (double)c.X - a.X, acY = (double)c.Y - a.Y, acZ = (double)c.Z - a.Z;
            double normalX = abY * acZ - abZ * acY, normalY = abZ * acX - abX * acZ, normalZ = abX * acY - abY * acX;
            double length = Math.Sqrt(normalX * normalX + normalY * normalY + normalZ * normalZ);
            return length > 0.0 && Math.Abs(normalY) / length >= FlatNormal;
        }

        // Consecutive flats within the gap form one group at its highest flat; groups under the area floor are dropped.
        private static List<(double Height, double Area)> GroupTiers(List<(double Height, double Area)> flats, double minArea, double gap) {
            var tiers = new List<(double Height, double Area)>();
            int start = 0;
            while (start < flats.Count) {
                int end = start;
                double area = flats[start].Area;
                while (end + 1 < flats.Count && flats[end + 1].Height - flats[end].Height <= gap) {
                    end++;
                    area += flats[end].Area;
                }
                if (area >= minArea) tiers.Add((flats[end].Height, area));
                start = end + 1;
            }
            return tiers;
        }

        private static int SmallestTier(List<(double Height, double Area)> tiers) {
            int smallest = 0;
            for (int index = 1; index < tiers.Count; index++) {
                if (tiers[index].Area < tiers[smallest].Area) smallest = index;
            }
            return smallest;
        }

        // Splits the worst piece over target, most important kind first, within the budget; then merges, a few rounds.
        // Returns where the budget ran out, or an empty string.
        // First every piece is brought under the standoff max target (and every cap off the air), then, with the
        // budget left, under the p90 target: the max is a hard gate, the p90 only needs most samples close.
        private static string Refine(PrismRegion region, PrismEnvelopeSettings settings, int budget) {
            string ranOut = RefineTo(region, budget);
            region.SetTarget(settings.StandoffP90Target * settings.PieceStandoffShare);
            string ranOutLater = RefineTo(region, budget);
            return ranOut.Length > 0 ? ranOut : ranOutLater;
        }

        private static string RefineTo(PrismRegion region, int budget) {
            string ranOut = string.Empty;
            for (int round = 0; round < MergeRounds; round++) {
                ranOut = SplitWhileOver(region, budget);
                if (region.Merge() == 0 || region.WorstOver() == null) break;
            }
            return ranOut;
        }

        // Cuts through the worst reachable ledge the final metric finds, while one is over the gate and budget remains.
        private static void RepairLedges(IReadOnlyList<Vector3> corners, PrismEnvelopeSettings settings, PrismRegion region, List<PrismPiece> coarse, int budget) {
            for (int round = 0; round < budget && region.Pieces.Count < budget; round++) {
                var prisms = new List<EnvelopePrism>();
                foreach (PrismPiece piece in region.Pieces) prisms.Add(piece.ToPrism());
                foreach (PrismPiece piece in coarse) prisms.Add(piece.ToPrism());
                EnvelopeMetrics metrics = Metrics(corners, prisms, settings);
                List<double> air = metrics.LedgeAir(settings.Ground, settings.LookHeight(settings.StepOffset), settings.LookHeight(settings.ReachableTop));
                if (!(EnvelopeMetrics.Quantile(air, 1.0) > settings.LedgeAirMax)) return;

                Vector3 spot = metrics.WorstLedgePoint;
                if (!region.ForceCapCut(new Vector3(spot.X, spot.Y - 0.02f, spot.Z))) return;
            }
        }

        private static string SplitWhileOver(PrismRegion region, int budget) {
            int iterations = Math.Max(1, budget) * 16;
            for (int iteration = 0; iteration < iterations; iteration++) {
                PrismPiece worst = region.WorstOver();
                if (worst == null) return string.Empty;
                if (region.Pieces.Count >= budget) {
                    return "budget ran out at a piece " + Format(worst.Bottom) + "–" + Format(worst.Top) + " with standoff " + Format(worst.Standoff) + ", cap over air " + Format(worst.CapOverAir);
                }

                region.TrySplit(worst);
            }
            return string.Empty;
        }

        private static PrismEnvelopeFit Finish(IReadOnlyList<Vector3> corners, PrismEnvelopeSettings settings, List<double> edges, PrismRegion region, List<PrismPiece> coarse, string ranOut) {
            var pieces = new List<PrismPiece>();
            if (region != null) pieces.AddRange(region.Pieces);
            pieces.AddRange(coarse);
            var prisms = new List<EnvelopePrism>();
            foreach (PrismPiece piece in pieces) prisms.Add(piece.ToPrism());
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
            string missed = MissedTargets(prisms.Count, standoffP90, standoffMax, ledgeAirMax, settings);
            if (missed.Length > 0 && ranOut.Length > 0) missed += "; " + ranOut;
            int fallbacks = 0;
            foreach (PrismPiece piece in pieces) fallbacks += piece.FellBack ? 1 : 0;
            string report = Report(pieces, region == null ? 0 : region.Pieces.Count, standoffP90, standoffMax, ledgeAirMax, worstOutside, fallbacks, settings);
            if (settings.Strict && missed.Length > 0) throw new InvalidOperationException("Prism fit misses its targets: " + missed + ".\n" + report);

            return new PrismEnvelopeFit(prisms, edges, metrics, standoffP90, standoffMax, ledgeAirMax, worstOutside, fallbacks, missed.Length == 0, report);
        }

        // Comma-separated targets the fit misses; empty when it meets them all.
        private static string MissedTargets(int count, double standoffP90, double standoffMax, double ledgeAirMax, PrismEnvelopeSettings settings) {
            var missed = new List<string>();
            if (count > settings.MaxPrisms) missed.Add("prisms " + count + " > " + settings.MaxPrisms);
            if (standoffP90 > settings.StandoffP90Target) missed.Add("standoff p90 " + Format(standoffP90) + " > " + Format(settings.StandoffP90Target));
            if (standoffMax > settings.StandoffMaxTarget) missed.Add("standoff max " + Format(standoffMax) + " > " + Format(settings.StandoffMaxTarget));
            if (ledgeAirMax > settings.LedgeAirMax) missed.Add("ledge air " + Format(ledgeAirMax) + " > " + Format(settings.LedgeAirMax));
            return string.Join(", ", missed);
        }

        private static EnvelopeMetrics Metrics(IReadOnlyList<Vector3> corners, List<EnvelopePrism> prisms, PrismEnvelopeSettings settings) {
            return new EnvelopeMetrics(corners, null, null, prisms, settings.SampleSpacing) { LedgeLip = settings.LedgeLip };
        }

        /// <summary>The furthest any look vertex or triangle sample (every <paramref name="spacing"/>) lies outside every prism.</summary>
        public static double WorstOutside(IReadOnlyList<Vector3> corners, IReadOnlyList<EnvelopePrism> prisms, double spacing) {
            if (corners == null) throw new ArgumentNullException(nameof(corners));
            if (prisms == null) throw new ArgumentNullException(nameof(prisms));

            double worst = 0.0;
            int lastHit = 0;
            for (int face = 0; face + 2 < corners.Count; face += 3) worst = Math.Max(worst, WorstOutsideFace(corners[face], corners[face + 1], corners[face + 2], prisms, spacing, ref lastHit));
            return worst;
        }

        // Barycentric samples of one face in double precision.
        private static double WorstOutsideFace(Vector3 a, Vector3 b, Vector3 c, IReadOnlyList<EnvelopePrism> prisms, double spacing, ref int lastHit) {
            double abX = (double)b.X - a.X, abY = (double)b.Y - a.Y, abZ = (double)b.Z - a.Z;
            double acX = (double)c.X - a.X, acY = (double)c.Y - a.Y, acZ = (double)c.Z - a.Z;
            double bcX = acX - abX, bcY = acY - abY, bcZ = acZ - abZ;
            double longest = Math.Sqrt(Math.Max(abX * abX + abY * abY + abZ * abZ, Math.Max(acX * acX + acY * acY + acZ * acZ, bcX * bcX + bcY * bcY + bcZ * bcZ)));
            int steps = Math.Max(1, (int)Math.Ceiling(longest / spacing));
            double worst = 0.0;
            for (int first = 0; first <= steps; first++) {
                for (int second = 0; first + second <= steps; second++) {
                    double u = (double)first / steps, v = (double)second / steps;
                    worst = Math.Max(worst, Outside(a.X + abX * u + acX * v, a.Y + abY * u + acY * v, a.Z + abZ * u + acZ * v, prisms, ref lastHit));
                }
            }
            return worst;
        }

        // The least distance over the prisms, trying the last prism that held a sample first (same answer, fewer tests).
        private static double Outside(double x, double y, double z, IReadOnlyList<EnvelopePrism> prisms, ref int lastHit) {
            if (prisms.Count == 0) return double.PositiveInfinity;
            if (prisms[lastHit].DistanceOutside(x, y, z) <= 0.0) return 0.0;

            double best = double.PositiveInfinity;
            for (int index = 0; index < prisms.Count; index++) {
                EnvelopePrism prism = prisms[index];
                if (y < prism.Bottom - best || y > prism.Top + best) continue;

                best = Math.Min(best, prism.DistanceOutside(x, y, z));
                if (best > 0.0) continue;

                lastHit = index;
                return 0.0;
            }
            return best;
        }

        private static string Report(List<PrismPiece> pieces, int regionCount, double standoffP90, double standoffMax, double ledgeAirMax, double worstOutside, int fallbacks, PrismEnvelopeSettings settings) {
            var text = new StringBuilder();
            for (int index = 0; index < pieces.Count; index++) {
                PrismPiece piece = pieces[index];
                text.Append(index < regionCount ? "prism " : "coarse ").Append(Format(piece.Bottom)).Append("–").Append(Format(piece.Top))
                    .Append(": ").Append(piece.Plan.Count).Append(" vertices");
                if (index < regionCount) text.Append(", standoff ").Append(Format(piece.Standoff)).Append(" (layer ").Append(piece.WorstLayer).Append(", kind ").Append(piece.Kind).Append(")").Append(", cap over air ").Append(Format(piece.CapOverAir));
                if (piece.FellBack) text.Append(", rectangle fallback");
                if (piece.Unsplittable) text.Append(", stuck");
                text.Append('\n');
            }
            text.Append("prisms ").Append(pieces.Count).Append('/').Append(settings.MaxPrisms)
                .Append(", standoff p90 ").Append(Format(standoffP90)).Append(" max ").Append(Format(standoffMax))
                .Append(", ledge air max ").Append(Format(ledgeAirMax)).Append(ledgeAirMax > settings.LedgeAirMax ? " (over)" : string.Empty)
                .Append(", worst outside ").Append(Format(worstOutside))
                .Append(", rectangle fallbacks ").Append(fallbacks);
            return text.ToString();
        }

        private static string Format(double value) {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
