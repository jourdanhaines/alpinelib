using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Replication.Messages;
using AlpineLib.Netcode.Replication.Origin;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The origin announcement and the epoch stamp on every position-bearing message survive the wire.
    /// </summary>
    public sealed class OriginWireCodecTests {
        private const ushort Epoch = 65534;

        private static readonly PawnState State = new PawnState(
            new Vector3(12.5f, 1f, -3.25f), 90f, new Vector3(1f, 0f, 0f), PawnState.PackFlags(WireLocomotion.Walk, false, true));

        [Fact]
        public void OriginShiftRoundTrips() {
            var origin = new SessionOrigin(Epoch, -2_000_000_000, 17, 128.0);

            OriginShift decoded = RoundTrip(new OriginShift(origin));

            Assert.Equal(origin, decoded.Origin);
        }

        [Fact]
        public void OriginShiftRefusesANegativeCellSizeOnTheWire() {
            var buffer = new byte[64];
            var writer = new NetWriter(buffer);
            writer.WriteUShort(1);
            writer.WriteInt(0);
            writer.WriteInt(0);
            writer.WriteDouble(-1.0);

            var reader = new NetReader(buffer, 0, writer.Written);
            var decoded = new OriginShift();

            Assert.Throws<NetProtocolException>(() => decoded.Deserialize(ref reader));
        }

        [Fact]
        public void EveryPositionBearingMessageCarriesItsEpoch() {
            var records = new List<EntitySnapshotRecord> { new EntitySnapshotRecord(4u, State) };
            var keyframes = new List<EntityKeyframeRecord> {
                new EntityKeyframeRecord(4u, 1, 2, AuthorityMode.OwnerClient, EntityKind.Pawn, 0, State)
            };

            Assert.Equal(Epoch, RoundTrip(new Snapshot(9u, records) { OriginEpoch = Epoch }).OriginEpoch);
            Assert.Equal(Epoch, RoundTrip(new SnapshotKeyframe(9u, keyframes) { OriginEpoch = Epoch }).OriginEpoch);
            Assert.Equal(Epoch, RoundTrip(new SpawnEntity(4u, 1, 2, AuthorityMode.OwnerClient, State) { OriginEpoch = Epoch }).OriginEpoch);
            Assert.Equal(Epoch, RoundTrip(new AuthorityCorrection(4u, 9u, 3u, State) { OriginEpoch = Epoch }).OriginEpoch);
            Assert.Equal(Epoch, RoundTrip(new OwnerPawnUpdate(4u, 9u, OwnerPawnUpdate.ResyncFlag, State) { OriginEpoch = Epoch }).OriginEpoch);
        }

        [Fact]
        public void TheStampDoesNotDisturbTheRestOfTheMessage() {
            OwnerPawnUpdate decoded = RoundTrip(new OwnerPawnUpdate(4u, 9u, OwnerPawnUpdate.ResyncFlag, State) { OriginEpoch = 3 });

            Assert.True(decoded.IsResync);
            Assert.Equal(9u, decoded.ClientTick);
            Assert.Equal(State.Position, decoded.State.Position);
        }

        [Fact]
        public void TheOriginShiftIdIsReservedInTheReplicationTail() {
            Assert.InRange(ReplicationMessageIds.OriginShift, MessageIdBudget.ReplicationTailBandStart, MessageIdBudget.ReplicationTailBandEnd);
            Assert.True(MessageIdBudget.IsReservedByLibrary(ReplicationMessageIds.OriginShift));
            Assert.True(MessageIdBudget.IsReservedByLibrary(MessageIdBudget.ReplicationTailBandEnd));
            Assert.False(MessageIdBudget.IsInGameBand(ReplicationMessageIds.OriginShift));
        }

        [Fact]
        public void TheProtocolVersionMovedForTheOriginEpoch() {
            Assert.Equal(8, NetProtocol.Version);
        }

        private static TMessage RoundTrip<TMessage>(TMessage message) where TMessage : INetMessage, new() {
            var buffer = new byte[512];
            var writer = new NetWriter(buffer);
            message.Serialize(ref writer);

            var reader = new NetReader(buffer, 0, writer.Written);
            var decoded = new TMessage();
            decoded.Deserialize(ref reader);

            Assert.Equal(0, reader.Remaining);
            return decoded;
        }
    }
}
