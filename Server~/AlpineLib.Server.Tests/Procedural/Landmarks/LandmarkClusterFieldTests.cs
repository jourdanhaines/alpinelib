using System;
using System.Collections.Generic;
using System.Linq;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Grid;
using AlpineLib.Procedural.Landmarks;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Landmarks {
    /// <summary>The cluster tiers: kind mix, member slots and sizes, bearings, multi degrade, completeness and order independence.</summary>
    public sealed class LandmarkClusterFieldTests {
        private const ulong Seed = 0x5EED_C1A5_0001UL;
        private const int ClusterTier = 0;
        private const int LonelyTier = 1;
        private const int Blocks = 30;

        private sealed class Everywhere : ILandmarkAnchorBias {
            public int Score(int tier, CellCoord block, CellCoord candidate, double preference) {
                return Math.Abs(candidate.X) + Math.Abs(candidate.Y);
            }
        }

        private sealed class Nowhere : ILandmarkAnchorBias {
            public int Score(int tier, CellCoord block, CellCoord candidate, double preference) {
                return int.MaxValue;
            }
        }

        private static LandmarkField CreateField(ILandmarkAnchorBias bias = null, double chance = 0.0) {
            return new LandmarkField(Seed, new[] { LandmarkTierSettings.Clusters.WithAttractChance(chance), LandmarkTierSettings.Lonely }, bias);
        }

        private static IEnumerable<CellCoord> AllBlocks() {
            for (int blockY = -Blocks; blockY < Blocks; blockY++) {
                for (int blockX = -Blocks; blockX < Blocks; blockX++) {
                    yield return new CellCoord(blockX, blockY);
                }
            }
        }

        private static List<LandmarkCluster> Clusters(LandmarkField field, int tier) {
            var clusters = new List<LandmarkCluster>();
            foreach (CellCoord block in AllBlocks()) {
                if (field.TryGetCluster(tier, block, out LandmarkCluster cluster)) {
                    clusters.Add(cluster);
                }
            }
            return clusters;
        }

        [Fact]
        public void PresetsAreValid() {
            LandmarkTierSettings clusters = LandmarkTierSettings.Clusters;
            Assert.Equal(("landmark/cluster", 20, 7, 0.9, 7, 12), (clusters.Name, clusters.BlockCells, clusters.FootprintRadiusCells, clusters.Presence, clusters.AnchorMin, clusters.AnchorMax));
            LandmarkTierSettings lonely = LandmarkTierSettings.Lonely;
            Assert.Equal(("landmark/lonely", 8, 2, 0.5, 2, 5), (lonely.Name, lonely.BlockCells, lonely.FootprintRadiusCells, lonely.Presence, lonely.AnchorMin, lonely.AnchorMax));
            Assert.Same(LandmarkClusterSettings.Clusters, clusters.WithAttractChance(0.5).Cluster);
            Assert.Null(LandmarkTierSettings.Giant.Cluster);
        }

        [Fact]
        public void KindsFollowTheSharesWhenTheBiasAlwaysFindsAnAnchor() {
            List<LandmarkCluster> clusters = Clusters(CreateField(new Everywhere()), ClusterTier);
            double lonely = clusters.Count((cluster) => cluster.Kind == LandmarkClusterKind.Lonely) / (double)clusters.Count;
            double group = clusters.Count((cluster) => cluster.Kind == LandmarkClusterKind.Group) / (double)clusters.Count;
            double multi = clusters.Count((cluster) => cluster.Kind == LandmarkClusterKind.Multi) / (double)clusters.Count;
            Assert.True(clusters.Count > 3000, $"{clusters.Count} clusters");
            Assert.InRange(lonely, 0.32, 0.38);
            Assert.InRange(group, 0.37, 0.43);
            Assert.InRange(multi, 0.22, 0.28);
            Assert.All(clusters.Where((cluster) => cluster.Kind == LandmarkClusterKind.Multi), (cluster) => Assert.True(cluster.IsAttracted || IsBest(cluster)));
        }

        [Fact]
        public void AMultiWithoutACandidateDegradesToAGroupAndKeepsItsDraws() {
            List<LandmarkCluster> found = Clusters(CreateField(new Everywhere()), ClusterTier);
            List<LandmarkCluster> lost = Clusters(CreateField(new Nowhere()), ClusterTier);
            Assert.Equal(found.Count, lost.Count);
            Assert.DoesNotContain(lost, (cluster) => cluster.Kind == LandmarkClusterKind.Multi);
            for (int index = 0; index < found.Count; index++) {
                if (found[index].Kind != LandmarkClusterKind.Multi) {
                    continue;
                }
                LandmarkCluster degraded = lost[index];
                Assert.Equal(LandmarkClusterKind.Group, degraded.Kind);
                Assert.Equal(found[index].Members.Count - 1, degraded.Members.Count);
                Assert.Equal(found[index].Members[0].ShapeSeed, degraded.Members[0].ShapeSeed);
                Assert.Equal(found[index].Members[2].Member.Across, degraded.Members[1].Member.Across);
                Assert.Equal(found[index].Members[2].Id, degraded.Members[1].Id);
            }
        }

        [Fact]
        public void MembersHoldTheirSlotsSizesAndBearings() {
            LandmarkClusterSettings rules = LandmarkClusterSettings.Clusters;
            var ids = new HashSet<ulong>();
            foreach (LandmarkCluster cluster in Clusters(CreateField(new Everywhere()), ClusterTier)) {
                int larges = cluster.Kind == LandmarkClusterKind.Multi ? 2 : 1;
                int smalls = cluster.Members.Count - larges;
                Assert.Equal(cluster.Kind == LandmarkClusterKind.Lonely ? 0 : 1, Math.Sign(smalls));
                if (cluster.Kind != LandmarkClusterKind.Lonely) {
                    Assert.InRange(smalls, rules.SmallCountMin, rules.SmallCountMax);
                }
                for (int index = 0; index < cluster.Members.Count; index++) {
                    LandmarkSite site = cluster.Members[index];
                    LandmarkMember member = site.Member;
                    Assert.True(ids.Add(site.Id));
                    Assert.Equal((cluster.Id, cluster.Kind, cluster.Members.Count, cluster.Anchor), (member.ClusterId, member.Kind, member.Count, site.Anchor));
                    Assert.Equal(index < larges ? index : LandmarkClusterSettings.FirstSmallSlot + index - larges, member.Slot);
                    Assert.InRange(member.BearingDegrees, 0, 359);
                    Assert.InRange(member.Ring, 0.0, 0.999999);
                    AssertSize(cluster.Kind, index < larges, member.Across, rules);
                    Assert.Equal(cluster.Kind == LandmarkClusterKind.Lonely ? member.Across >= rules.LargeMin : index < larges, member.IsLarge);
                }
            }
        }

        [Fact]
        public void SmallsSpreadRoundTheRingAndLonelySizesSkewSmall() {
            var lonelySizes = new List<double>();
            foreach (LandmarkCluster cluster in Clusters(CreateField(new Everywhere()), ClusterTier)) {
                if (cluster.Kind == LandmarkClusterKind.Lonely) {
                    lonelySizes.Add(cluster.Members[0].Member.Across);
                    continue;
                }
                List<int> bearings = cluster.Members.Where((site) => !site.Member.IsLarge).Select((site) => site.Member.BearingDegrees).OrderBy((bearing) => bearing).ToList();
                int widest = 360 - bearings[bearings.Count - 1] + bearings[0];
                for (int index = 1; index < bearings.Count; index++) {
                    widest = Math.Max(widest, bearings[index] - bearings[index - 1]);
                }
                Assert.True(widest <= 1.6 * 360.0 / bearings.Count + 1.0, $"gap {widest} for {bearings.Count} smalls");
            }
            double median = lonelySizes.OrderBy((size) => size).ElementAt(lonelySizes.Count / 2);
            Assert.InRange(median, 250.0, 320.0);
        }

        [Fact]
        public void LonelyTierHoldsOneLonelyMemberInItsRange() {
            List<LandmarkCluster> clusters = Clusters(CreateField(), LonelyTier);
            Assert.InRange(clusters.Count / (double)(4 * Blocks * Blocks), 0.46, 0.54);
            foreach (LandmarkCluster cluster in clusters) {
                Assert.Equal(LandmarkClusterKind.Lonely, cluster.Kind);
                LandmarkSite site = Assert.Single(cluster.Members);
                Assert.InRange(site.Member.Across, 180.0, 450.0);
                Assert.Equal((0, 0.0), (site.Member.BearingDegrees, site.Member.Ring));
                Assert.True(IsFirstMemberSite(cluster));
            }
        }

        [Fact]
        public void SitesTouchingAppendsEveryMemberOfEveryNearCluster() {
            LandmarkField field = CreateField(new Everywhere(), 0.5);
            var all = new List<LandmarkSite>();
            for (int tier = 0; tier < field.TierCount; tier++) {
                foreach (LandmarkCluster cluster in Clusters(field, tier)) {
                    all.AddRange(cluster.Members);
                }
            }
            var found = new List<LandmarkSite>();
            for (int cellY = -80; cellY < 80; cellY += 7) {
                for (int cellX = -80; cellX < 80; cellX += 7) {
                    found.Clear();
                    field.SitesTouching(new CellCoord(cellX, cellY), 1, found);
                    List<LandmarkSite> expected = all
                        .Where((site) => Math.Max(Math.Abs(site.Anchor.X - cellX), Math.Abs(site.Anchor.Y - cellY)) <= field.Tier(site.Tier).FootprintRadiusCells + 1)
                        .OrderBy((site) => site.Tier).ThenBy((site) => site.Id).ToList();
                    Assert.Equal(expected, found);
                }
            }
        }

        [Fact]
        public void ClustersAreIndependentOfQueryOrderAndInstance() {
            List<CellCoord> blocks = AllBlocks().ToList();
            Dictionary<CellCoord, LandmarkCluster> forward = Collect(CreateField(new Everywhere(), 0.5), blocks);
            new SplitMix64(11).Shuffle(blocks);
            Dictionary<CellCoord, LandmarkCluster> shuffled = Collect(CreateField(new Everywhere(), 0.5), blocks);
            Assert.Equal(forward.Count, shuffled.Count);
            foreach (KeyValuePair<CellCoord, LandmarkCluster> entry in forward) {
                Assert.Equal(entry.Value.Members, shuffled[entry.Key].Members);
                Assert.Equal((entry.Value.Kind, entry.Value.Anchor, entry.Value.IsAttracted), (shuffled[entry.Key].Kind, shuffled[entry.Key].Anchor, shuffled[entry.Key].IsAttracted));
            }
        }

        [Fact]
        public void ClassicTiersKeepTheirSitesAndComeBackAsOneMemberClusters() {
            var field = new LandmarkField(Seed, new[] { LandmarkTierSettings.Mesa, LandmarkTierSettings.Giant });
            foreach (CellCoord block in AllBlocks().Take(200)) {
                bool hasSite = field.TryGetSite(1, block, out LandmarkSite site);
                Assert.Equal(hasSite, field.TryGetCluster(1, block, out LandmarkCluster cluster));
                if (!hasSite) {
                    continue;
                }
                Assert.Equal(site, Assert.Single(cluster.Members));
                Assert.False(site.Member.IsClustered);
            }
        }

        [Fact]
        public void BadClusterSettingsThrow() {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LandmarkClusterSettings(0.7, 0.4, 1, 2, 1, 1, 2, 1, 1, 2, 1, 1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LandmarkClusterSettings(0.3, 0.3, 2, 1, 1, 1, 2, 1, 1, 2, 1, 1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LandmarkClusterSettings(0.3, 0.3, 1, 2, 1.3, 1, 2, 1, 1, 2, 1, 1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LandmarkClusterSettings(0.3, 0.3, 1, 2, 1, 1, 2, 1, 1, 2, 1, 3, 6));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LandmarkTierSettings("a", 20, -1, 0.5, 7, 12, 0, 0, null));
            Assert.Equal(180.0 + 420.0 * 0.25, LandmarkClusterSettings.Size(180.0, 600.0, 2.0, 0.5));
            Assert.Equal(Math.Round((180.0 + 120.0 * 0.25 * Math.Sqrt(0.25)) * 10000.0) / 10000.0, LandmarkClusterSettings.Size(180.0, 300.0, 1.5, 0.25));
        }

        private static bool IsFirstMemberSite(LandmarkCluster cluster) {
            LandmarkField field = CreateField();
            return field.TryGetSite(cluster.Tier, cluster.Block, out LandmarkSite site) && site.Equals(cluster.Members[0]);
        }

        // With the Everywhere bias an unmoved multi anchor must already be the best window cell.
        private static bool IsBest(LandmarkCluster cluster) {
            LandmarkTierSettings settings = LandmarkTierSettings.Clusters;
            int best = int.MaxValue;
            for (int offsetY = settings.AnchorMin; offsetY <= settings.AnchorMax; offsetY++) {
                for (int offsetX = settings.AnchorMin; offsetX <= settings.AnchorMax; offsetX++) {
                    best = Math.Min(best, Math.Abs(cluster.Block.X * settings.BlockCells + offsetX) + Math.Abs(cluster.Block.Y * settings.BlockCells + offsetY));
                }
            }
            return Math.Abs(cluster.Anchor.X) + Math.Abs(cluster.Anchor.Y) == best;
        }

        private static void AssertSize(LandmarkClusterKind kind, bool isLarge, double across, LandmarkClusterSettings rules) {
            if (kind == LandmarkClusterKind.Lonely) {
                Assert.InRange(across, rules.LonelyMin, rules.LonelyMax);
                return;
            }
            Assert.InRange(across, isLarge ? rules.LargeMin : rules.SmallMin, isLarge ? rules.LargeMax : rules.SmallMax);
        }

        private static Dictionary<CellCoord, LandmarkCluster> Collect(LandmarkField field, List<CellCoord> blocks) {
            var clusters = new Dictionary<CellCoord, LandmarkCluster>();
            foreach (CellCoord block in blocks) {
                if (field.TryGetCluster(ClusterTier, block, out LandmarkCluster cluster)) {
                    clusters[block] = cluster;
                }
            }
            return clusters;
        }
    }
}
