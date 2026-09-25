using System;
using AlpineLib.Netcode.Appearance;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Peer id to state channel subject key and back.
    /// </summary>
    public sealed class AppearanceSubjectKeyTests {
        [Theory]
        [InlineData(0)]
        [InlineData(7)]
        [InlineData(65535)]
        public void InRangePeerIdsRoundTrip(int peerId) {
            Assert.Equal(peerId, AppearanceSubjectKey.ToPeerId(AppearanceSubjectKey.FromPeerId(peerId)));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(65536)]
        public void OutOfRangePeerIdsAreRefused(int peerId) {
            Assert.Throws<ArgumentOutOfRangeException>(() => AppearanceSubjectKey.FromPeerId(peerId));
        }
    }
}
