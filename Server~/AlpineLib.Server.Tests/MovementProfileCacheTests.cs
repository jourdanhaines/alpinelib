using System;
using AlpineLib.Netcode.Protocol;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>The profile's cached slope threshold and air drag decay, against the float libm calls they replaced.</summary>
    public sealed class MovementProfileCacheTests {
        [Theory]
        [InlineData(45f)]
        [InlineData(50f)]
        public void TheAuthoredSlopeLimitsKeepTheirThresholdBits(float degrees) {
            var profile = new MovementProfile { SlopeLimitDegrees = degrees };
            float previous = MathF.Cos((float)(degrees * (MathF.PI / 180f)));

            Assert.Equal(BitConverter.SingleToInt32Bits(previous), BitConverter.SingleToInt32Bits(profile.WalkableNormalY));
        }

        [Fact]
        public void TheDefaultThresholdIsTheDefaultLimitsCosine() {
            var profile = new MovementProfile();

            Assert.Equal((float)Math.Cos(50.0 * Math.PI / 180.0), profile.WalkableNormalY);
        }

        [Fact]
        public void TheGoldenAirDragKeepsItsDecayBits() {
            var profile = new MovementProfile { AirDrag = 0.5f };
            float previous = MathF.Exp((float)(-0.5f * (1f / 30f)));

            Assert.Equal(BitConverter.SingleToInt32Bits(previous), BitConverter.SingleToInt32Bits(profile.GetAirDragDecay(1f / 30f)));
        }

        [Fact]
        public void TheDecayFollowsTheDragAndTheStepLength() {
            var profile = new MovementProfile { AirDrag = 0.5f };
            float atThirty = profile.GetAirDragDecay(1f / 30f);
            float atSixty = profile.GetAirDragDecay(1f / 60f);
            profile.AirDrag = 2f;

            Assert.Equal((float)Math.Exp(-0.5 * (1f / 30f)), atThirty);
            Assert.Equal((float)Math.Exp(-0.5 * (1f / 60f)), atSixty);
            Assert.Equal((float)Math.Exp(-2.0 * (1f / 30f)), profile.GetAirDragDecay(1f / 30f));
            Assert.Equal(1f, new MovementProfile().GetAirDragDecay(1f / 30f));
        }
    }
}
