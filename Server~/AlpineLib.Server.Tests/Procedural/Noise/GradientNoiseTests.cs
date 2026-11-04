using System;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Noise;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Noise {
    public sealed class GradientNoiseTests {
        private const ulong Seed = 77UL;
        private const int SweepCount = 200000;

        [Fact]
        public void ZeroOnLatticePoints() {
            for (int coordinate = -5; coordinate <= 5; coordinate++) {
                Assert.Equal(0.0, GradientNoise.Sample1(Seed, coordinate));
                Assert.Equal(0.0, GradientNoise.Sample2(Seed, coordinate, -coordinate * 3));
                Assert.Equal(0.0, GradientNoise.Sample3(Seed, coordinate, 2 * coordinate, -coordinate));
            }
        }

        [Fact]
        public void SweepStaysInsideUnitRangeAndUsesIt() {
            SplitMix64 random = new SplitMix64(1);
            double max1 = 0.0;
            double max2 = 0.0;
            double max3 = 0.0;
            for (int sample = 0; sample < SweepCount; sample++) {
                double x = random.NextDouble() * 400.0 - 200.0;
                double y = random.NextDouble() * 400.0 - 200.0;
                double z = random.NextDouble() * 400.0 - 200.0;
                max1 = Math.Max(max1, Math.Abs(GradientNoise.Sample1(Seed, x)));
                max2 = Math.Max(max2, Math.Abs(GradientNoise.Sample2(Seed, x, y)));
                max3 = Math.Max(max3, Math.Abs(GradientNoise.Sample3(Seed, x, y, z)));
            }
            // Strictly inside: the clamp is a safety net, never the shape.
            Assert.InRange(max1, 0.6, 0.999999);
            Assert.InRange(max2, 0.6, 0.999999);
            Assert.InRange(max3, 0.6, 0.999999);
        }

        [Fact]
        public void ContinuousAcrossLatticeLines() {
            const double step = 1e-6;
            for (int line = -20; line <= 20; line++) {
                double before = line - step;
                double after = line + step;
                Assert.True(Math.Abs(GradientNoise.Sample1(Seed, after) - GradientNoise.Sample1(Seed, before)) < 1e-4);
                Assert.True(Math.Abs(GradientNoise.Sample2(Seed, after, 0.37) - GradientNoise.Sample2(Seed, before, 0.37)) < 1e-4);
                Assert.True(Math.Abs(GradientNoise.Sample2(Seed, 0.61, after) - GradientNoise.Sample2(Seed, 0.61, before)) < 1e-4);
                Assert.True(Math.Abs(GradientNoise.Sample3(Seed, 0.2, 0.7, after) - GradientNoise.Sample3(Seed, 0.2, 0.7, before)) < 1e-4);
                Assert.True(Math.Abs(GradientNoise.Sample3(Seed, after, 0.7, 0.4) - GradientNoise.Sample3(Seed, before, 0.7, 0.4)) < 1e-4);
            }
        }

        [Fact]
        public void SmallStepsGiveSmallChanges() {
            SplitMix64 random = new SplitMix64(9);
            const double step = 1e-3;
            for (int sample = 0; sample < 5000; sample++) {
                double x = random.NextDouble() * 100.0 - 50.0;
                double y = random.NextDouble() * 100.0 - 50.0;
                double delta = Math.Abs(GradientNoise.Sample2(Seed, x + step, y) - GradientNoise.Sample2(Seed, x, y));
                Assert.True(delta < 0.01, $"|Δ| {delta} at ({x}, {y})");
            }
        }

        [Fact]
        public void SeedsAreIndependent() {
            SplitMix64 random = new SplitMix64(3);
            double sumA = 0.0;
            double sumB = 0.0;
            double sumAB = 0.0;
            double sumAA = 0.0;
            double sumBB = 0.0;
            const int count = 20000;
            for (int sample = 0; sample < count; sample++) {
                double x = random.NextDouble() * 500.0;
                double y = random.NextDouble() * 500.0;
                double valueA = GradientNoise.Sample2(1UL, x, y);
                double valueB = GradientNoise.Sample2(2UL, x, y);
                sumA += valueA;
                sumB += valueB;
                sumAB += valueA * valueB;
                sumAA += valueA * valueA;
                sumBB += valueB * valueB;
            }
            double covariance = sumAB / count - sumA / count * (sumB / count);
            double varianceA = sumAA / count - sumA / count * (sumA / count);
            double varianceB = sumBB / count - sumB / count * (sumB / count);
            double correlation = covariance / Math.Sqrt(varianceA * varianceB);
            Assert.InRange(correlation, -0.05, 0.05);
        }

        [Fact]
        public void SameInputsSameOutput() {
            Assert.Equal(GradientNoise.Sample3(5UL, 1.25, -3.5, 8.75), GradientNoise.Sample3(5UL, 1.25, -3.5, 8.75));
            Assert.NotEqual(GradientNoise.Sample3(5UL, 1.25, -3.5, 8.75), GradientNoise.Sample3(6UL, 1.25, -3.5, 8.75));
        }

        [Fact]
        public void FadeHitsEndpoints() {
            Assert.Equal(0.0, GradientNoise.Fade(0.0));
            Assert.Equal(1.0, GradientNoise.Fade(1.0));
            Assert.Equal(0.5, GradientNoise.Fade(0.5));
        }
    }
}
