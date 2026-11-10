using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AllAboard.WorldGen;
using AlpineLib.Procedural.Meshing;

namespace AllAboard.GoldenRecorder {
    /// <summary>
    /// The game's talus builder as a thin adapter over <see cref="TalusApron.Lay"/> (rise policy, bare edges, voids and the
    /// clear test stay game-side), checked against the game's whole island on every build; every lay becomes a candidate case.
    /// </summary>
    public static class TalusRecorder {
        public static readonly TalusApronSettings Settings = new TalusApronSettings(0.3, 0.1, LandmarkTalusGame.GradientMax, 1.01, 2, 10, 0.5, 1.0, 1.5, 0.02);
        public static readonly List<TalusCandidate> Candidates = new List<TalusCandidate>();
        private const double NeighbourGap = 2.0;
        private const double MinRise = 2.0;
        private const double JumpReach = 1.5, ReachAcross = 1.5, ReachMargin = 0.1;
        private const double SeamClearance = 1.75, SeamReach = 0.6;
        private const double SeamRiseStep = 0.8;
        private const double SampleStep = 0.5;

        public static void Check(LandmarkTalusGame game, TalusInputs inputs) {
            var island = new TalusIsland(inputs.Rises);
            var hulls = new List<IReadOnlyList<PlanPoint>>();
            if (inputs.Neighbours.Count > 0) hulls.Add(ConvexPlan.Hull(inputs.Neighbours.SelectMany((plan) => plan).ToList()));
            var firstTops = new double[inputs.Masses.Count];
            for (int mass = 0; mass < inputs.Masses.Count; mass++) firstTops[mass] = LandmarkShape.FirstColliderTop(inputs.Masses[mass]);
            for (int mass = 0; mass < inputs.Feet.Count; mass++) AddMass(inputs, island, mass, hulls, firstTops);
            Recorder.Check("talus.build", IsSame(game, island, inputs.Slope));
        }

        private static void AddMass(TalusInputs inputs, TalusIsland island, int mass, List<IReadOnlyList<PlanPoint>> hulls, double[] firstTops) {
            var bareEdges = new bool[inputs.Masses[mass].Base.Count];
            for (int edge = 0; edge < bareEdges.Length; edge++) bareEdges[edge] = inputs.Bare[mass][edge] || inputs.Rises[mass] <= 0.0;
            IReadOnlyList<PlanPoint> plan = inputs.Feet[mass];
            if (plan.Count < 3) return;

            double slope = inputs.Slope;
            island.Rises[mass] = Math.Min(island.Rises[mass], ConvexPlan.Round(0.8 * LandmarkFilletsGame.Width(plan) * slope));
            island.Rises[mass] = Unreachable(plan, island.Rises[mass], slope, inputs.Tops);
            if (island.Rises[mass] < MinRise) island.Rises[mass] = 0.0;
            var foot = new LandmarkTalusFoot(inputs.Feet, mass, inputs.Masses[mass].Base, bareEdges, inputs.Clefts, inputs.Alcoves, inputs.Tunnel, inputs.Context, hulls, firstTops);
            List<IReadOnlyList<PlanPoint>> others = inputs.Feet.Where((other, index) => index != mass).ToList();
            TalusApronResult result = Lay(foot, plan, others, island.Rises[mass], slope);
            // Laid again a step lower while pieces meet at a seam step; under the least rise the foot goes bare.
            while (!result.IsLaid && ConvexPlan.Round(island.Rises[mass] * SeamRiseStep) >= MinRise) {
                island.Rises[mass] = ConvexPlan.Round(island.Rises[mass] * SeamRiseStep);
                result = Lay(foot, plan, others, island.Rises[mass], slope);
            }
            if (!result.IsLaid) {
                island.Rises[mass] = 0.0;
                for (int edge = 0; edge < bareEdges.Length; edge++) bareEdges[edge] = true;
                result = Lay(foot, plan, others, 0.0, slope);
            }
            if (result.IsLaid) island.Commit(mass, result);
        }

