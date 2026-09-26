using System;
using System.Collections.Generic;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Rules;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural {
    public sealed class LayoutAttemptsTests {
        [Fact]
        public void AttemptsDrawFromDerivedStreams() {
            var rng = new SplitMix64(99);
            var seenSeeds = new List<ulong>();
            var rules = new ConstraintSet<ulong>().Add("never", (value, violations) => violations.Add("no"));

            bool built = LayoutAttempts.TryBuild(rng, 3, (attemptRng) => Record(attemptRng, seenSeeds), rules, out ulong result, out ConstraintReport report);

            Assert.False(built);
            Assert.Equal(new[] {
                rng.Derive("attempt/0").Seed, rng.Derive("attempt/1").Seed, rng.Derive("attempt/2").Seed,
            }, seenSeeds);
            Assert.Equal(seenSeeds[2], result);
            Assert.Equal(new[] { "never: no" }, report.Violations);
        }

        [Fact]
        public void StopsAtFirstSatisfyingAttempt() {
            var rng = new SplitMix64(1);
            int calls = 0;
            var rules = new ConstraintSet<int>().Add("atLeastThree", (value, violations) => AddIfBelow(value, 3, violations));

            bool built = LayoutAttempts.TryBuild(rng, 10, (attemptRng) => ++calls, rules, out int result, out ConstraintReport report);

            Assert.True(built);
            Assert.Equal(3, result);
            Assert.Equal(3, calls);
            Assert.True(report.IsSatisfied);
        }

        [Fact]
        public void OutcomeIgnoresPriorDrawsOnParent() {
            var fresh = new SplitMix64(7);
            var drawn = new SplitMix64(7);
            drawn.NextULong();
            var rules = new ConstraintSet<ulong>();

            LayoutAttempts.TryBuild(fresh, 1, (attemptRng) => attemptRng.NextULong(), rules, out ulong first, out _);
            LayoutAttempts.TryBuild(drawn, 1, (attemptRng) => attemptRng.NextULong(), rules, out ulong second, out _);

            Assert.Equal(first, second);
        }

        [Fact]
        public void RejectsZeroAttempts() {
            var rules = new ConstraintSet<int>();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => LayoutAttempts.TryBuild(new SplitMix64(0), 0, (attemptRng) => 0, rules, out _, out _));
        }

        private static ulong Record(SplitMix64 attemptRng, List<ulong> seenSeeds) {
            seenSeeds.Add(attemptRng.Seed);
            return attemptRng.Seed;
        }

        private static void AddIfBelow(int value, int minimum, List<string> violations) {
            if (value >= minimum) {
                return;
            }
            violations.Add($"{value} < {minimum}");
        }
    }
}
