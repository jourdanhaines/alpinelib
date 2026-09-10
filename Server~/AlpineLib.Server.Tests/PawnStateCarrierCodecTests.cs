using System.Numerics;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The carrier id is a wire field with no schema behind it, so the only thing keeping the writer and
    /// the reader in agreement is a round trip. These also pin the two derived promises the rest of the
    /// carrier work rests on: that the frame survives every state transformation, and that dirty tracking
    /// never mistakes two frames for one.
    /// </summary>
    public sealed class PawnStateCarrierCodecTests {
        /// <summary>Position, quantized yaw, quantized velocity, flags and the carrier id.</summary>
        private const int CarrierStateWireBytes = 23;

        [Fact]
        public void AWorldStateStillRoundTripsAndReportsTheWorldFrame() {
            var original = new PawnState(
                new Vector3(4.5f, 1.25f, -8.75f),
                90f,
                new Vector3(1f, 0f, -2f),
                PawnState.PackFlags(WireLocomotion.Walk, false, true));

            PawnState decoded = RoundTrip(in original);

            Assert.Equal(PawnState.WorldCarrierId, decoded.CarrierId);
            Assert.False(decoded.IsCarrierRelative);
            Assert.Equal(original.Position, decoded.Position);
        }

        [Fact]
        public void ACarrierRelativeStateKeepsItsCarrierAcrossTheWire() {
            var original = new PawnState(
                new Vector3(-1.5f, 0.25f, 3.125f),
                271.5f,
                new Vector3(0.5f, 0f, 1.25f),
                PawnState.PackFlags(WireLocomotion.Jog, false, true),
                4097);

            PawnState decoded = RoundTrip(in original);

            Assert.Equal(4097, decoded.CarrierId);
            Assert.True(decoded.IsCarrierRelative);
            Assert.Equal(original.Position, decoded.Position);
            Assert.Equal(original.Flags, decoded.Flags);
            Assert.Equal(original.YawDegrees, decoded.YawDegrees, 2);
            Assert.Equal(original.Velocity.Z, decoded.Velocity.Z, 3);
        }

        [Fact]
        public void TheStateOccupiesTheDocumentedNumberOfBytes() {
            var state = new PawnState(Vector3.One, 45f, Vector3.UnitZ, 0, 7);
            var buffer = new byte[64];
            var writer = new NetWriter(buffer);

            state.Serialize(ref writer);

            Assert.Equal(CarrierStateWireBytes, writer.Written);
        }

        [Fact]
        public void TheSamePoseAgainstDifferentCarriersIsNotTheSameState() {
            var onTheWorld = new PawnState(new Vector3(2f, 0f, 2f), 0f, Vector3.Zero, 0);
            PawnState onACarrier = onTheWorld.WithCarrier(3);

            Assert.False(PawnState.ApproximatelyEquals(in onTheWorld, in onACarrier));
            Assert.True(PawnState.ApproximatelyEquals(in onACarrier, in onACarrier));
        }

        [Fact]
        public void QuantizingAndReflaggingLeaveTheFrameAlone() {
            var state = new PawnState(
                new Vector3(1f, 2f, 3f),
                123.456f,
                new Vector3(0.3f, -0.7f, 1.1f),
                PawnState.PackFlags(WireLocomotion.Walk, false, true),
                12);

            Assert.Equal(12, state.Quantized().CarrierId);
            Assert.Equal(12, state.WithFlags(WireLocomotion.Sprint, true, false).CarrierId);
        }

        [Fact]
        public void RelabellingAFrameMovesNoNumbers() {
            var state = new PawnState(new Vector3(5f, 6f, 7f), 33f, new Vector3(1f, 2f, 3f), 9, 2);

            PawnState relabelled = state.WithCarrier(PawnState.WorldCarrierId);

            Assert.Equal(PawnState.WorldCarrierId, relabelled.CarrierId);
            Assert.Equal(state.Position, relabelled.Position);
            Assert.Equal(state.Velocity, relabelled.Velocity);
            Assert.Equal(state.YawDegrees, relabelled.YawDegrees);
            Assert.Equal(state.Flags, relabelled.Flags);
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
