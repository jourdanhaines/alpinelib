using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Pins the narrow-phase and motor goldens of <see cref="MonoParityGoldens"/> on CoreCLR. The same hashes must come
    /// out of the Unity editor's Mono (its gate re-runs them), so any change here is a wire-visible behaviour change.
    /// </summary>
    public sealed class MonoParityGoldenTests {
        /// <summary>Box, sphere, capsule and plane contacts and supports over the golden poses.</summary>
        public const ulong ShapeGolden = 0x2A398B624CD6B11DUL;

        /// <summary>Pawn motor states over the golden script and world.</summary>
        public const ulong MotorGolden = 0xEE6E28DCD15A17E5UL;

        [Fact]
        public void TheShapeGoldenHolds() {
            Assert.Equal(ShapeGolden, MonoParityGoldens.ShapeHash());
        }

        [Fact]
        public void TheMotorGoldenHolds() {
            Assert.Equal(MotorGolden, MonoParityGoldens.MotorHash());
        }
    }
}
