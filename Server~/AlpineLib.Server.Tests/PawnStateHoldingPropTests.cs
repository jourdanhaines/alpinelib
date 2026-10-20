using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Replication.Messages;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The holding-prop bit is presentation only, so nothing on the way reads it — every hop that
    /// rebuilds a flags byte is a place it can silently drop back to "empty-handed".
    /// </summary>
    public sealed class PawnStateHoldingPropTests {
        private const ushort PawnPrefab = 0;
        private const float TickInterval = 1f / 30f;
        private const int Capacity = 32;

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TheBitCrossesTheWire(bool holding) {
            Assert.Equal(holding, RoundTrip(Holding(holding)).HoldingProp);
        }

        [Fact]
        public void AnEmptyHandedStateIsByteIdenticalToTheOldLayout() {
            PawnState empty = Holding(false);

            Assert.Equal(PawnState.PackFlags(WireLocomotion.Walk, false, true), empty.Flags);
            Assert.Equal(0, empty.Flags & PawnState.HoldingPropBit);
        }

        [Fact]
        public void TheBitIsIndependentOfTheLocomotionReads() {
            PawnState empty = Holding(false);
            PawnState holding = Holding(true);

            Assert.Equal(empty.Locomotion, holding.Locomotion);
            Assert.Equal(empty.IsCrouching, holding.IsCrouching);
            Assert.Equal(empty.IsGrounded, holding.IsGrounded);
            Assert.True(holding.WithHoldingProp(false).Flags == empty.Flags);
        }

        [Fact]
        public void PickingSomethingUpAloneMarksTheStateChanged() {
            PawnState empty = Holding(false);
            PawnState holding = Holding(true);

            Assert.False(PawnState.ApproximatelyEquals(in empty, in holding));
        }

        [Fact]
        public void EveryTransformationOfAStateKeepsTheBit() {
            PawnState holding = Holding(true);

            Assert.True(holding.WithCarrier(3).HoldingProp);
            Assert.True(holding.WithOriginShift(new Vector3(10f, 0f, -5f)).HoldingProp);
            Assert.True(holding.WithCarrier(3).WithOriginShift(new Vector3(10f, 0f, -5f)).HoldingProp);
            Assert.True(holding.Quantized().HoldingProp);

            PawnState reflagged = holding.WithFlags(WireLocomotion.Sprint, true, false);
            Assert.True(reflagged.HoldingProp);
            Assert.Equal(WireLocomotion.Sprint, reflagged.Locomotion);
            Assert.True(reflagged.IsCrouching);
            Assert.False(reflagged.IsGrounded);
        }

        [Fact]
        public void TheMessagesThatCarryAPoseCarryTheBitToo() {
            PawnState holding = Holding(true).Quantized();

            Assert.True(RoundTripMessage(new SpawnEntity(1u, 0, 3, AuthorityMode.OwnerClient, in holding)).State.HoldingProp);
            Assert.True(RoundTripMessage(new OwnerPawnUpdate(1u, 5u, in holding)).State.HoldingProp);
            Assert.True(RoundTripMessage(new AuthorityCorrection(1u, 5u, 4u, in holding)).State.HoldingProp);

            var snapshot = new Snapshot(5u, new List<EntitySnapshotRecord> { new EntitySnapshotRecord(1u, in holding) });
            Assert.True(RoundTripMessage(snapshot).Records[0].State.HoldingProp);

            var keyframe = new SnapshotKeyframe(5u, new List<EntityKeyframeRecord> {
                new EntityKeyframeRecord(1u, 0, 3, AuthorityMode.OwnerClient, in holding)
            });
            Assert.True(RoundTripMessage(keyframe).Records[0].State.HoldingProp);
        }

        [Fact]
        public void AClampedMoveKeepsTheBit() {
            MovementValidator validator = BuildValidator();
            PawnState previous = Holding(false);
            PawnState next = Holding(true);
            next.Position = previous.Position + new Vector3(0f, 0f, AllowedStep() * 2f);

            MovementVerdict verdict = validator.Validate(PawnPrefab, in previous, in next, TickInterval);

            Assert.Equal(MovementVerdictKind.Clamped, verdict.Kind);
            Assert.True(verdict.ResolvedState.HoldingProp);
        }

        [Fact]
        public void ARejectedMoveReturnsThePreviousFlags() {
            MovementValidator validator = BuildValidator();
            PawnState previous = Holding(false);
            PawnState next = Holding(true);
            next.Position = previous.Position + new Vector3(0f, 0f, 400f);

            MovementVerdict verdict = validator.Validate(PawnPrefab, in previous, in next, TickInterval);

            Assert.Equal(MovementVerdictKind.Rejected, verdict.Kind);
            Assert.Equal(previous.Flags, verdict.ResolvedState.Flags);
        }

        [Theory]
        [InlineData(0.5f)]
        [InlineData(2f)]
        [InlineData(400f)]
        public void TheVerdictIgnoresTheBit(float distanceSteps) {
            MovementValidator validator = BuildValidator();
            float distance = distanceSteps > 100f ? distanceSteps : AllowedStep() * distanceSteps;

            MovementVerdict empty = Judge(validator, false, distance);
            MovementVerdict holding = Judge(validator, true, distance);

            Assert.Equal(empty.Kind, holding.Kind);
            Assert.Equal(empty.ResolvedState.Position, holding.ResolvedState.Position);
        }

        [Fact]
        public void TheInterpolatorTakesTheLaterSamplesBit() {
            var interpolator = new StateInterpolator(TickInterval, Capacity);
            PawnState empty = Holding(false);
            PawnState holding = Holding(true);

            interpolator.Push(10u, in empty);
            interpolator.Push(12u, in holding);

            Assert.True(interpolator.Sample(11.0 * TickInterval, out PawnState halfway));
            Assert.True(halfway.HoldingProp);
        }

        [Fact]
        public void TheMotorCarriesTheBitAcrossAStep() {
            PawnState holding = Holding(true);
            var input = new PawnInput(1u, new Vector2(0f, 1f), WireLocomotion.Jog, false, true);

            PawnState stepped = PawnMotor.Step(in input, in holding, BuildProfile(), CollisionWorld.Flat(), 1u, TickInterval);

            Assert.True(stepped.HoldingProp);
            Assert.Equal(WireLocomotion.Jog, stepped.Locomotion);
            Assert.True(stepped.IsCrouching);
        }

        [Fact]
        public void PredictionNeverCorrectsOverTheBit() {
            Assert.Equal(0, ClientReplication.CorrectionFlagsMask & PawnState.HoldingPropBit);
        }

        [Fact]
        public void TheProtocolVersionMovedForTheBit() {
            Assert.True(NetProtocol.Version >= 9);
            Assert.NotEqual(NetProtocol.Fnv1a(8).ToString("x8"), NetProtocol.BuildConnectKey("game").Split('/')[1]);
        }

        private static MovementVerdict Judge(MovementValidator validator, bool holding, float distance) {
            PawnState previous = Holding(holding);
            PawnState next = Holding(holding);
            next.Position = previous.Position + new Vector3(0f, 0f, distance);
            return validator.Validate(PawnPrefab, in previous, in next, TickInterval);
        }

        private static float AllowedStep() {
            return 2f * 1.5f * TickInterval + MovementValidator.PositionSlackMetres;
        }

        private static PawnState Holding(bool holding) {
            var state = new PawnState(
                new Vector3(1f, 0f, 2f),
                45f,
                Vector3.Zero,
                PawnState.PackFlags(WireLocomotion.Walk, false, true));
            return state.WithHoldingProp(holding);
        }

        private static MovementProfile BuildProfile() {
            return new MovementProfile {
                DisplayName = "pawn",
                WalkSlowSpeed = 0.8f,
                WalkSpeed = 2f,
                JogSpeed = 3.5f,
                SprintSpeed = 5.5f,
                CrouchSpeed = 1.2f,
                CrouchFastSpeed = 2.2f
            };
        }

        private static MovementValidator BuildValidator() {
            var config = new NetConfig {
                MovementToleranceMultiplier = 1.5f,
                MovementProfiles = new[] { BuildProfile() }
            };

            return new MovementValidator(config);
        }

        private static TMessage RoundTripMessage<TMessage>(TMessage message) where TMessage : INetMessage, new() {
            var buffer = new byte[512];
            var writer = new NetWriter(buffer);
            message.Serialize(ref writer);

            var reader = new NetReader(buffer, 0, writer.Written);
            var decoded = new TMessage();
            decoded.Deserialize(ref reader);

            Assert.Equal(0, reader.Remaining);
            return decoded;
        }

        private static PawnState RoundTrip(in PawnState state) {
            var buffer = new byte[64];
            var writer = new NetWriter(buffer);
            state.Serialize(ref writer);

            var reader = new NetReader(buffer, 0, writer.Written);
            PawnState decoded = default;
            decoded.Deserialize(ref reader);

            Assert.Equal(0, reader.Remaining);
            return decoded;
        }
    }
}
