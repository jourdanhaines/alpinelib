using System;

namespace AlpineLib.Chat {
    /// <summary>
    /// The client-side pipe chat frames travel through, deliberately ignorant of what is inside them.
    /// </summary>
    /// <remarks>
    /// The concrete adapter lives on the netcode side and wraps the game connection: it sends a frame as
    /// the payload of envelope message 192 on the reliable chat channel, and raises
    /// <see cref="PayloadReceived"/> for every envelope of that id that arrives. Chat therefore needs no
    /// socket, no port and no second connection — and a test can drive the whole pipeline through a
    /// two-line fake.
    /// </remarks>
    public interface IChatTransport {
        /// <summary>True while frames can actually be sent.</summary>
        bool IsConnected { get; }

        /// <summary>Raised when the underlying connection comes up.</summary>
        event Action Connected;

        /// <summary>Raised when the underlying connection goes away.</summary>
        event Action Disconnected;

        /// <summary>
        /// Raised for each frame received from the server. The segment is only valid for the duration of
        /// the call — a handler that keeps the bytes must copy them.
        /// </summary>
        event Action<ArraySegment<byte>> PayloadReceived;

        /// <summary>Sends one frame. Ignored when <see cref="IsConnected"/> is false.</summary>
        void Send(byte[] payload, int offset, int length);
    }
}
