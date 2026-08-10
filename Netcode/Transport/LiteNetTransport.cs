using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteDisconnectReason = LiteNetLib.DisconnectReason;

namespace AlpineLib.Netcode.Transport {
    /// <summary>
    /// The shipping transport: a thin wrapper over LiteNetLib 2.1.4's <see cref="NetManager"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LiteNetLib runs its own receive and logic threads, but <c>UnsyncedEvents</c> is left off, so
    /// arriving packets are parked in a queue and only turned into listener callbacks when
    /// <see cref="Poll"/> drains them. That is what lets every layer above this one — sessions,
    /// replication, chat — be single-threaded code with no locks and no dispatcher: their handlers run
    /// inside the caller's <see cref="Poll"/>, on the Unity main thread or the server's game-loop
    /// thread.
    /// </para>
    /// <para>
    /// Two channels are configured. Channel 0 carries gameplay in all three of its delivery flavours;
    /// channel 1 carries nothing but chat, so a chat backlog cannot head-of-line block the reliable
    /// gameplay stream. The channel count is part of the wire contract — both ends allocate their
    /// channel arrays from it — so it is a constant here rather than a setting.
    /// </para>
    /// <para>
    /// LiteNetLib's own listener interface is implemented explicitly. The names collide with the
    /// <see cref="INetTransport"/> events almost one for one, and explicit implementation keeps the
    /// library's callbacks out of this type's public surface where they would invite being called from
    /// the wrong thread.
    /// </para>
    /// </remarks>
    public sealed class LiteNetTransport : INetTransport, INetEventListener {
        /// <summary>Channel every gameplay payload rides on.</summary>
        public const byte GameChannel = 0;

        /// <summary>Channel reserved for the chat envelope.</summary>
        public const byte ChatChannel = 1;

        private const byte ChannelCount = 2;
        private const int DefaultDisconnectTimeoutMs = 5000;
        private const int UnknownPingMs = -1;

        private readonly Dictionary<int, NetPeer> peersByHandleId = new Dictionary<int, NetPeer>();
        private readonly NetManager manager;

        private string connectKey = string.Empty;
        private int maxPeers = int.MaxValue;
        private bool isServer;
        private bool disposed;

        /// <param name="disconnectTimeoutMs">
        /// How long a silent peer is kept before the link is declared dead. Mirrors
        /// <c>NetConfig.DisconnectTimeoutMs</c>; both ends should agree.
        /// </param>
        public LiteNetTransport(int disconnectTimeoutMs = DefaultDisconnectTimeoutMs) {
            if (disconnectTimeoutMs <= 0) {
                throw new ArgumentOutOfRangeException(nameof(disconnectTimeoutMs), "Disconnect timeout must be positive.");
            }

            manager = new NetManager(this);
            manager.ChannelsCount = ChannelCount;
            manager.UnsyncedEvents = false;
            manager.AutoRecycle = true;
            manager.UnconnectedMessagesEnabled = false;
            manager.DisconnectTimeout = disconnectTimeoutMs;
        }

        /// <inheritdoc />
        public event Action<PeerHandle> OnPeerConnected;

        /// <inheritdoc />
        public event Action<PeerHandle, DisconnectReason> OnPeerDisconnected;

        /// <inheritdoc />
        public event Action<PeerHandle, ArraySegment<byte>, DeliveryClass> OnData;

        /// <summary>True between a successful start and <see cref="Stop"/>.</summary>
        public bool IsRunning => manager.IsRunning;

        /// <summary>
        /// The port actually bound. Meaningful after a start — and the only way to learn the port when
        /// zero was passed, which is how tests and local dev avoid fighting over a fixed one.
        /// </summary>
        public int LocalPort => manager.LocalPort;

        /// <summary>How many peers are connected right now.</summary>
        public int ConnectedPeerCount => manager.ConnectedPeersCount;

        /// <inheritdoc />
        public void StartServer(int port, int maxPeers, string protocolKey) {
            ThrowIfDisposed();
            RequireStopped();

            if (port < 0 || port > 65535) {
                throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 0 and 65535.");
            }

            if (maxPeers <= 0) {
                throw new ArgumentOutOfRangeException(nameof(maxPeers), "A server must allow at least one peer.");
            }

            RequireProtocolKey(protocolKey);

            connectKey = protocolKey;
            this.maxPeers = maxPeers;
            isServer = true;

            if (manager.Start(port)) {
                return;
            }

            isServer = false;
            throw new InvalidOperationException($"Could not bind UDP port {port}.");
        }

        /// <inheritdoc />
        public void StartClient(string protocolKey) {
            ThrowIfDisposed();
            RequireStopped();
            RequireProtocolKey(protocolKey);

            connectKey = protocolKey;
            maxPeers = int.MaxValue;
            isServer = false;

            if (manager.Start()) {
                return;
            }

            throw new InvalidOperationException("Could not bind a local UDP port for the client.");
        }

        /// <inheritdoc />
        public void Connect(NetEndpoint endpoint) {
            ThrowIfDisposed();

            if (isServer || !manager.IsRunning) {
                throw new InvalidOperationException("Call StartClient before connecting.");
            }

            if (endpoint.Kind != TransportKind.Direct) {
                throw new NotSupportedException($"{nameof(LiteNetTransport)} only dials direct endpoints; {endpoint.Kind} needs its own transport.");
            }

            if (!endpoint.IsValid) {
                throw new ArgumentException("Endpoint carries no host and port to dial.", nameof(endpoint));
            }

            manager.Connect(endpoint.Host, endpoint.Port, connectKey);
        }

