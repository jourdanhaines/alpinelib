using System.Collections.Generic;
using AllAboard.GoldenRecorder;
using AlpineLib.Procedural.Meshing;

namespace AllAboard.WorldGen {
    /// <summary>Facade over the game's fillets: returns the game's results, checking the rounding calls against the lib.</summary>
    public static class LandmarkFillets {
        public static List<PlanPoint> Round(IReadOnlyList<PlanPoint> plan, IReadOnlyList<PlanPoint> basePlan, ulong cornerSeed) {
            List<PlanPoint> game = LandmarkFilletsGame.Round(plan, basePlan, cornerSeed);
            FilletRecorder.CheckRound(plan, basePlan, cornerSeed, game);
            return game;
        }

        public static List<double> LookRadii(IReadOnlyList<PlanPoint> plan, IReadOnlyList<PlanPoint> basePlan, ulong cornerSeed, double width) {
            List<double> game = LandmarkFilletsGame.LookRadii(plan, basePlan, cornerSeed, width);
            FilletRecorder.CheckLookRadii(plan, basePlan, cornerSeed, width, game);
            return game;
        }

        public static double Width(IReadOnlyList<PlanPoint> plan) => LandmarkFilletsGame.Width(plan);

        public static List<LandmarkFaceKind> FaceKinds(IReadOnlyList<PlanPoint> plan) => LandmarkFilletsGame.FaceKinds(plan);

        public static double Unit(ulong hash) => LandmarkFilletsGame.Unit(hash);
    }
}
