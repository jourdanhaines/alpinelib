using System;
using System.Collections.Generic;
using System.Text;
using AlpineLib.Procedural.Meshing;

namespace AllAboard.GoldenRecorder {
    /// <summary>Random small feet until one meets a seam step (real islands never have): the source of the seam-step test.</summary>
    public static class SeamSearch {
        private const int Attempts = 200000;
        private const double Slope = 0.6075;

        public static void Run() {
            var random = new Random(5);
            for (int attempt = 0; attempt < Attempts; attempt++) {
                if (TryAttempt(random, attempt)) return;
            }
            Console.WriteLine("none found");
        }

        private static bool TryAttempt(Random random, int attempt) {
            var none = new List<IReadOnlyList<PlanPoint>>();
            var points = new List<PlanPoint>();
            int count = random.Next(3, 7);
            for (int index = 0; index < count * 3; index++) points.Add(new PlanPoint(Math.Round(random.NextDouble() * 30.0, 1), Math.Round(random.NextDouble() * 12.0, 1)));
            List<PlanPoint> foot = ConvexPlan.Hull(points);
            if (foot.Count < 3) return false;

            double rise = Math.Round(2.0 + random.NextDouble() * 14.0, 1);
            int bareMask = random.Next(0, 1 << foot.Count);
            TalusApronResult result = TalusApron.Lay(foot, none, rise, Slope, (edge) => ((bareMask >> edge) & 1) == 1, none, (piece) => true, TalusRecorder.Settings);
            if (result.IsLaid) return false;

            var flags = new StringBuilder();
            for (int edge = 0; edge < foot.Count; edge++) flags.Append(((bareMask >> edge) & 1) == 1 ? '1' : '0');
            Console.WriteLine($"UNLAID attempt {attempt}: rise {rise} slope {Slope} bare {flags} foot {Recorder.Points(foot)}");
            return true;
        }
    }
}
