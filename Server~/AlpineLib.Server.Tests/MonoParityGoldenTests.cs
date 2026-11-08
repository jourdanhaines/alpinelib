using AlpineLib.Netcode.Diagnostics;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Pins <see cref="MonoParityGoldens"/> on CoreCLR, both its published constants and the hashes. The game's Unity
    /// gate compares the editor's Mono against the same constants, so a change here changes what clients compute.
    /// </summary>
    public sealed class MonoParityGoldenTests {
        [Fact]
        public void TheShapeGoldenHolds() {
            Assert.Equal(0x2A398B624CD6B11DUL, MonoParityGoldens.ShapeGolden);
            Assert.Equal(MonoParityGoldens.ShapeGolden, MonoParityGoldens.ShapeHash());
        }

        [Fact]
        public void TheMotorGoldenHolds() {
            Assert.Equal(0xEE6E28DCD15A17E5UL, MonoParityGoldens.MotorGolden);
            Assert.Equal(MonoParityGoldens.MotorGolden, MonoParityGoldens.MotorHash());
        }

        [Fact]
        public void ThePrismGoldenHolds() {
            Assert.Equal(0xE7F2B71C41C1F340UL, MonoParityGoldens.PrismGolden);
            Assert.Equal(MonoParityGoldens.PrismGolden, MonoParityGoldens.PrismHash());
        }
    }
}
