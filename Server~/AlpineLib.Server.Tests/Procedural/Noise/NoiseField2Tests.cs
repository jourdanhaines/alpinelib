using AlpineLib.Procedural;
using AlpineLib.Procedural.Noise;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Noise {
    public sealed class NoiseField2Tests {
        [Fact]
        public void WithoutWarpItIsFbm() {
            FbmSettings settings = new FbmSettings(3, 1.0 / 18.0);
            NoiseField2 field = new NoiseField2(21UL, settings);

            Assert.Equal(Fbm.Sample2(21UL, 7.5, -2.25, settings), field.Sample(7.5, -2.25));
        }

        [Fact]
        public void WarpChangesTheField() {
            FbmSettings settings = new FbmSettings(3, 1.0 / 18.0);
            NoiseField2 plain = new NoiseField2(21UL, settings);
            NoiseField2 warped = new NoiseField2(21UL, settings, 6.0, new FbmSettings(2, 1.0 / 60.0));

            Assert.NotEqual(plain.Sample(7.5, -2.25), warped.Sample(7.5, -2.25));
        }

        [Fact]
        public void Sample01StaysInUnitInterval() {
            NoiseField2 field = new NoiseField2(4UL, new FbmSettings(4, 0.2), 3.0, new FbmSettings(2, 0.05));
            SplitMix64 random = new SplitMix64(6);
            for (int sample = 0; sample < 10000; sample++) {
                double x = random.NextDouble() * 300.0;
                double y = random.NextDouble() * 300.0;
                Assert.InRange(field.Sample01(x, y), 0.0, 1.0);
            }
        }
    }
}
