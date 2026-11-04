using System;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Noise;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Noise {
    public sealed class FbmTests {
        [Fact]
        public void DefaultSettingsAreRejected() {
            FbmSettings settings = default;

            Assert.Throws<ArgumentException>(() => Fbm.Sample1(1UL, 3.3, settings));
            Assert.Throws<ArgumentException>(() => Fbm.Sample2(1UL, 3.3, 4.4, settings));
        }

        [Fact]
        public void SingleOctaveIsOffsetGradientNoiseAtTheOctaveSeed() {
            FbmSettings settings = new FbmSettings(1, 0.5);
            ulong octaveSeed = Fbm.OctaveSeed(9UL, 0);
            double offsetX = Fbm.OctaveOffset(9UL, 0, 0);
            double offsetY = Fbm.OctaveOffset(9UL, 0, 1);

            Assert.Equal(GradientNoise.Sample2(octaveSeed, 1.5 + offsetX, 2.0 + offsetY), Fbm.Sample2(9UL, 3.0, 4.0, settings));
            Assert.Equal(GradientNoise.Sample1(octaveSeed, 1.5 + offsetX), Fbm.Sample1(9UL, 3.0, settings));
            Assert.NotEqual(offsetX, offsetY);
        }

        [Fact]
        public void NotPinnedToZeroOnTheFrequencyGrid() {
            FbmSettings ground = new FbmSettings(3, 1.0 / 18.0);
            FbmSettings wind = new FbmSettings(2, 1.0 / 600.0);
            int zeros = 0;
            for (int cellX = -10; cellX <= 10; cellX++) {
                for (int cellY = -10; cellY <= 10; cellY++) {
                    zeros += Fbm.Sample2(42UL, 18.0 * cellX, 18.0 * cellY, ground) == 0.0 ? 1 : 0;
                }
                zeros += Fbm.Sample1(9UL, 600.0 * cellX, wind) == 0.0 ? 1 : 0;
            }
            Assert.Equal(0, zeros);
        }

        [Fact]
        public void OctaveSeedsAreSaltedAwayFromLatticeHashes() {
            for (int octave = 0; octave < FbmSettings.MaxOctaves; octave++) {
                Assert.NotEqual(LatticeHash.Hash(7UL, octave), Fbm.OctaveSeed(7UL, octave));
                Assert.NotEqual(AlpineLib.Procedural.StableHash.Mix(7UL, (ulong)octave), Fbm.OctaveSeed(7UL, octave));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => Fbm.OctaveOffset(7UL, 0, 2));
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
