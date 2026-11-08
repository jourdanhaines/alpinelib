using System;
using System.Collections.Generic;
using System.Linq;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Grid;
using AlpineLib.Procedural.Landmarks;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Landmarks {
    public sealed class LandmarkFieldTests {
        private const ulong Seed = 0x5EED_1A4D_0001UL;
        private const int MesaTier = 0;
        private const int GiantTier = 1;
        private static readonly long NegativeZeroBits = BitConverter.DoubleToInt64Bits(-0.0);

        private static LandmarkField CreateField(ulong seed = Seed) {
            return new LandmarkField(seed, new[] { LandmarkTierSettings.Mesa, LandmarkTierSettings.Giant });
        }

        [Fact]
        public void SiteIsIndependentOfQueryOrderAndInstance() {
            var blocks = new List<CellCoord>();
            for (int blockY = -6; blockY < 6; blockY++) {
                for (int blockX = -6; blockX < 6; blockX++) {
                    blocks.Add(new CellCoord(blockX, blockY));
                }
            }
            Dictionary<(int, CellCoord), LandmarkSite?> forward = CollectSites(CreateField(), blocks);
            new SplitMix64(7).Shuffle(blocks);
            Dictionary<(int, CellCoord), LandmarkSite?> shuffled = CollectSites(CreateField(), blocks);

            Assert.Equal(forward.Count, shuffled.Count);
            foreach (KeyValuePair<(int, CellCoord), LandmarkSite?> entry in forward) {
                Assert.Equal(entry.Value, shuffled[entry.Key]);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void SitesTouchingMatchesBruteForce(int reach) {
            AssertMatchesBruteForce(CreateField(), reach);
        }

        // Narrow blocks make the reach span several blocks per axis.
        [Theory]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void SitesTouchingMatchesBruteForceOnWideReach(int reach) {
            AssertMatchesBruteForce(CreateWideField(), reach);
        }

        [Fact]
        public void ReachZeroIsExactlyTheFootprint() {
            foreach (LandmarkField field in new[] { CreateField(), CreateWideField() }) {
                List<LandmarkSite> all = AllSitesNear(field, -30, 30);
                var result = new List<LandmarkSite>();
                for (int cellY = -20; cellY < 20; cellY++) {
                    for (int cellX = -20; cellX < 20; cellX++) {
                        var cell = new CellCoord(cellX, cellY);
                        result.Clear();
                        field.SitesTouching(cell, 0, result);
                        Assert.Equal(FootprintScan(field, all, cell), result);
                    }
                }
            }
        }

        [Fact]
        public void SitesTouchingAppendsAfterExistingEntries() {
            LandmarkField field = CreateField();
            var marker = new LandmarkSite(9, default, default, 0, 0, 0, default, 0);
            var result = new List<LandmarkSite> { marker };

            field.SitesTouching(new CellCoord(3, 3), 1, result);

            Assert.Equal(marker, result[0]);
        }

        [Fact]
        public void MesaAnchorsAreAtLeastTwoCellsApart() {
            List<LandmarkSite> mesas = AllSitesNear(CreateField(), -40, 40).Where((site) => site.Tier == MesaTier).ToList();
            Assert.True(mesas.Count > 100);
            for (int first = 0; first < mesas.Count; first++) {
                for (int second = first + 1; second < mesas.Count; second++) {
                    Assert.True(Chebyshev(mesas[first].Anchor, mesas[second].Anchor) >= 2);
                }
            }
        }

        [Fact]
        public void GiantFootprintsKeepTwoFreeCellsBetween() {
            LandmarkField field = CreateField();
            List<LandmarkSite> giants = AllSitesNear(field, -64, 64).Where((site) => site.Tier == GiantTier).ToList();
            Assert.True(giants.Count > 100);
            for (int first = 0; first < giants.Count; first++) {
                for (int second = first + 1; second < giants.Count; second++) {
                    // Radius 1 footprints with two free cells between them have anchors >= 1 + 2 + 1 + 1 apart.
                    Assert.True(Chebyshev(giants[first].Anchor, giants[second].Anchor) >= 5);
                }
            }
            Assert.Equal(9, field.FootprintCells(giants[0]).Count());
        }

        [Theory]
        [InlineData(MesaTier, 0.9)]
        [InlineData(GiantTier, 0.8)]
        public void PresenceRateMatchesSettings(int tier, double presence) {
            LandmarkField field = CreateField();
            int present = 0;
            for (int blockY = 0; blockY < 100; blockY++) {
                for (int blockX = 0; blockX < 100; blockX++) {
                    present += field.TryGetSite(tier, new CellCoord(blockX - 50, blockY - 50), out _) ? 1 : 0;
                }
            }
            Assert.InRange(present / 10000.0, presence - 0.03, presence + 0.03);
        }

        [Fact]
        public void OffsetsDrawsAndAnchorsStayInRange() {
            LandmarkField field = CreateField();
            foreach (LandmarkSite site in AllSitesNear(field, -40, 40)) {
                LandmarkTierSettings settings = field.Tier(site.Tier);
                Assert.InRange(site.OffsetX, -settings.SubCellJitter, settings.SubCellJitter);
                Assert.InRange(site.OffsetZ, -settings.SubCellJitter, settings.SubCellJitter);
                Assert.Equal(RoundAsField(site.OffsetX), site.OffsetX);
                Assert.Equal(RoundAsField(site.OffsetZ), site.OffsetZ);
                Assert.NotEqual(NegativeZeroBits, BitConverter.DoubleToInt64Bits(site.OffsetX));
                Assert.NotEqual(NegativeZeroBits, BitConverter.DoubleToInt64Bits(site.OffsetZ));
                for (int index = 0; index < LandmarkDraws.Count; index++) {
                    Assert.InRange(site.UDraws[index], 0.0, 0.999999);
                }
                Assert.Equal(site.Block, field.BlockOf(site.Tier, site.Anchor));
                Assert.All(field.FootprintCells(site), (cell) => Assert.Equal(site.Block, field.BlockOf(site.Tier, cell)));
                Assert.Equal(StableHash.Mix(new SplitMix64(Seed).Derive(settings.Name).Seed, Pack(site.Block)), site.Id);
            }
        }

        [Fact]
        public void MesaOffsetsUseTheJitterSpread() {
            List<LandmarkSite> mesas = AllSitesNear(CreateField(), -40, 40).Where((site) => site.Tier == MesaTier).ToList();
            Assert.Contains(mesas, (site) => site.OffsetX < -4.0);
            Assert.Contains(mesas, (site) => site.OffsetX > 4.0);
        }

        [Fact]
        public void RerunsAreBitIdentical() {
            Assert.Equal(Fingerprint(CreateField()), Fingerprint(CreateField()));
            Assert.NotEqual(Fingerprint(CreateField()), Fingerprint(CreateField(Seed + 1)));
        }

        [Fact]
        public void BlockOfFloorsNegativeCells() {
            LandmarkField field = CreateField();
            Assert.Equal(new CellCoord(-1, -1), field.BlockOf(MesaTier, new CellCoord(-1, -4)));
            Assert.Equal(new CellCoord(-2, 0), field.BlockOf(MesaTier, new CellCoord(-5, 3)));
            Assert.Equal(new CellCoord(0, -1), field.BlockOf(GiantTier, new CellCoord(7, -8)));
        }

        [Fact]
        public void PresetsAreValid() {
            AssertPreset(LandmarkTierSettings.Mesa, "landmark/mesa", 4, 0, 0.9, 0, 2, 8.0);
            AssertPreset(LandmarkTierSettings.Giant, "landmark/giant", 8, 1, 0.8, 2, 5, 0.0);
        }

        [Fact]
        public void BadSettingsThrow() {
            Assert.Throws<ArgumentException>(() => new LandmarkTierSettings("", 4, 0, 0.5, 0, 2, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LandmarkTierSettings("a", 1, 0, 0.5, 0, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LandmarkTierSettings("a", 4, 2, 0.5, 2, 2, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LandmarkTierSettings("a", 4, 0, 1.1, 0, 2, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LandmarkTierSettings("a", 4, 0, double.NaN, 0, 2, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LandmarkTierSettings("a", 4, 0, 0.5, 3, 2, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LandmarkTierSettings("a", 4, 0, 0.5, -1, 2, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LandmarkTierSettings("a", 4, 0, 0.5, 0, 4, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LandmarkTierSettings("a", 8, 1, 0.5, 0, 5, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LandmarkTierSettings("a", 8, 1, 0.5, 2, 7, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LandmarkTierSettings("a", 4, 0, 0.5, 0, 2, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LandmarkTierSettings("a", 4, 0, 0.5, 0, 2, double.PositiveInfinity));
        }

        [Fact]
        public void BadQueriesThrow() {
            LandmarkField field = CreateField();
            Assert.Throws<ArgumentOutOfRangeException>(() => field.TryGetSite(2, default, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => field.SitesTouching(default, -1, new List<LandmarkSite>()));
            Assert.Throws<ArgumentNullException>(() => new LandmarkField(Seed, null));
            var twin = new LandmarkTierSettings(LandmarkTierSettings.Mesa.Name, 8, 1, 0.5, 2, 5, 0);
            Assert.Throws<ArgumentException>(() => new LandmarkField(Seed, new[] { LandmarkTierSettings.Mesa, twin }));
        }

        private static LandmarkField CreateWideField() {
            var narrow = new LandmarkTierSettings("test/narrow", 2, 0, 0.5, 0, 1, 1.0);
            var ringed = new LandmarkTierSettings("test/ringed", 3, 1, 0.6, 1, 1, 0.0);
            return new LandmarkField(Seed, new[] { narrow, ringed });
        }

        private static void AssertMatchesBruteForce(LandmarkField field, int reach) {
            List<LandmarkSite> all = AllSitesNear(field, -30, 30);
            var result = new List<LandmarkSite>();
            for (int cellY = -20; cellY < 20; cellY++) {
                for (int cellX = -20; cellX < 20; cellX++) {
                    var cell = new CellCoord(cellX, cellY);
                    result.Clear();
                    field.SitesTouching(cell, reach, result);
                    Assert.Equal(BruteForceTouching(field, all, cell, reach), result);
                }
            }
        }

        private static List<LandmarkSite> FootprintScan(LandmarkField field, List<LandmarkSite> all, CellCoord cell) {
            return all
                .Where((site) => field.FootprintCells(site).Contains(cell))
                .OrderBy((site) => site.Tier)
                .ThenBy((site) => site.Id)
                .ToList();
        }

        private static void AssertPreset(LandmarkTierSettings preset, string name, int blockCells, int radius,
                                         double presence, int anchorMin, int anchorMax, double jitter) {
            Assert.Equal(name, preset.Name);
            Assert.Equal(blockCells, preset.BlockCells);
            Assert.Equal(radius, preset.FootprintRadiusCells);
            Assert.Equal(presence, preset.Presence);
            Assert.Equal(anchorMin, preset.AnchorMin);
            Assert.Equal(anchorMax, preset.AnchorMax);
            Assert.Equal(jitter, preset.SubCellJitter);
        }

        // Same formula as the field: round to 1e-4, then clear the sign of zero.
        private static double RoundAsField(double value) {
            return Math.Round(value * 10000.0) / 10000.0 + 0.0;
        }

        private static Dictionary<(int, CellCoord), LandmarkSite?> CollectSites(LandmarkField field, List<CellCoord> blocks) {
            var sites = new Dictionary<(int, CellCoord), LandmarkSite?>();
            foreach (CellCoord block in blocks) {
                for (int tier = 0; tier < field.TierCount; tier++) {
                    sites[(tier, block)] = field.TryGetSite(tier, block, out LandmarkSite site) ? site : (LandmarkSite?)null;
                }
            }
            return sites;
        }

        // Every site of every tier whose block intersects the cell window [min, max).
        private static List<LandmarkSite> AllSitesNear(LandmarkField field, int min, int max) {
            var sites = new List<LandmarkSite>();
            for (int tier = 0; tier < field.TierCount; tier++) {
                CellCoord low = field.BlockOf(tier, new CellCoord(min, min));
                CellCoord high = field.BlockOf(tier, new CellCoord(max - 1, max - 1));
                AppendBlockSites(field, tier, low, high, sites);
            }
            return sites;
        }

        private static void AppendBlockSites(LandmarkField field, int tier, CellCoord low, CellCoord high, List<LandmarkSite> into) {
            for (int blockY = low.Y; blockY <= high.Y; blockY++) {
                for (int blockX = low.X; blockX <= high.X; blockX++) {
                    if (field.TryGetSite(tier, new CellCoord(blockX, blockY), out LandmarkSite site)) {
                        into.Add(site);
                    }
                }
            }
        }

        private static List<LandmarkSite> BruteForceTouching(LandmarkField field, List<LandmarkSite> all, CellCoord cell, int reach) {
            return all
                .Where((site) => Chebyshev(site.Anchor, cell) <= field.Tier(site.Tier).FootprintRadiusCells + reach)
                .OrderBy((site) => site.Tier)
                .ThenBy((site) => site.Id)
                .ToList();
        }

        private static ulong Fingerprint(LandmarkField field) {
            ulong hash = StableHash.Fnv1a64OffsetBasis;
            foreach (LandmarkSite site in AllSitesNear(field, -32, 32)) {
                hash = StableHash.Mix(hash, site.Id);
                hash = StableHash.Mix(hash, Pack(site.Anchor));
                hash = StableHash.Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(site.OffsetX));
                hash = StableHash.Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(site.OffsetZ));
                hash = StableHash.Mix(hash, site.ShapeSeed);
                for (int index = 0; index < LandmarkDraws.Count; index++) {
                    hash = StableHash.Mix(hash, (ulong)BitConverter.DoubleToInt64Bits(site.UDraws[index]));
                }
            }
            return hash;
        }

        private static int Chebyshev(CellCoord left, CellCoord right) {
            return Math.Max(Math.Abs(left.X - right.X), Math.Abs(left.Y - right.Y));
        }

        private static ulong Pack(CellCoord coord) {
            return ((ulong)(uint)coord.X << 32) | (uint)coord.Y;
        }
    }
}
