using System;
using AlpineLib.Procedural;
using AlpineLib.Procedural.Noise;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural.Noise {
    public sealed class DomainWarpTests {
        private static readonly FbmSettings WarpSettings = new FbmSettings(2, 1.0 / 60.0);

        [Fact]
        public void OffsetNeverExceedsAmplitude() {
            SplitMix64 random = new SplitMix64(5);
            for (int sample = 0; sample < 20000; sample++) {
                double originalX = random.NextDouble() * 1000.0 - 500.0;
                double originalY = random.NextDouble() * 1000.0 - 500.0;
                double x = originalX;
                double y = originalY;
                DomainWarp.Warp2(3UL, ref x, ref y, 6.0, WarpSettings);
                Assert.True(Math.Abs(x - originalX) <= 6.0);
                Assert.True(Math.Abs(y - originalY) <= 6.0);
            }
        }

        [Fact]
        public void ZeroAmplitudeLeavesThePoint() {
            double x = 12.5;
            double y = -3.25;
            DomainWarp.Warp2(3UL, ref x, ref y, 0.0, WarpSettings);

            Assert.Equal(12.5, x);
            Assert.Equal(-3.25, y);
        }

        [Fact]
        public void AxesAreIndependent() {
            double x = 40.0;
            double y = 40.0;
            DomainWarp.Warp2(8UL, ref x, ref y, 6.0, WarpSettings);

            Assert.NotEqual(x - 40.0, y - 40.0);
        }
    }
}