        private static TalusApronResult Lay(LandmarkTalusFoot foot, IReadOnlyList<PlanPoint> plan, List<IReadOnlyList<PlanPoint>> others, double rise, double slope) {
            int count = plan.Count;
            List<IReadOnlyList<PlanPoint>> keepOffs = foot.Voids();
            var refused = new SortedSet<ulong>();
            Func<SlopedPrismPiece, bool> isClear = (piece) => Remember(refused, piece, IsClear(foot, piece));
            TalusApronResult result = TalusApron.Lay(plan, others, rise, slope, (edge) => foot.IsBare(plan[edge], plan[(edge + 1) % count]), keepOffs, isClear, Settings);
            lock (Recorder.Gate) Candidates.Add(Candidate(foot, plan, others, rise, slope, keepOffs, refused, result));
            return result;
        }

        private static bool Remember(SortedSet<ulong> refused, SlopedPrismPiece piece, bool isClear) {
            if (!isClear) refused.Add(GoldenHashes.PieceKey(piece));
            return isClear;
        }

        private static TalusCandidate Candidate(LandmarkTalusFoot foot, IReadOnlyList<PlanPoint> plan, List<IReadOnlyList<PlanPoint>> others, double rise, double slope,
                                                List<IReadOnlyList<PlanPoint>> keepOffs, SortedSet<ulong> refused, TalusApronResult result) {
            var bareFlags = new StringBuilder();
            for (int edge = 0; edge < plan.Count; edge++) bareFlags.Append(foot.IsBare(plan[edge], plan[(edge + 1) % plan.Count]) ? '1' : '0');
            int score = (result.IsLaid ? 0 : 16) + (refused.Count > 0 ? 4 : 0) + (result.Bare.Count > 0 ? 2 : 0) + (keepOffs.Count > 0 ? 2 : 0) + (others.Count > 0 ? 1 : 0)
                        + (result.Parts.Count == 0 ? 0 : 1);
            var text = new StringBuilder();
            text.Append($"new TalusCase({Recorder.Literal(rise)}, {Recorder.Literal(slope)}, \"{bareFlags}\",\n                {Recorder.Points(plan)},\n                ");
            text.Append($"{Recorder.Polygons(others)},\n                {Recorder.Polygons(keepOffs)},\n                new ulong[] {{ ");
            text.Append(string.Join(", ", refused.Select((key) => Recorder.Hex(key))));
            text.Append($" }},\n                {Recorder.Hex(GoldenHashes.Result(result))}, {result.Parts.Count}),");
            return new TalusCandidate(text.ToString(), score);
        }

        // The game's clear test: off the rails and stations, off voids and neighbours, and under no other mass's lowest seam.
        private static bool IsClear(LandmarkTalusFoot foot, SlopedPrismPiece slope) {
            if (!foot.Context.IsClear(slope.Plan, LandmarkSettings.TalusRailClearance)) return false;
            if (foot.Voids().Any((hole) => LandmarkPolygons.PolygonDistance(slope.Plan, hole) < NeighbourGap)) return false;
            if (foot.Neighbours.Any((neighbour) => LandmarkPolygons.PolygonDistance(slope.Plan, neighbour) < NeighbourGap)) return false;

            return !IsUnderSeam(foot, slope);
        }

        private static bool IsUnderSeam(LandmarkTalusFoot foot, SlopedPrismPiece slope) {
            for (int other = 0; other < foot.Feet.Count; other++) {
                if (other == foot.Mass || foot.Feet[other].Count < 3) continue;
                if (IsUnderSeam(foot.Feet[other], foot.FirstTops[other] - SeamClearance, slope)) return true;
            }
            return false;
        }

        private static bool IsUnderSeam(IReadOnlyList<PlanPoint> otherFoot, double limit, SlopedPrismPiece slope) {
            foreach (PlanPoint point in Samples(slope.Plan)) {
                if (slope.HeightAt(point.X, point.Z) > limit && ConvexPlan.Distance(otherFoot, point.X, point.Z) < SeamReach) return true;
            }
            return false;
        }

