using System;

namespace AlpineLib.Netcode.Appearance {
    /// <summary>
    /// Converts between a transport peer id and the ushort subject key an outfit is published under on
    /// the state channel.
    /// </summary>
    public static class AppearanceSubjectKey {
        /// <summary>The state channel key for <paramref name="peerId"/>.</summary>
        public static ushort FromPeerId(int peerId) {
            if (peerId < ushort.MinValue || peerId > ushort.MaxValue) {
                throw new ArgumentOutOfRangeException(nameof(peerId), $"Peer id {peerId} does not fit a ushort subject key.");
            }

            return (ushort)peerId;
        }

        /// <summary>The peer id a state channel <paramref name="key"/> was published for.</summary>
        public static int ToPeerId(ushort key) {
            return key;
        }
    }
}
