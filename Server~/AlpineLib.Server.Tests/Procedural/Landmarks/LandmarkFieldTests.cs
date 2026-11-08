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

        private static LandmarkField CreateField(ulong seed = Seed) {
            return new LandmarkField(seed, new[] { LandmarkTierSettings.Mesa, LandmarkTierSettings.Giant });
        }

        [Fact]
        public void SiteIsIndependentOfQueryOrderAndInstance() {
            var blocks = new List<CellCoord>();
            for (int y = -6; y < 6; y++) {
                for (int x = -6; x < 6; x++) {
                    blocks.Add(new CellCoord(x, y));
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
            LandmarkField field = CreateField();
            List<LandmarkSite> all = AllSitesNear(field, -30, 30);
            var result = new List<LandmarkSite>();
            for (int y = -20; y < 20; y++) {
                for (int x = -20; x < 20; x++) {
                    var cell = new CellCoord(x, y);
                    result.Clear();
                    field.SitesTouching(cell, reach, result);
                    List<LandmarkSite> expected = BruteForceTouching(field, all, cell, reach);
                    Assert.Equal(expected, result);
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
            for (int y = 0; y < 100; y++) {
                for (int x = 0; x < 100; x++) {
                    present += field.TryGetSite(tier, new CellCoord(x - 50, y - 50), out _) ? 1 : 0;
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
                Assert.Equal(Math.Round(site.OffsetX, 4), site.OffsetX);
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
            Assert.Equal(4, LandmarkTierSettings.Mesa.BlockCells);
            Assert.Equal(0, LandmarkTierSettings.Mesa.FootprintRadiusCells);
            Assert.Equal(8, LandmarkTierSettings.Giant.BlockCells);
            Assert.Equal(1, LandmarkTierSettings.Giant.FootprintRadiusCells);
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
            for (int y = low.Y; y <= high.Y; y++) {
                for (int x = low.X; x <= high.X; x++) {
                    if (field.TryGetSite(tier, new CellCoord(x, y), out LandmarkSite site)) {
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
