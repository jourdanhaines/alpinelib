using System;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Noise;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Noise {
    public sealed class FbmTests {
        [Fact]
        public void DefaultSettingsSampleFlatZero() {
            FbmSettings settings = default;

            Assert.Equal(0.0, Fbm.Sample1(1UL, 3.3, settings));
            Assert.Equal(0.0, Fbm.Sample2(1UL, 3.3, 4.4, settings));
        }

        [Fact]
        public void SingleOctaveIsGradientNoiseAtTheOctaveSeed() {
            FbmSettings settings = new FbmSettings(1, 0.5);

            Assert.Equal(GradientNoise.Sample2(Fbm.OctaveSeed(9UL, 0), 1.5, 2.0), Fbm.Sample2(9UL, 3.0, 4.0, settings));
            Assert.Equal(GradientNoise.Sample1(Fbm.OctaveSeed(9UL, 0), 1.5), Fbm.Sample1(9UL, 3.0, settings));
        }

        [Fact]
        public void SweepStaysInsideUnitRangeForAnyGain() {
            FbmSettings[] variants = { new FbmSettings(6, 0.1), new FbmSettings(3, 1.0, 3.0, 0.9), new FbmSettings(8, 0.05, 2.0, 1.5) };
            SplitMix64 random = new SplitMix64(4);
            foreach (FbmSettings settings in variants) {
                for (int sample = 0; sample < 20000; sample++) {
                    double x = random.NextDouble() * 2000.0 - 1000.0;
                    double y = random.NextDouble() * 2000.0 - 1000.0;
                    Assert.InRange(Fbm.Sample2(11UL, x, y, settings), -1.0, 1.0);
                    Assert.InRange(Fbm.Sample1(11UL, x, settings), -1.0, 1.0);
                }
            }
        }

        [Fact]
        public void ContinuousOverTime() {
            FbmSettings settings = new FbmSettings(3, 1.0 / 45.0);
            double previous = Fbm.Sample1(2UL, 0.0, settings);
            for (int step = 1; step <= 36000; step++) {
                double current = Fbm.Sample1(2UL, step * 0.1, settings);
                Assert.True(Math.Abs(current - previous) < 0.05);
                previous = current;
            }
        }

        [Fact]
        public void SettingsRejectBadValues() {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FbmSettings(0, 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FbmSettings(FbmSettings.MaxOctaves + 1, 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FbmSettings(2, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FbmSettings(2, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FbmSettings(2, 1.0, double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FbmSettings(2, 1.0, 2.0, -0.5));
        }

        [Fact]
        public void SettingsDefaultLacunarityAndGain() {
            FbmSettings settings = new FbmSettings(3, 0.25);

            Assert.Equal(2.0, settings.Lacunarity);
            Assert.Equal(0.5, settings.Gain);
            Assert.Equal(settings, new FbmSettings(3, 0.25, 2.0, 0.5));
        }
    }
}
