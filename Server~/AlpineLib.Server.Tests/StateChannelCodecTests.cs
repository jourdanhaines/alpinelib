using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication.StateChannel;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The state channel wire format round-tripped through the real writer and reader. There is no
    /// schema anywhere in this protocol — the serialize and deserialize bodies are the schema — so a
    /// field added to one and forgotten in the other is caught here or in production, and nowhere in
    /// between.
    /// </summary>
    public sealed class StateChannelCodecTests {
        [Fact]
        public void ARecordCarriesItsSubjectItsTickAndItsState() {
            var original = new StateChannelRecord<StateChannelTestState>(
                7,
                123456u,
                new StateChannelTestState(412.5f, -8.25f, -3));

            StateChannelRecord<StateChannelTestState> decoded = RoundTrip(in original);

            Assert.Equal(7, decoded.Id);
            Assert.Equal(123456u, decoded.Tick);
            Assert.Equal(412.5f, decoded.State.Distance);
            Assert.Equal(-8.25f, decoded.State.Velocity);
            Assert.Equal(-3, decoded.State.Notch);
        }

        [Fact]
        public void AnEnvelopeKeepsItsRecordsInOrder() {
            var records = new List<StateChannelRecord<StateChannelTestState>> {
                new StateChannelRecord<StateChannelTestState>(3, 10u, new StateChannelTestState(1f, 2f, 1)),
                new StateChannelRecord<StateChannelTestState>(1, 11u, new StateChannelTestState(3f, 4f, 2)),
                new StateChannelRecord<StateChannelTestState>(2, 12u, new StateChannelTestState(5f, 6f, 3)),
            };

            var original = new StateChannelEnvelope<StateChannelTestState>(999u, records);

            StateChannelEnvelope<StateChannelTestState> decoded = RoundTrip(in original);

            Assert.Equal(999u, decoded.ServerTick);
            Assert.Equal(3, decoded.Records.Count);
            Assert.Equal(3, decoded.Records[0].Id);
            Assert.Equal(1, decoded.Records[1].Id);
            Assert.Equal(2, decoded.Records[2].Id);
            Assert.Equal(11u, decoded.Records[1].Tick);
            Assert.Equal(3f, decoded.Records[1].State.Distance);
        }

        [Fact]
        public void ANullRecordListIsWrittenAsAnEmptyEnvelope() {
            var original = new StateChannelEnvelope<StateChannelTestState>(4u, null);

            StateChannelEnvelope<StateChannelTestState> decoded = RoundTrip(in original);

            Assert.Equal(4u, decoded.ServerTick);
            Assert.NotNull(decoded.Records);
            Assert.Empty(decoded.Records);
        }

        [Fact]
        public void AnOverlongRecordClaimIsRefusedRatherThanAllocatedFor() {
            var buffer = new byte[64];
            var writer = new NetWriter(buffer);
            writer.WriteUInt(1u);
            writer.WriteUShort((ushort)(StateChannelEnvelope<StateChannelTestState>.MaxRecordCount + 1));
            int written = writer.Written;

            Assert.Throws<NetProtocolException>(() => DecodeEnvelope(buffer, written));
        }

        /// <summary>A ref-struct reader cannot be touched from a lambda, so the throwing call lives here.</summary>
        private static void DecodeEnvelope(byte[] buffer, int length) {
            var reader = new NetReader(buffer, 0, length);
            StateChannelEnvelope<StateChannelTestState> envelope = default;
            envelope.Deserialize(ref reader);
        }

        private static TMessage RoundTrip<TMessage>(in TMessage message) where TMessage : struct, INetMessage {
            var buffer = new byte[4096];
            var writer = new NetWriter(buffer);
            message.Serialize(ref writer);

            var reader = new NetReader(buffer, 0, writer.Written);
            TMessage decoded = default;
            decoded.Deserialize(ref reader);

            Assert.Equal(0, reader.Remaining);
            return decoded;
        }
    }
}
