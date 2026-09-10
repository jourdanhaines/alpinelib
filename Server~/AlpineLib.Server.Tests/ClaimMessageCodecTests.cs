using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Sessions.Claims.Messages;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Pins the claim messages to the bytes they put on the wire. The serializer and deserializer are the
    /// whole schema of this protocol, so a field added to one and not the other, or a size that quietly
    /// changes, can only be caught here.
    /// </summary>
    public sealed class ClaimMessageCodecTests {
        [Theory]
        [InlineData((ushort)0)]
        [InlineData((ushort)1)]
        [InlineData((ushort)ushort.MaxValue)]
        public void ClaimRequestRoundTrips(ushort slot) {
            var decoded = RoundTrip(new ClaimRequest(slot), out int written);

            Assert.Equal(slot, decoded.Slot);
            Assert.Equal(2, written);
        }

        [Theory]
        [InlineData((ushort)0)]
        [InlineData((ushort)7)]
        [InlineData((ushort)ushort.MaxValue)]
        public void ClaimReleaseRoundTrips(ushort slot) {
            var decoded = RoundTrip(new ClaimRelease(slot), out int written);

            Assert.Equal(slot, decoded.Slot);
            Assert.Equal(2, written);
        }

        [Theory]
        [InlineData((ushort)0, -1)]
        [InlineData((ushort)3, 0)]
        [InlineData((ushort)ushort.MaxValue, int.MaxValue)]
        public void ClaimChangedRoundTrips(ushort slot, int holderPeerId) {
            var decoded = RoundTrip(new ClaimChanged(slot, holderPeerId), out int written);

            Assert.Equal(slot, decoded.Slot);
            Assert.Equal(holderPeerId, decoded.HolderPeerId);
            Assert.Equal(6, written);
        }

        /// <summary>
        /// The free marker has to survive the wire as a negative number rather than as an unsigned peer
        /// id, which is the one thing a widened holder field would silently break.
        /// </summary>
        [Fact]
        public void AFreeSlotSurvivesAsNegativeOne() {
            var decoded = RoundTrip(new ClaimChanged(12, -1), out int _);

            Assert.Equal(-1, decoded.HolderPeerId);
        }

        /// <summary>
        /// These numbers are a compatibility contract with shipped builds. A test that reads them from the
        /// constants would pin nothing, so they are written out.
        /// </summary>
        [Fact]
        public void MessageIdsAreThePinnedNumbers() {
            Assert.Equal(84, ClaimMessageIds.ClaimRequest);
            Assert.Equal(85, ClaimMessageIds.ClaimRelease);
            Assert.Equal(86, ClaimMessageIds.ClaimChanged);
        }

        private static TMessage RoundTrip<TMessage>(TMessage message, out int written)
            where TMessage : struct, INetMessage {
            var buffer = new byte[64];
            var writer = new NetWriter(buffer);

            message.Serialize(ref writer);
            written = writer.Written;

            var reader = new NetReader(buffer, 0, written);
            TMessage decoded = default;
            decoded.Deserialize(ref reader);

            Assert.True(reader.IsExhausted, "The reader did not consume every byte the writer produced.");
            return decoded;
        }
    }
}