        /// <inheritdoc />
        public void Send(PeerHandle peer, ReadOnlySpan<byte> payload, DeliveryClass delivery) {
            if (payload.IsEmpty) {
                return;
            }

            if (!peersByHandleId.TryGetValue(peer.Id, out NetPeer target)) {
                return;
            }

            target.Send(payload, ChannelFor(delivery), MethodFor(delivery));
        }

        /// <inheritdoc />
        public void Disconnect(PeerHandle peer) {
            if (!peersByHandleId.TryGetValue(peer.Id, out NetPeer target)) {
                return;
            }

            manager.DisconnectPeer(target);
        }

        /// <inheritdoc />
        public void Poll() {
            if (!manager.IsRunning) {
                return;
            }

            manager.PollEvents();
        }

        /// <inheritdoc />
        public void Stop() {
            if (!manager.IsRunning) {
                return;
            }

            manager.Stop(true);
            peersByHandleId.Clear();
            isServer = false;
        }

        /// <inheritdoc />
        public int GetPingMs(PeerHandle peer) {
            if (!peersByHandleId.TryGetValue(peer.Id, out NetPeer target)) {
                return UnknownPingMs;
            }

            return target.RoundTripTime;
        }

        /// <inheritdoc />
        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            Stop();

            OnPeerConnected = null;
            OnPeerDisconnected = null;
            OnData = null;
        }

        /// <summary>Channel a delivery class rides on: chat is alone on its own, everything else shares 0.</summary>
        public static byte ChannelFor(DeliveryClass delivery) {
            return delivery == DeliveryClass.ReliableChat ? ChatChannel : GameChannel;
        }

        /// <summary>LiteNetLib delivery method backing a delivery class.</summary>
        public static DeliveryMethod MethodFor(DeliveryClass delivery) {
            switch (delivery) {
                case DeliveryClass.UnreliableSequenced:
                    // LiteNetLib's "Sequenced" is the unreliable one: drops allowed, stale packets discarded.
                    return DeliveryMethod.Sequenced;
                case DeliveryClass.Unreliable:
                    return DeliveryMethod.Unreliable;
                default:
                    // Both ReliableOrdered and ReliableChat are reliable ordered; only the channel differs.
                    return DeliveryMethod.ReliableOrdered;
            }
        }

        /// <summary>Recovers the delivery class a received payload was sent with.</summary>
        public static DeliveryClass ClassFor(byte channelNumber, DeliveryMethod deliveryMethod) {
            if (channelNumber == ChatChannel) {
                return DeliveryClass.ReliableChat;
            }

            switch (deliveryMethod) {
                case DeliveryMethod.Sequenced:
                    return DeliveryClass.UnreliableSequenced;
                case DeliveryMethod.Unreliable:
                    return DeliveryClass.Unreliable;
                default:
                    return DeliveryClass.ReliableOrdered;
            }
        }

        /// <summary>Collapses LiteNetLib's disconnect taxonomy onto the one the game layer branches on.</summary>
        public static DisconnectReason ReasonFor(LiteDisconnectReason reason) {
            switch (reason) {
                case LiteDisconnectReason.RemoteConnectionClose:
                case LiteDisconnectReason.DisconnectPeerCalled:
                    return DisconnectReason.Graceful;
                case LiteDisconnectReason.Timeout:
                case LiteDisconnectReason.Reconnect:
                case LiteDisconnectReason.PeerNotFound:
                    // Reconnect and PeerNotFound both mean this link is stale even though nobody said goodbye.
                    return DisconnectReason.Timeout;
                case LiteDisconnectReason.ConnectionRejected:
                case LiteDisconnectReason.InvalidProtocol:
                case LiteDisconnectReason.PeerToPeerConnection:
                    return DisconnectReason.Rejected;
                default:
                    return DisconnectReason.TransportError;
            }
        }

        void INetEventListener.OnConnectionRequest(ConnectionRequest request) {
            if (!isServer) {
                request.Reject();
                return;
            }

            if (manager.ConnectedPeersCount >= maxPeers) {
                request.Reject();
                return;
            }

            request.AcceptIfKey(connectKey);
        }

        void INetEventListener.OnPeerConnected(NetPeer peer) {
            peersByHandleId[peer.Id] = peer;
            OnPeerConnected?.Invoke(new PeerHandle(peer.Id));
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
            peersByHandleId.Remove(peer.Id);
            OnPeerDisconnected?.Invoke(new PeerHandle(peer.Id), ReasonFor(disconnectInfo.Reason));
        }

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod) {
            // Points straight at the pooled receive buffer, which AutoRecycle reclaims the moment this
            // returns — the interface contract is that handlers read synchronously and retain nothing.
            var payload = new ArraySegment<byte>(reader.RawData, reader.UserDataOffset, reader.UserDataSize);
            OnData?.Invoke(new PeerHandle(peer.Id), payload, ClassFor(channelNumber, deliveryMethod));
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError) {
            // Socket errors that matter arrive again as a disconnect; transient ones are noise here.
        }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
            // Unconnected messages are disabled; nothing should reach this.
        }

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) {
            // Latency is pulled on demand through GetPingMs rather than pushed.
        }

        private void ThrowIfDisposed() {
            if (disposed) {
                throw new ObjectDisposedException(nameof(LiteNetTransport));
            }
        }

        private void RequireStopped() {
            if (manager.IsRunning) {
                throw new InvalidOperationException("Transport is already running; call Stop first.");
            }
        }

        private static void RequireProtocolKey(string protocolKey) {
            if (string.IsNullOrEmpty(protocolKey)) {
                throw new ArgumentException("A protocol key is required; build it with NetProtocol.BuildConnectKey.", nameof(protocolKey));
            }
        }
    }
}
