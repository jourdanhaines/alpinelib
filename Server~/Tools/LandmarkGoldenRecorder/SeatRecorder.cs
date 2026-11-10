using System;
using System.Collections.Generic;
using System.Text;
using AllAboard.WorldGen;
using AlpineLib.Procedural.Kits;
using AlpineLib.Procedural.Meshing;

namespace AllAboard.GoldenRecorder {
    /// <summary>The game's rock seating as a thin adapter over <see cref="LooseSeat"/>, checked against the game on every call.</summary>
    public static class SeatRecorder {
        private const double SeamReach = 0.05;
        private const int SampleEvery = 23, MaxCases = 32;
        private static readonly LooseSeatSettings Settings = new LooseSeatSettings(7, new[] { -0.6, -0.2, 0.2, 0.6 }, 0.15);
        private static int _seen;

        public static SlopedPrismPiece ToLib(LandmarkSlopePiece piece) {
            return new SlopedPrismPiece(piece.Plan, piece.CentreX, piece.CentreZ, piece.TopY0, piece.SlopeX, piece.SlopeZ, piece.IsFan);
        }

        public static void CheckSeated(PropRotation rotation, double x, double centreY, double z, double halfX, double halfY, double halfZ, IReadOnlyList<LandmarkSlopePiece> slopes,
                                       IReadOnlyList<LandmarkPrism> plinths, double game) {
            double reach = Math.Sqrt(halfX * halfX + halfY * halfY + halfZ * halfZ) + SeamReach;
            List<SlopedPrismPiece> nearSlopes = NearSlopes(slopes, x, z, reach);
            List<(IReadOnlyList<PlanPoint> Plan, double Top)> nearPlinths = NearPlinths(plinths, x, z, reach);
            Func<double, double, double> surface = (pointX, pointZ) => SurfaceOf(nearSlopes, nearPlinths, pointX, pointZ);
            Recorder.Check("seat.seated", Recorder.Same(LooseSeat.Seated(rotation.Lib, x, centreY, z, halfX, halfY, halfZ, surface, Settings), game));
            // Buried at the seated height on the near lists must equal the game's on the full lists.
            double buriedGame = LandmarkLooseSeatGame.Buried(rotation.Game, x, game, z, halfX, halfY, halfZ, slopes, plinths);
            Recorder.Check("seat.buriedNear", Recorder.Same(buriedGame, LooseSeat.Buried(rotation.Lib, x, game, z, halfX, halfY, halfZ, surface)));
            lock (Recorder.Gate) {
                _seen++;
                if (_seen % SampleEvery == 1 && Recorder.SeatCases.Count < MaxCases) Recorder.SeatCases.Add(CaseText(rotation, x, centreY, z, halfX, halfY, halfZ, nearSlopes, nearPlinths, game, buriedGame));
            }
        }

        public static void CheckBuried(PropRotation rotation, double x, double centreY, double z, double halfX, double halfY, double halfZ, IReadOnlyList<LandmarkSlopePiece> slopes,
                                       IReadOnlyList<LandmarkPrism> plinths, double game) {
            List<SlopedPrismPiece> libSlopes = NearSlopes(slopes, x, z, double.PositiveInfinity);
            List<(IReadOnlyList<PlanPoint> Plan, double Top)> libPlinths = NearPlinths(plinths, x, z, double.PositiveInfinity);
            double lib = LooseSeat.Buried(rotation.Lib, x, centreY, z, halfX, halfY, halfZ, (pointX, pointZ) => SurfaceOf(libSlopes, libPlinths, pointX, pointZ));
            Recorder.Check("seat.buried", Recorder.Same(lib, game));
        }

        // The game's walkable surface: the highest talus piece over (x, z) or within 5 cm, a plinth top, else the ground.
        private static double SurfaceOf(List<SlopedPrismPiece> slopes, List<(IReadOnlyList<PlanPoint> Plan, double Top)> plinths, double x, double z) {
            double surface = 0.0;
            foreach (SlopedPrismPiece slope in slopes) {
                if (ConvexPlan.Contains(slope.Plan, x, z, SeamReach)) surface = Math.Max(surface, slope.HeightAt(x, z));
            }
            if (plinths == null) return surface;

            foreach ((IReadOnlyList<PlanPoint> plan, double top) in plinths) {
                if (ConvexPlan.Contains(plan, x, z, SeamReach)) surface = Math.Max(surface, top);
            }
            return surface;
        }

        private static List<SlopedPrismPiece> NearSlopes(IReadOnlyList<LandmarkSlopePiece> slopes, double x, double z, double reach) {
            var near = new List<SlopedPrismPiece>();
            foreach (LandmarkSlopePiece slope in slopes) {
                if (ConvexPlan.Distance(slope.Plan, x, z) <= reach) near.Add(ToLib(slope));
            }
            return near;
        }

        private static List<(IReadOnlyList<PlanPoint> Plan, double Top)> NearPlinths(IReadOnlyList<LandmarkPrism> plinths, double x, double z, double reach) {
            if (plinths == null) return null;

            var near = new List<(IReadOnlyList<PlanPoint> Plan, double Top)>();
            foreach (LandmarkPrism prism in plinths) {
                if (ConvexPlan.Distance(prism.Plan, x, z) <= reach) near.Add((prism.Plan, prism.Top));
            }
            return near;
        }

        private static string CaseText(PropRotation rotation, double x, double centreY, double z, double halfX, double halfY, double halfZ, List<SlopedPrismPiece> slopes,
                                       List<(IReadOnlyList<PlanPoint> Plan, double Top)> plinths, double seated, double buried) {
            var text = new StringBuilder();
            text.Append($"new SeatCase({rotation.Yaw}, {rotation.Pitch}, {rotation.Roll}, {Recorder.Literal(x)}, {Recorder.Literal(centreY)}, {Recorder.Literal(z)}, ");
            text.Append($"{Recorder.Literal(halfX)}, {Recorder.Literal(halfY)}, {Recorder.Literal(halfZ)},\n                Slopes(");
            for (int index = 0; index < slopes.Count; index++) text.Append(index > 0 ? ",\n                    " : "").Append(SlopeText(slopes[index]));
            text.Append("),\n                ").Append(PlinthsText(plinths));
            text.Append($",\n                {Recorder.Literal(seated)}, {Recorder.Literal(buried)}),");
            return text.ToString();
        }

        private static string SlopeText(SlopedPrismPiece slope) {
            return $"S({Recorder.Points(slope.Plan)}, {Recorder.Literal(slope.CentreX)}, {Recorder.Literal(slope.CentreZ)}, {Recorder.Literal(slope.TopY0)}, "
                   + $"{Recorder.Literal(slope.SlopeX)}, {Recorder.Literal(slope.SlopeZ)}, {(slope.IsFan ? "true" : "false")})";
        }

        private static string PlinthsText(List<(IReadOnlyList<PlanPoint> Plan, double Top)> plinths) {
            if (plinths == null) return "null";

            var text = new StringBuilder("Plinths(");
            for (int index = 0; index < plinths.Count; index++) text.Append(index > 0 ? ", " : "").Append($"({Recorder.Points(plinths[index].Plan)}, {Recorder.Literal(plinths[index].Top)})");
            return text.Append(')').ToString();
        }
    }
}
