using System;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Diagnostics;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Pins <see cref="MonoParityGoldens"/> on CoreCLR, both its published constants and the hashes, and proves its
    /// inputs reach the paths it exists to cover. The game's Unity gate compares the editor's Mono to the same constants.
    /// </summary>
    public sealed class MonoParityGoldenTests {
        private const int ZeroLengthCapsuleIndex = 7;
        private const int CollapsedBoxIndex = 8;

        [Fact]
        public void TheExactGoldensHold() {
            Assert.Equal(0x226E3ADFDE4DB867UL, MonoParityGoldens.ShapeGolden);
            Assert.Equal(0xD2D03DA2234E2E54UL, MonoParityGoldens.MotorGolden);
            Assert.Equal(0xE7F2B71C41C1F340UL, MonoParityGoldens.PrismGolden);
            Assert.Equal(MonoParityGoldens.ShapeGolden, MonoParityGoldens.ShapeHash());
            Assert.Equal(MonoParityGoldens.MotorGolden, MonoParityGoldens.MotorHash());
            Assert.Equal(MonoParityGoldens.PrismGolden, MonoParityGoldens.PrismHash());
        }

        [Fact]
        public void TheRoughGoldensHold() {
            Assert.Equal(0x03A44F2656D55DCEUL, MonoParityGoldens.RoughShapeGolden);
            Assert.Equal(0x86C3844F8949D43CUL, MonoParityGoldens.RoughMotorGolden);
            Assert.Equal(0xA52A7C80C478586EUL, MonoParityGoldens.RoughPrismGolden);
            Assert.Equal(MonoParityGoldens.RoughShapeGolden, MonoParityGoldens.RoughShapeHash());
            Assert.Equal(MonoParityGoldens.RoughMotorGolden, MonoParityGoldens.RoughMotorHash());
            Assert.Equal(MonoParityGoldens.RoughPrismGolden, MonoParityGoldens.RoughPrismHash());
        }

        [Fact]
        public void TheSlopedGoldensHold() {
            Assert.Equal(0xE31BF47AA4E52B4AUL, MonoParityGoldens.SlopedPrismGolden);
            Assert.Equal(0xA683802B1DAD9316UL, MonoParityGoldens.SlopedMotorGolden);
            Assert.Equal(0x9CD5A79E4776C326UL, MonoParityGoldens.RoughSlopedPrismGolden);
            Assert.Equal(0x79D582EE7873843DUL, MonoParityGoldens.RoughSlopedMotorGolden);
            Assert.Equal(MonoParityGoldens.SlopedPrismGolden, MonoParityGoldens.SlopedPrismHash());
            Assert.Equal(MonoParityGoldens.SlopedMotorGolden, MonoParityGoldens.SlopedMotorHash());
            Assert.Equal(MonoParityGoldens.RoughSlopedPrismGolden, MonoParityGoldens.RoughSlopedPrismHash());
            Assert.Equal(MonoParityGoldens.RoughSlopedMotorGolden, MonoParityGoldens.RoughSlopedMotorHash());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TheSlopedRunGroundsOnAndTouchesSlopedPrisms(bool rough) {
            var coverage = new MotorParityCoverage();
            MonoParityGoldens.SlopedMotorHash(rough, coverage);

            Assert.True(coverage.SlopedPrismGrounded > 200, "Grounded " + coverage.SlopedPrismGrounded);
            Assert.True(coverage.SlopedPrismTouches > 50, "Touches " + coverage.SlopedPrismTouches);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TheSlopedPosesReachTheTopFaceRimAndInterior(bool rough) {
            CollisionShape[] shapes = MonoParityGoldens.SlopedPrisms();
            int slanted = 0, sideways = 0, supports = 0;
            for (int index = 0; index < 4096; index++) {
                CapsulePose pose = rough ? MonoParityGoldens.RoughPose(index) : MonoParityGoldens.Pose(index);
                foreach (CollisionShape shape in shapes) {
                    if (CollisionResolver.TryGetSupport(in shape, pose.FootPosition.X, pose.FootPosition.Z, 5f, -5f, out _, out _)) supports++;
                    if (!CollisionResolver.TryGetContact(in pose, in shape, out CollisionContact contact)) continue;

                    if (contact.Normal.Y > 0.05f && contact.Normal.Y < 0.999f) slanted++;
                    if (contact.Normal.Y == 0f) sideways++;
                }
            }

            Assert.True(slanted > 100, "Slanted " + slanted);
            Assert.True(sideways > 100, "Sideways " + sideways);
            Assert.True(supports > 1000, "Supports " + supports);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TheMotorRunReachesEveryCoveredPath(bool rough) {
            var coverage = new MotorParityCoverage();
            MonoParityGoldens.MotorHash(rough, coverage);

            Assert.True(coverage.AirborneWithoutInput > 0);
            Assert.True(coverage.OverLengthInputs > 0);
            Assert.True(coverage.HeldYaws > 0);
            Assert.True(coverage.PrismTouches > 0);
            Assert.True(coverage.RampTouches > 0);
            Assert.True(coverage.OriginShifts > 0);
            Assert.True(coverage.LoopWraps > 0);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TheShapePassReachesDegenerateShapesWithFiniteOutputs(bool rough) {
            CollisionShape[] shapes = MonoParityGoldens.Shapes();
            int zeroLengthContacts = 0;
            int collapsedBoxContacts = 0;
            for (int index = 0; index < 4096; index++) {
                CapsulePose pose = rough ? MonoParityGoldens.RoughPose(index) : MonoParityGoldens.Pose(index);
                zeroLengthContacts += CheckFinite(in pose, in shapes[ZeroLengthCapsuleIndex]);
                collapsedBoxContacts += CheckFinite(in pose, in shapes[CollapsedBoxIndex]);
                // The collapsed box has no top face: its support probe passes through without an entry face.
                Assert.False(CollisionResolver.TryGetSupport(in shapes[CollapsedBoxIndex], pose.FootPosition.X, pose.FootPosition.Z, 5f, -5f, out _, out _));
            }

            Assert.True(zeroLengthContacts > 0);
            Assert.True(collapsedBoxContacts > 0);
        }

        private static int CheckFinite(in CapsulePose pose, in CollisionShape shape) {
            if (!CollisionResolver.TryGetContact(in pose, in shape, out CollisionContact contact)) return 0;

            Assert.True(float.IsFinite(contact.Normal.X) && float.IsFinite(contact.Normal.Y) && float.IsFinite(contact.Normal.Z));
            Assert.True(float.IsFinite(contact.Depth));
            return 1;
        }
    }
}
