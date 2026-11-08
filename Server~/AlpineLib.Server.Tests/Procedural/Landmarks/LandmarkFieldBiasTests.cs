using System;
using System.Collections.Generic;
using AlpineLib.Procedural.Grid;
using AlpineLib.Procedural.Landmarks;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Landmarks {
    /// <summary>The optional anchor bias: inert at chance 0, snaps attracted anchors to the best window cell, never leaves the window.</summary>
    public sealed class LandmarkFieldBiasTests {
        private const ulong Seed = 0x5EED_B1A5_0001UL;
        private const int Blocks = 12;

        // Prefers cells on the diagonal x == y (score = |x − y|); cells with x odd are no candidates.
        private sealed class DiagonalBias : ILandmarkAnchorBias {
            public int Score(int tier, CellCoord block, CellCoord candidate, double preference) {
                if ((candidate.X & 1) != 0) {
                    return int.MaxValue;
                }
                return Math.Abs(candidate.X - candidate.Y);
            }
        }

        private sealed class NoCandidates : ILandmarkAnchorBias {
            public int Score(int tier, CellCoord block, CellCoord candidate, double preference) {
                return int.MaxValue;
            }
        }

        private static IReadOnlyList<LandmarkTierSettings> Tiers(double chance) {
            return new[] { LandmarkTierSettings.Mesa.WithAttractChance(chance), LandmarkTierSettings.Giant.WithAttractChance(chance) };
        }

        private static IEnumerable<(int Tier, CellCoord Block)> AllBlocks() {
            for (int tier = 0; tier < 2; tier++) {
                for (int blockY = -Blocks; blockY < Blocks; blockY++) {
                    for (int blockX = -Blocks; blockX < Blocks; blockX++) {
                        yield return (tier, new CellCoord(blockX, blockY));
                    }
                }
            }
        }

        [Fact]
        public void ChanceZeroOrNoBiasLeavesEverySiteAsBefore() {
            var plain = new LandmarkField(Seed, new[] { LandmarkTierSettings.Mesa, LandmarkTierSettings.Giant });
            var zero = new LandmarkField(Seed, Tiers(0.0), new DiagonalBias());
            var unbiased = new LandmarkField(Seed, Tiers(1.0), null);
            foreach ((int tier, CellCoord block) in AllBlocks()) {
                bool has = plain.TryGetSite(tier, block, out LandmarkSite expected);
                Assert.Equal(has, zero.TryGetSite(tier, block, out LandmarkSite fromZero));
                Assert.Equal(has, unbiased.TryGetSite(tier, block, out LandmarkSite fromUnbiased));
                if (!has) {
                    continue;
                }
                Assert.Equal(expected, fromZero);
                Assert.Equal(expected, fromUnbiased);
            }
        }

        [Fact]
        public void AttractedAnchorsTakeTheBestWindowCellAndKeepEveryOtherValue() {
            var plain = new LandmarkField(Seed, new[] { LandmarkTierSettings.Mesa, LandmarkTierSettings.Giant });
            var biased = new LandmarkField(Seed, Tiers(1.0), new DiagonalBias());
            int moved = 0;
            foreach ((int tier, CellCoord block) in AllBlocks()) {
                if (!plain.TryGetSite(tier, block, out LandmarkSite expected)) {
                    continue;
                }
                Assert.True(biased.TryGetSite(tier, block, out LandmarkSite site));
                Assert.Equal(Best(biased.Tier(tier), block), site.Anchor);
                Assert.Equal(!site.Anchor.Equals(expected.Anchor), site.IsAttracted);
                Assert.Equal((expected.Id, expected.ShapeSeed, expected.UDraws, expected.OffsetX, expected.OffsetZ), (site.Id, site.ShapeSeed, site.UDraws, site.OffsetX, site.OffsetZ));
                moved += site.IsAttracted ? 1 : 0;
            }
            Assert.True(moved > 0);
        }

        [Fact]
        public void AWindowWithoutCandidatesKeepsTheDrawnAnchor() {
            var plain = new LandmarkField(Seed, new[] { LandmarkTierSettings.Mesa, LandmarkTierSettings.Giant });
            var biased = new LandmarkField(Seed, Tiers(1.0), new NoCandidates());
            foreach ((int tier, CellCoord block) in AllBlocks()) {
                if (!plain.TryGetSite(tier, block, out LandmarkSite expected)) {
                    continue;
                }
                Assert.True(biased.TryGetSite(tier, block, out LandmarkSite site));
                Assert.Equal(expected.Anchor, site.Anchor);
                Assert.False(site.IsAttracted);
            }
        }

        [Fact]
        public void SitesTouchingStaysCompleteWithTheBias() {
            var field = new LandmarkField(Seed, Tiers(0.5), new DiagonalBias());
            var all = new List<LandmarkSite>();
            foreach ((int tier, CellCoord block) in AllBlocks()) {
                if (field.TryGetSite(tier, block, out LandmarkSite site)) {
                    all.Add(site);
                }
            }
            var found = new List<LandmarkSite>();
            for (int cellY = -40; cellY < 40; cellY += 3) {
                for (int cellX = -40; cellX < 40; cellX += 3) {
                    found.Clear();
                    var cell = new CellCoord(cellX, cellY);
                    field.SitesTouching(cell, 1, found);
                    foreach (LandmarkSite site in all) {
                        int reach = field.Tier(site.Tier).FootprintRadiusCells + 1;
                        bool near = Math.Max(Math.Abs(site.Anchor.X - cellX), Math.Abs(site.Anchor.Y - cellY)) <= reach;
                        Assert.Equal(near, found.Contains(site));
                    }
                }
            }
        }

        [Fact]
        public void AttractChanceMustBeAProbability() {
            Assert.Throws<ArgumentOutOfRangeException>(() => LandmarkTierSettings.Mesa.WithAttractChance(-0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => LandmarkTierSettings.Mesa.WithAttractChance(1.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => LandmarkTierSettings.Mesa.WithAttractChance(double.NaN));
            Assert.Equal(0.0, LandmarkTierSettings.Giant.AttractChance);
        }

        // Lowest |x − y| among even-x cells of the window, row-major first; the drawn anchor when none qualifies is not possible here (windows hold even columns).
        private static CellCoord Best(LandmarkTierSettings settings, CellCoord block) {
            int best = int.MaxValue;
            CellCoord chosen = default;
            for (int offsetY = settings.AnchorMin; offsetY <= settings.AnchorMax; offsetY++) {
                for (int offsetX = settings.AnchorMin; offsetX <= settings.AnchorMax; offsetX++) {
                    var candidate = new CellCoord(block.X * settings.BlockCells + offsetX, block.Y * settings.BlockCells + offsetY);
                    int score = (candidate.X & 1) != 0 ? int.MaxValue : Math.Abs(candidate.X - candidate.Y);
                    if (score < best) {
                        best = score;
                        chosen = candidate;
                    }
                }
            }
            return chosen;
        }
    }
}