        private static IEnumerable<PlanPoint> Samples(IReadOnlyList<PlanPoint> plan) {
            for (int index = 0; index < plan.Count; index++) {
                PlanPoint start = plan[index], end = plan[(index + 1) % plan.Count];
                int steps = Math.Max(1, (int)Math.Ceiling(LandmarkPolygons.SegmentLength(start, end) / SampleStep));
                for (int step = 0; step < steps; step++) yield return new PlanPoint(start.X + (end.X - start.X) * step / steps, start.Z + (end.Z - start.Z) * step / steps);
            }
        }

        private static double Unreachable(IReadOnlyList<PlanPoint> foot, double rise, double slope, IReadOnlyList<LandmarkTalusTop> tops) {
            if (rise <= 0.0) return rise;

            double width = rise / slope;
            foreach (LandmarkTalusTop top in tops) {
                if (top.IsUnderPlumb) continue;

                double distance = LandmarkPolygons.PolygonDistance(foot, top.Plan);
                if (distance > width + ReachAcross) continue;

                rise = Math.Min(rise, top.Height - JumpReach - ReachMargin + slope * Math.Max(0.0, distance - ReachAcross));
            }
            return ConvexPlan.Round(Math.Max(0.0, rise));
        }

        private static bool IsSame(LandmarkTalusGame game, TalusIsland island, double slope) {
            if (!Recorder.Same(game.Slope, slope) || game.Rises.Count != island.Rises.Length || game.Edges.Count != island.Edges.Count) return false;
            if (game.Bare.Count != island.Bare.Count || game.Pieces.Count != island.Pieces.Count || game.Surfaces.Count != island.Surfaces.Count) return false;

            bool isSame = true;
            for (int index = 0; isSame && index < island.Rises.Length; index++) isSame = Recorder.Same(game.Rises[index], island.Rises[index]);
            for (int index = 0; isSame && index < island.Edges.Count; index++) isSame = IsSame(game.Edges[index], island.Edges[index]);
            for (int index = 0; isSame && index < island.Bare.Count; index++) isSame = IsSame(game.Bare[index], island.Bare[index]);
            for (int index = 0; isSame && index < island.Pieces.Count; index++) isSame = IsSame(game.Pieces[index], island.Pieces[index]);
            for (int index = 0; isSame && index < island.Surfaces.Count; index++) isSame = Recorder.Same(game.Surfaces[index], island.Surfaces[index]);
            return isSame;
        }

        private static bool IsSame(LandmarkTalusEdge one, LandmarkTalusEdge two) {
            return one.Mass == two.Mass && Recorder.Same(one.InnerStart, two.InnerStart) && Recorder.Same(one.InnerEnd, two.InnerEnd) && Recorder.Same(one.OuterStart, two.OuterStart)
                   && Recorder.Same(one.OuterEnd, two.OuterEnd) && Recorder.Same(one.Rise, two.Rise) && Recorder.Same(one.Width, two.Width) && one.IsCorner == two.IsCorner
                   && one.PieceStart == two.PieceStart && one.PieceCount == two.PieceCount;
        }

        private static bool IsSame(LandmarkBareFoot one, LandmarkBareFoot two) {
            return one.Mass == two.Mass && Recorder.Same(one.Start, two.Start) && Recorder.Same(one.End, two.End);
        }

        private static bool IsSame(LandmarkSlopePiece one, LandmarkSlopePiece two) {
            return one.Mass == two.Mass && Recorder.Same(one.Plan, two.Plan) && Recorder.Same(one.CentreX, two.CentreX) && Recorder.Same(one.CentreZ, two.CentreZ)
                   && Recorder.Same(one.TopY0, two.TopY0) && Recorder.Same(one.SlopeX, two.SlopeX) && Recorder.Same(one.SlopeZ, two.SlopeZ) && one.IsFan == two.IsFan;
        }
    }
}
