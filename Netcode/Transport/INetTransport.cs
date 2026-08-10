using System;

namespace AlpineLib.Netcode.Transport {
    /// <summary>
    /// The seam between everything alpinelib knows about networking and the library that actually moves
    /// bytes. Sessions, replication and chat are written against this interface alone, which is what lets
    /// a future <c>SteamRelayTransport</c> (or an in-memory fake in tests) drop in without any of them
    /// changing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Threading.</b> Every event on this interface is raised synchronously from inside
    /// <see cref="Poll"/>, on whichever thread called it — the Unity main thread in the editor and
    /// player, the fixed-step game-loop thread on the dedicated server. There is no dispatcher and no
    /// marshalling anywhere above the transport, and implementations must not raise events from a
    /// background thread.
    /// </para>
    /// <para>
    /// <b>Payload lifetime.</b> The <see cref="ArraySegment{T}"/> handed to <see cref="OnData"/> points
    /// into the implementation's receive buffer and is recycled as soon as the handler returns. Handlers
    /// must read what they need synchronously and never retain the segment or its array.
    /// </para>
    /// </remarks>
    public interface INetTransport : IDisposable {
        /// <summary>A peer finished connecting. On a client this fires once, for the server link.</summary>
        event Action<PeerHandle> OnPeerConnected;

        /// <summary>
        /// A peer's connection ended, whether it was closed, timed out or never came up. Fires for a
        /// failed connect attempt too, so a client can distinguish "refused" from "unreachable".
        /// </summary>
        event Action<PeerHandle, DisconnectReason> OnPeerDisconnected;

        /// <summary>
        /// A payload arrived. The delivery class reports how it was sent, so the receiving side can tell
        /// gameplay traffic from the chat pipe without inspecting the payload.
        /// </summary>
        event Action<PeerHandle, ArraySegment<byte>, DeliveryClass> OnData;

        /// <summary>
        /// Binds a listening socket and starts accepting connections whose connect key matches.
        /// </summary>
        /// <param name="port">UDP port to bind, or zero to take any free port.</param>
        /// <param name="maxPeers">Connections accepted at once; further requests are rejected.</param>
        /// <param name="protocolKey">
        /// The version gate from <c>NetProtocol.BuildConnectKey</c>. This is the only transport-level
        /// handshake there is — a mismatched build is turned away before it can send a byte.
        /// </param>
        void StartServer(int port, int maxPeers, string protocolKey);

        /// <summary>
        /// Binds an ephemeral local socket so <see cref="Connect"/> can be called. Does not dial.
        /// </summary>
        /// <param name="protocolKey">The connect key presented to the server, as above.</param>
        void StartClient(string protocolKey);

        /// <summary>Dials a server. Completion — or failure — arrives as an event during <see cref="Poll"/>.</summary>
        void Connect(NetEndpoint endpoint);

        /// <summary>
        /// Queues a payload to a peer. Sending to a peer that has already gone is a no-op rather than an
        /// error: disconnects and sends race by nature, and callers should not have to guard every send.
        /// </summary>
        void Send(PeerHandle peer, ReadOnlySpan<byte> payload, DeliveryClass delivery);

        /// <summary>Closes one peer's connection gracefully.</summary>
        void Disconnect(PeerHandle peer);

        /// <summary>
        /// Delivers everything received since the last call, as synchronous events on this thread. Call
        /// once per frame or tick.
        /// </summary>
        void Poll();

        /// <summary>Closes every connection and releases the socket. The transport may be started again.</summary>
        void Stop();

        /// <summary>
        /// Round-trip time to a peer in milliseconds — what players call ping — or a negative value when
        /// the peer is unknown.
        /// </summary>
        int GetPingMs(PeerHandle peer);
    }
}
