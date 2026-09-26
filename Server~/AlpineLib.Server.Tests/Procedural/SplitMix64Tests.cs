using System;
using System.Collections.Generic;
using System.Linq;
using AlpineLib.Procedural;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural {
    public sealed class SplitMix64Tests {
        [Fact]
        public void SeedZeroProducesStandardGoldenVectors() {
            var rng = new SplitMix64(0);

            Assert.Equal(0xE220A8397B1DCDAFUL, rng.NextULong());
            Assert.Equal(0x6E789E6AA1B965F4UL, rng.NextULong());
            Assert.Equal(0x06C45D188009454FUL, rng.NextULong());
        }

        [Fact]
        public void SameSeedProducesSameSequence() {
            var first = new SplitMix64(12345);
            var second = new SplitMix64(12345);

            for (int index = 0; index < 100; index++) {
                Assert.Equal(first.NextULong(), second.NextULong());
            }
        }

        [Fact]
        public void SeedIsUnchangedByDrawing() {
            var rng = new SplitMix64(77);
            rng.NextULong();
            rng.NextInt(10);

            Assert.Equal(77UL, rng.Seed);
        }

        [Fact]
        public void DeriveDependsOnlyOnSeedAndLabel() {
            var fresh = new SplitMix64(42);
            var drawn = new SplitMix64(42);
            for (int index = 0; index < 17; index++) {
                drawn.NextULong();
            }

            SplitMix64 fromFresh = fresh.Derive("spine");
            SplitMix64 fromDrawn = drawn.Derive("spine");

            Assert.Equal(fromFresh.Seed, fromDrawn.Seed);
            Assert.Equal(fromFresh.NextULong(), fromDrawn.NextULong());
        }

        [Fact]
        public void DeriveSeparatesLabelsAndParents() {
            var rng = new SplitMix64(42);

            Assert.NotEqual(rng.Derive("spine").Seed, rng.Derive("wings").Seed);
            Assert.NotEqual(rng.Derive("spine").Seed, new SplitMix64(43).Derive("spine").Seed);
            Assert.NotEqual(rng.Seed, rng.Derive("spine").Seed);
        }

        [Fact]
        public void DerivedSeedIsPinned() {
            // Pins the Mix/FNV combination: changing it would silently regenerate every saved seed.
            var rng = new SplitMix64(0);

            ulong expected = StableHash.Mix(0, StableHash.Fnv1a64("spine"));

            Assert.Equal(expected, rng.Derive("spine").Seed);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(7)]
        [InlineData(int.MaxValue)]
        public void NextIntStaysInRange(int maxExclusive) {
            var rng = new SplitMix64(9);

            for (int index = 0; index < 10000; index++) {
                int value = rng.NextInt(maxExclusive);
                Assert.InRange(value, 0, maxExclusive - 1);
            }
        }

        [Fact]
        public void NextIntMinMaxStaysInRangeIncludingFullSpan() {
            var rng = new SplitMix64(9);

            for (int index = 0; index < 10000; index++) {
                Assert.InRange(rng.NextInt(-5, 5), -5, 4);
                Assert.InRange(rng.NextInt(int.MinValue, int.MaxValue), int.MinValue, int.MaxValue - 1);
            }
        }

        [Fact]
        public void NextIntRejectsEmptyRanges() {
            var rng = new SplitMix64(9);

            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(3, 3));
        }

        [Fact]
        public void NextIntIsRoughlyUniform() {
            var rng = new SplitMix64(2026);
            const int buckets = 6;
            const int draws = 60000;
            var counts = new int[buckets];

            for (int index = 0; index < draws; index++) {
                counts[rng.NextInt(buckets)]++;
            }

            // Expected 10000 each; 5 sigma is about ±456.
            foreach (int count in counts) {
                Assert.InRange(count, 9500, 10500);
            }
        }

        [Fact]
        public void NextIntHasNoModuloBiasOnLargeRange() {
            // Range 1.5·2^30: plain modulo of 32 bits would put 75% of draws below 2^30 instead of 2/3.
            var rng = new SplitMix64(5);
            const int range = 3 << 29;
            const int draws = 30000;
            int lowerPart = 0;

            for (int index = 0; index < draws; index++) {
                lowerPart += rng.NextInt(range) < (1 << 30) ? 1 : 0;
            }

            Assert.InRange(lowerPart, 19500, 20500);
        }

        [Fact]
        public void FloatsAndDoublesStayInUnitInterval() {
            var rng = new SplitMix64(3);

            for (int index = 0; index < 10000; index++) {
                float single = rng.NextFloat();
                double wide = rng.NextDouble();
                Assert.True(single >= 0f && single < 1f);
                Assert.True(wide >= 0.0 && wide < 1.0);
            }
        }

        [Fact]
        public void ChanceHonoursExtremesAndAlwaysDraws() {
            var rng = new SplitMix64(3);
            var twin = new SplitMix64(3);

            Assert.False(rng.Chance(0));
            Assert.True(rng.Chance(1));
            twin.NextDouble();
            twin.NextDouble();

            Assert.Equal(twin.NextULong(), rng.NextULong());
        }

        [Fact]
        public void ShuffleProducesPermutation() {
            var rng = new SplitMix64(11);
            var items = Enumerable.Range(0, 50).ToList();

            rng.Shuffle(items);

            Assert.Equal(Enumerable.Range(0, 50), items.OrderBy((item) => item));
            Assert.NotEqual(Enumerable.Range(0, 50), items);
        }

        [Fact]
        public void ShuffleIsDeterministic() {
            var first = Enumerable.Range(0, 20).ToList();
            var second = Enumerable.Range(0, 20).ToList();

            new SplitMix64(8).Shuffle(first);
            new SplitMix64(8).Shuffle(second);

            Assert.Equal(first, second);
        }

        [Fact]
        public void PickReturnsListMember() {
            var rng = new SplitMix64(1);
            var items = new[] { "a", "b", "c" };

            for (int index = 0; index < 100; index++) {
                Assert.Contains(rng.Pick(items), items);
            }
            Assert.Throws<ArgumentException>(() => rng.Pick(Array.Empty<string>()));
        }

        [Fact]
        public void PickWeightedFollowsWeightsAndSkipsZero() {
            var rng = new SplitMix64(4);
            var items = new[] { "never", "rare", "common" };
            var weights = new Dictionary<string, float> { ["never"] = 0f, ["rare"] = 1f, ["common"] = 3f };
            var counts = new Dictionary<string, int> { ["never"] = 0, ["rare"] = 0, ["common"] = 0 };

            for (int index = 0; index < 40000; index++) {
                counts[rng.PickWeighted(items, (item) => weights[item])]++;
            }

            Assert.Equal(0, counts["never"]);
            Assert.InRange(counts["rare"], 9500, 10500);
            Assert.InRange(counts["common"], 29500, 30500);
        }

        [Fact]
        public void PickWeightedRejectsNoPositiveWeight() {
            var rng = new SplitMix64(4);
            var items = new[] { "a", "b" };

            Assert.Throws<ArgumentException>(() => rng.PickWeighted(items, (item) => 0f));
            Assert.Throws<ArgumentException>(() => rng.PickWeighted(items, (item) => float.NaN));
        }
    }
}
