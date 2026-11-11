using AlpineLib.Netcode.Diagnostics;
using Xunit;

namespace AlpineLib.Server.Tests.Motor {
    /// <summary>
    /// Pins <see cref="MonoParityGoldens.CornerMotorHash()"/> on CoreCLR and proves its script reaches the corners the
    /// one-contact motor is left inside, so a Mono run that matches it has exercised the joint corner resolution.
    /// </summary>
    public sealed class PawnMotorCornerParityTests {
        [Fact]
        public void TheCornerGoldenHolds() {
            Assert.Equal(0x7B8A18580091B449UL, MonoParityGoldens.CornerMotorGolden);
            Assert.Equal(MonoParityGoldens.CornerMotorGolden, MonoParityGoldens.CornerMotorHash());
        }

        [Fact]
        public void TheCornerScriptLeavesTheOneContactMotorInsideAndTheJointOneOutside() {
            ulong joint = MonoParityGoldens.CornerMotorHash(true, out float jointResidual);
            ulong single = MonoParityGoldens.CornerMotorHash(false, out float singleResidual);

            Assert.NotEqual(single, joint);
            Assert.True(singleResidual > 0.05f, $"The one-contact motor is only left {singleResidual:F4} m inside.");
            Assert.True(jointResidual <= 0.01f, $"The joint motor is left {jointResidual:F4} m inside.");
        }
    }
}
