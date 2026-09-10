using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// An <see cref="INetTransport"/> that moves bytes between objects in one process instead of over a
    /// socket, so session tests can assert on ordering and phase timing with no real network in the way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It keeps the one promise the real transport makes that the layers above depend on: every event is
    /// raised synchronously from inside <see cref="Poll"/> and never from anywhere else. Delivery is
    /// otherwise perfect and instant — loss and latency are a replication concern, not a session one.
    /// </para>
    /// <para>
    /// Everything it knows about who is connected to whom lives in its
    /// <see cref="SessionLoopbackNetwork"/>, never in shared static state, so test classes running in
    /// parallel cannot see each other's peers.
    /// </para>
    /// </remarks>
    internal sealed class SessionLoopbackTransport : INetTransport {
        /// <summary>Handle every client uses to address the server it is connected to.</summary>
        public static readonly PeerHandle ServerPeer = new PeerHandle(0);

        private readonly SessionLoopbackNetwork _network;
        private readonly bool _isServer;
        private readonly Queue<TransportEvent> _incoming = new Queue<TransportEvent>();

        private bool _isRunning;
        private bool _isDisposed;

        internal SessionLoopbackTransport(SessionLoopbackNetwork network, bool isServer) {
            _network = network;
            _isServer = isServer;
            Handle = PeerHandle.None;
        }

        /// <inheritdoc />
        public event Action<PeerHandle> OnPeerConnected;

        /// <inheritdoc />
        public event Action<PeerHandle, DisconnectReason> OnPeerDisconnected;

        /// <inheritdoc />
        public event Action<PeerHandle, ArraySegment<byte>, DeliveryClass> OnData;

        /// <summary>This client's handle as the server knows it. Meaningless on the server transport.</summary>
        public PeerHandle Handle { get; internal set; }

        /// <summary>True between a start and a stop.</summary>
        public bool IsRunning => _isRunning;

        /// <summary>True for the single server-side transport of a network.</summary>
        public bool IsServer => _isServer;

        /// <inheritdoc />
        public void StartServer(int port, int maxPeers, string protocolKey) {
            _isRunning = true;
        }

        /// <inheritdoc />
        public void StartClient(string protocolKey) {
            _isRunning = true;
        }

        /// <inheritdoc />
        public void Connect(NetEndpoint endpoint) {
            if (!_isRunning) {
                throw new InvalidOperationException("Call StartClient before connecting.");
            }

            _network.ConnectClient(this);
        }

        /// <inheritdoc />
        public void Send(PeerHandle peer, ReadOnlySpan<byte> payload, DeliveryClass delivery) {
            if (!_isRunning || payload.IsEmpty) {
                return;
            }

            _network.Send(this, peer, payload.ToArray(), delivery);
        }

        /// <inheritdoc />
        public void Disconnect(PeerHandle peer) {
            _network.Disconnect(this, peer, DisconnectReason.Graceful);
        }

        /// <inheritdoc />
        public void Poll() {
            if (!_isRunning) {
                return;
            }

            // The budget is snapshotted first: a handler that sends something must not have its own
            // reply delivered inside the same drain, or ordering would depend on how deep the call
            // stack happened to go.
            int budget = _incoming.Count;
            while (budget > 0 && _incoming.Count > 0) {
                budget--;
                Raise(_incoming.Dequeue());
            }
        }

        /// <inheritdoc />
        public void Stop() {
            _isRunning = false;
            _incoming.Clear();
        }

        /// <inheritdoc />
        public int GetPingMs(PeerHandle peer) {
            return 0;
        }

        /// <inheritdoc />
        public void Dispose() {
            if (_isDisposed) {
                return;
            }

            _isDisposed = true;
            Stop();
            _network.Forget(this);
        }

        internal void EnqueueConnected(PeerHandle peer) {
            _incoming.Enqueue(TransportEvent.Connected(peer));
        }

        internal void EnqueueDisconnected(PeerHandle peer, DisconnectReason reason) {
            _incoming.Enqueue(TransportEvent.Disconnected(peer, reason));
        }

        internal void EnqueueData(PeerHandle sender, byte[] payload, DeliveryClass delivery) {
            _incoming.Enqueue(TransportEvent.Data(sender, payload, delivery));
        }

        private void Raise(TransportEvent transportEvent) {
            if (transportEvent.Kind == TransportEventKind.Connected) {
                OnPeerConnected?.Invoke(transportEvent.Peer);
                return;
            }

            if (transportEvent.Kind == TransportEventKind.Disconnected) {
                OnPeerDisconnected?.Invoke(transportEvent.Peer, transportEvent.Reason);
                return;
            }

            OnData?.Invoke(transportEvent.Peer, new ArraySegment<byte>(transportEvent.Payload), transportEvent.Delivery);
        }

        /// <summary>What a queued event is.</summary>
        private enum TransportEventKind : byte {
            Connected = 0,
            Disconnected = 1,
            Data = 2
        }

        /// <summary>One queued event, waiting for the next poll to turn it into a callback.</summary>
        private readonly struct TransportEvent {
            private TransportEvent(TransportEventKind kind, PeerHandle peer, byte[] payload, DeliveryClass delivery, DisconnectReason reason) {
                Kind = kind;
                Peer = peer;
                Payload = payload;
                Delivery = delivery;
                Reason = reason;
            }

            public TransportEventKind Kind { get; }

            public PeerHandle Peer { get; }

            public byte[] Payload { get; }

            public DeliveryClass Delivery { get; }

            public DisconnectReason Reason { get; }

            public static TransportEvent Connected(PeerHandle peer) {
                return new TransportEvent(TransportEventKind.Connected, peer, null, DeliveryClass.ReliableOrdered, DisconnectReason.Graceful);
            }

            public static TransportEvent Disconnected(PeerHandle peer, DisconnectReason reason) {
                return new TransportEvent(TransportEventKind.Disconnected, peer, null, DeliveryClass.ReliableOrdered, reason);
            }

            public static TransportEvent Data(PeerHandle sender, byte[] payload, DeliveryClass delivery) {
                return new TransportEvent(TransportEventKind.Data, sender, payload, delivery, DisconnectReason.Graceful);
            }
        }
    }
}
