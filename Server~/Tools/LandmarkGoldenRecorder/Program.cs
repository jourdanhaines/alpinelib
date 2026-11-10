using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AllAboard.Server.Configuration;
using AllAboard.WorldGen;
using AlpineLib.Procedural.Grid;

namespace AllAboard.GoldenRecorder {
    /// <summary>Resolves every landmark round each seed's terminus (the facades check and sample them) and writes the cases.</summary>
    public static class Program {
        private const int TalusCases = 28;

        public static void Main(string[] args) {
            if (args.Length > 0 && args[0] == "seam") {
                SeamSearch.Run();
                return;
            }

            ulong[] seeds = args[0].Split(',').Select(ulong.Parse).ToArray();
            int cells = int.Parse(args[1]);
            string outDirectory = args[2];
            string config = Path.Combine(args[3], "Server", "AllAboard.Server", "config", "world");
            var kit = WorldTableLoader.LoadStationKit(Path.Combine(config, WorldTableLoader.StationKitFileName));
            var props = WorldTableLoader.LoadPropTable(Path.Combine(config, WorldTableLoader.PropTableFileName));
            var tiers = new SortedDictionary<string, int>();
            foreach (ulong seed in seeds) CountTiers(Resolve(new WorldGenerator(seed, Settings(), kit, props), cells), tiers);

            Console.WriteLine("TIERS " + string.Join(", ", tiers.Select((pair) => $"{pair.Key}={pair.Value}")));
            foreach (KeyValuePair<string, (int Ok, int Bad)> pair in Recorder.Counts) Console.WriteLine($"CHECK {pair.Key}: ok {pair.Value.Ok} bad {pair.Value.Bad}");
            Directory.CreateDirectory(outDirectory);
            File.WriteAllLines(Path.Combine(outDirectory, "fillet.txt"), Recorder.FilletCases);
            File.WriteAllLines(Path.Combine(outDirectory, "seat.txt"), Recorder.SeatCases);
            List<TalusCandidate> picked = PickTalus(TalusRecorder.Candidates, TalusCases);
            File.WriteAllLines(Path.Combine(outDirectory, "talus.txt"), picked.Select((candidate) => candidate.Text));
            Console.WriteLine($"CASES fillet {Recorder.FilletCases.Count} seat {Recorder.SeatCases.Count} talus {picked.Count} of {TalusRecorder.Candidates.Count}");
        }

        private static WorldGenSettings Settings() {
            var settings = new WorldGenSettings();
            settings.Landmarks.Enabled = true;
            return settings;
        }

        // Every standing landmark meeting the cells² window round the terminus.
        private static List<LandmarkPlan> Resolve(WorldGenerator generator, int cells) {
            CellCoord terminus = generator.Map.GetRouteCell(0).Coord;
            var plans = new Dictionary<ulong, LandmarkPlan>();
            for (int y = terminus.Y - cells / 2; y < terminus.Y + cells / 2; y += 4) {
                for (int x = terminus.X - cells / 2; x < terminus.X + cells / 2; x += 4) AddPlans(generator.LandmarksTouching(new CellCoord(x, y), 4), plans);
            }
            return plans.Values.ToList();
        }

        private static void AddPlans(List<LandmarkPlan> found, Dictionary<ulong, LandmarkPlan> plans) {
            foreach (LandmarkPlan plan in found) plans[plan.Site.Id] = plan;
        }

        private static void CountTiers(List<LandmarkPlan> plans, SortedDictionary<string, int> tiers) {
            foreach (LandmarkPlan plan in plans) {
                tiers.TryGetValue(plan.Tier.ToString(), out int count);
                tiers[plan.Tier.ToString()] = count + 1;
            }
        }

        // An even spread over the candidates' interest scores, the most interesting first.
        private static List<TalusCandidate> PickTalus(List<TalusCandidate> all, int cap) {
            var picked = new List<TalusCandidate>();
            int take = Math.Max(2, cap / 8);
            foreach (IGrouping<int, TalusCandidate> group in all.GroupBy((candidate) => candidate.Score).OrderByDescending((group) => group.Key)) {
                int stride = Math.Max(1, group.Count() / take);
                picked.AddRange(group.Where((candidate, index) => index % stride == 0).Take(Math.Min(take, cap - picked.Count)));
            }
            return picked;
        }
    }
}
