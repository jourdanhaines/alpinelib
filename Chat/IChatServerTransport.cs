using System;
using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Chat {
    /// <summary>
    /// The server-side pipe, addressed by player rather than by peer.
    /// </summary>
    /// <remarks>
    /// The session layer owns the <c>PlayerId</c> to <c>PeerHandle</c> map and applies it inside the
    /// adapter, so the chat pipeline never learns that transport handles exist. That is what lets a
    /// player keep their chat identity across a reconnect: the handle changes, the id does not.
    /// </remarks>
    public interface IChatServerTransport {
        /// <summary>
        /// Raised for each frame received from a player. The segment is only valid for the duration of
        /// the call — a handler that keeps the bytes must copy them.
        /// </summary>
        event Action<PlayerId, ArraySegment<byte>> PayloadReceived;

        /// <summary>Sends one frame to a player. Dropped silently when that player is not connected.</summary>
        void SendTo(PlayerId player, byte[] payload, int offset, int length);
    }
}
