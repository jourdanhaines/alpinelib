using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Replication.Messages;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The look pitch rides the pawn state for one purpose — a remote body bending its neck — and nothing
    /// on the way reads it, which is exactly how a field gets dropped: every place that rebuilds a state
    /// by hand is a place it can silently fall back to level.
    /// </summary>
    public sealed class PawnStateLookPitchTests {
        private const ushort PawnPrefab = 0;
        private const float TickInterval = 1f / 30f;
        private const int Capacity = 32;

        [Theory]
        [InlineData(0f)]
        [InlineData(37.5f)]
        [InlineData(-62.25f)]
        [InlineData(85f)]
        [InlineData(-90f)]
        public void APitchCrossesTheWireToWithinHalfAStep(float pitchDegrees) {
            PawnState decoded = RoundTrip(Looking(pitchDegrees));

            Assert.InRange(decoded.LookPitchDegrees, pitchDegrees - NetQuantization.PitchToleranceDegrees, pitchDegrees + NetQuantization.PitchToleranceDegrees);
        }

        [Fact]
        public void ALevelGazeIsExact() {
            Assert.Equal(0f, RoundTrip(Looking(0f)).LookPitchDegrees);
        }

        [Fact]
        public void APitchPastVerticalIsClampedRatherThanWrapped() {
            Assert.Equal(90f, RoundTrip(Looking(140f)).LookPitchDegrees, 3);
            Assert.Equal(-90f, RoundTrip(Looking(-140f)).LookPitchDegrees, 3);
        }

        [Fact]
        public void EveryTransformationOfAStateKeepsItsPitch() {
            PawnState state = Looking(40f);

            Assert.Equal(40f, state.WithCarrier(3).LookPitchDegrees);
            Assert.Equal(40f, state.WithFlags(WireLocomotion.Sprint, true, false).LookPitchDegrees);
            Assert.Equal(NetQuantization.QuantizePitch(40f), state.Quantized().LookPitchDegrees);
        }

        [Fact]
        public void AQuantizedStateMatchesWhatComesOffTheWire() {
            PawnState state = Looking(33.3f);
            PawnState decoded = RoundTrip(in state);
            PawnState quantized = state.Quantized();

            Assert.True(PawnState.ApproximatelyEquals(in quantized, in decoded));
        }

        [Fact]
        public void TurningTheHeadAloneMarksTheStateChanged() {
            PawnState level = Looking(0f);
            PawnState down = Looking(10f);

            Assert.False(PawnState.ApproximatelyEquals(in level, in down));
            Assert.True(PawnState.ApproximatelyEquals(in down, in down));
        }

        [Fact]
        public void TheMessagesThatCarryAPoseCarryItsPitchToo() {
            PawnState looking = Looking(-45f).Quantized();

            Assert.Equal(looking.LookPitchDegrees, RoundTripMessage(new SpawnEntity(1u, 0, 3, AuthorityMode.OwnerClient, in looking)).State.LookPitchDegrees);
            Assert.Equal(looking.LookPitchDegrees, RoundTripMessage(new OwnerPawnUpdate(1u, 5u, in looking)).State.LookPitchDegrees);
            Assert.Equal(looking.LookPitchDegrees, RoundTripMessage(new AuthorityCorrection(1u, 5u, 4u, in looking)).State.LookPitchDegrees);

            var snapshot = new Snapshot(5u, new List<EntitySnapshotRecord> { new EntitySnapshotRecord(1u, in looking) });

            Assert.Equal(looking.LookPitchDegrees, RoundTripMessage(snapshot).Records[0].State.LookPitchDegrees);
        }

        [Fact]
        public void AClampedMoveKeepsWhereTheOwnerWasLooking() {
            MovementValidator validator = BuildValidator();
            float allowed = 2f * 1.5f * TickInterval + MovementValidator.PositionSlackMetres;
            PawnState previous = Looking(0f);
            PawnState next = Looking(55f);
            next.Position = previous.Position + new Vector3(0f, 0f, allowed * 2f);

            MovementVerdict verdict = validator.Validate(PawnPrefab, in previous, in next, TickInterval);

            Assert.Equal(MovementVerdictKind.Clamped, verdict.Kind);
            Assert.Equal(55f, verdict.ResolvedState.LookPitchDegrees);
        }

        [Fact]
        public void APitchIsBlendedAcrossASpanAndHeldPastItsEnd() {
            var interpolator = new StateInterpolator(TickInterval, Capacity);
            PawnState level = Looking(0f);
            PawnState down = Looking(60f);

            interpolator.Push(10u, in level);
            interpolator.Push(12u, in down);

            Assert.True(interpolator.Sample(11.0 * TickInterval, out PawnState halfway));
            Assert.Equal(30f, halfway.LookPitchDegrees, 3);

            Assert.True(interpolator.Sample(12.5 * TickInterval, out PawnState beyond));
            Assert.Equal(60f, beyond.LookPitchDegrees, 3);
        }

        private static PawnState Looking(float pitchDegrees) {
            return new PawnState(
                new Vector3(1f, 0f, 2f),
                45f,
                Vector3.Zero,
                PawnState.PackFlags(WireLocomotion.Walk, false, true),
                PawnState.WorldCarrierId,
                pitchDegrees);
        }

        private static MovementValidator BuildValidator() {
            var config = new NetConfig {
                MovementToleranceMultiplier = 1.5f,
                MovementProfiles = new[] {
                    new MovementProfile {
                        DisplayName = "pawn",
                        WalkSlowSpeed = 0.8f,
                        WalkSpeed = 2f,
                        JogSpeed = 3.5f,
                        SprintSpeed = 5.5f,
                        CrouchSpeed = 1.2f,
                        CrouchFastSpeed = 2.2f
                    }
                }
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
