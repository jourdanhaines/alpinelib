using System;
using System.Collections.Generic;
using System.Text;
using AllAboard.WorldGen;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Meshing;

namespace AllAboard.GoldenRecorder {
    /// <summary>The game's band rounding as a thin adapter over <see cref="PolygonFillet"/>, checked against the game on every call.</summary>
    public static class FilletRecorder {
        private const double PlanRadiusMin = 2.0, PlanRadiusMax = 4.0;
        private const double LookRadiusMin = 6.0, LookRadiusMax = 30.0;
        private const double SharpShareMin = 0.2, SharpShareMax = 0.3;
        private const double FullRangeWidth = 120.0;
        private const ulong PlanSalt = 0x46494C50UL, LookSalt = 0x46494C4CUL, CountSalt = 0x46494C43UL, SharpSalt = 0x46494C53UL;
        private const int SampleEvery = 37, MaxCases = 24;
        private static readonly PolygonFilletSettings Settings = new PolygonFilletSettings(0.8660254037844386, 0.45);
        private static int _seen;

        public static void CheckRound(IReadOnlyList<PlanPoint> plan, IReadOnlyList<PlanPoint> basePlan, ulong cornerSeed, List<PlanPoint> game) {
            var corners = new List<FilletCorner>(plan.Count);
            foreach (PlanPoint at in plan) corners.Add(Corner(StableHash.Mix(cornerSeed, (ulong)NearestCorner(basePlan, at))));
            Recorder.Check("fillet.round", Recorder.Same(PolygonFillet.Round(plan, corners, Settings), game));
            lock (Recorder.Gate) {
                _seen++;
                if (_seen % SampleEvery == 1 && Recorder.FilletCases.Count < MaxCases) Recorder.FilletCases.Add(CaseText(plan, corners, game));
            }
        }

        public static void CheckLookRadii(IReadOnlyList<PlanPoint> plan, IReadOnlyList<PlanPoint> basePlan, ulong cornerSeed, double width, List<double> game) {
            double high = LookRadiusMin + (LookRadiusMax - LookRadiusMin) * Math.Min(1.0, width / FullRangeWidth);
            double sharpShare = SharpShareMin + (SharpShareMax - SharpShareMin) * LandmarkFilletsGame.Unit(StableHash.Mix(cornerSeed, SharpSalt));
            bool isSame = game.Count == plan.Count;
            for (int index = 0; isSame && index < plan.Count; index++) {
                ulong corner = StableHash.Mix(cornerSeed, (ulong)NearestCorner(basePlan, plan[index]));
                isSame = Recorder.Same(LookRadius(plan, index, corner, high, sharpShare), game[index]);
            }
            Recorder.Check("fillet.lookRadii", isSame);
        }

        private static FilletCorner Corner(ulong corner) {
            double radius = PlanRadiusMin + (PlanRadiusMax - PlanRadiusMin) * LandmarkFilletsGame.Unit(StableHash.Mix(corner, PlanSalt));
            return new FilletCorner(radius, !(LandmarkFilletsGame.Unit(StableHash.Mix(corner, CountSalt)) < 0.5));
        }

        private static double LookRadius(IReadOnlyList<PlanPoint> plan, int index, ulong corner, double high, double sharpShare) {
            if (LandmarkFilletsGame.Unit(StableHash.Mix(corner, LookSalt ^ 1UL)) < sharpShare) return 0.0;

            double radius = LookRadiusMin + (high - LookRadiusMin) * LandmarkFilletsGame.Unit(StableHash.Mix(corner, LookSalt));
            return ConvexPlan.Round(Math.Min(radius, PolygonFillet.MaxRadius(plan, index, 0.5)));
        }

        private static string CaseText(IReadOnlyList<PlanPoint> plan, List<FilletCorner> corners, List<PlanPoint> game) {
            ulong maxHash = GoldenHashes.Start;
            for (int index = 0; index < plan.Count; index++) maxHash = GoldenHashes.Add(maxHash, PolygonFillet.MaxRadius(plan, index, 0.5));
            var radii = new StringBuilder("R(");
            var midpoints = new StringBuilder();
            for (int index = 0; index < corners.Count; index++) {
                radii.Append(index > 0 ? ", " : "").Append(Recorder.Literal(corners[index].Radius));
                midpoints.Append(corners[index].HasMidpoint ? '1' : '0');
            }
            radii.Append(')');
            return $"new FilletCase({Recorder.Points(plan)}, {radii}, \"{midpoints}\", {Recorder.Hex(GoldenHashes.Add(GoldenHashes.Start, game))}, {Recorder.Hex(maxHash)}),";
        }

        private static int NearestCorner(IReadOnlyList<PlanPoint> basePlan, PlanPoint point) {
            int best = 0;
            double bestDistance = double.PositiveInfinity;
            for (int index = 0; index < basePlan.Count; index++) {
                double deltaX = basePlan[index].X - point.X, deltaZ = basePlan[index].Z - point.Z;
                double distance = deltaX * deltaX + deltaZ * deltaZ;
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = index;
            }
            return best;
        }
    }
}
