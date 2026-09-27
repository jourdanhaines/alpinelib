using System;
using System.Collections.Generic;

namespace AlpineLib.Netcode.Transport {
    /// <summary>One end of a <see cref="LoopbackNetwork"/>; created through the network, never directly.</summary>
    public sealed class LoopbackTransport : INetTransport {
        /// <summary>The handle clients know the server by.</summary>
        public static readonly PeerHandle ServerPeer = new PeerHandle(0);

        private readonly LoopbackNetwork _network;
        private readonly Queue<LoopbackTransportEvent> _incoming = new Queue<LoopbackTransportEvent>();

        private bool _isRunning;
        private bool _isDisposed;

        internal LoopbackTransport(LoopbackNetwork network, bool isServer) {
            _network = network;
            IsServer = isServer;
            Handle = PeerHandle.None;
        }

        /// <inheritdoc />
        public event Action<PeerHandle> OnPeerConnected;

        /// <inheritdoc />
        public event Action<PeerHandle, DisconnectReason> OnPeerDisconnected;

        /// <inheritdoc />
        public event Action<PeerHandle, ArraySegment<byte>, DeliveryClass> OnData;

        /// <summary>This client's handle as the server sees it; none on the server end.</summary>
        public PeerHandle Handle { get; internal set; }

        /// <summary>True between a start call and <see cref="Stop"/>.</summary>
        public bool IsRunning => _isRunning;

        /// <summary>True for the network's server end.</summary>
        public bool IsServer { get; }

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
            if (!_isRunning) throw new InvalidOperationException("Call StartClient before connecting.");

            _network.ConnectClient(this);
        }

        /// <inheritdoc />
        public void Send(PeerHandle peer, ReadOnlySpan<byte> payload, DeliveryClass delivery) {
            if (!_isRunning || payload.IsEmpty) return;

            _network.Send(this, peer, payload.ToArray(), delivery);
        }

        /// <inheritdoc />
        public void Disconnect(PeerHandle peer) {
            _network.Disconnect(this, peer, DisconnectReason.Graceful);
        }

        /// <summary>Raises what was queued before this call; replies sent by handlers wait for the next poll.</summary>
        public void Poll() {
            if (!_isRunning) return;

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
            if (_isDisposed) return;

            _isDisposed = true;
            Stop();
            _network.Forget(this);
        }

        internal void EnqueueConnected(PeerHandle peer) {
            _incoming.Enqueue(LoopbackTransportEvent.Connected(peer));
        }

        internal void EnqueueDisconnected(PeerHandle peer, DisconnectReason reason) {
            _incoming.Enqueue(LoopbackTransportEvent.Disconnected(peer, reason));
        }

        internal void EnqueueData(PeerHandle sender, byte[] payload, DeliveryClass delivery) {
            _incoming.Enqueue(LoopbackTransportEvent.Data(sender, payload, delivery));
        }

        private void Raise(LoopbackTransportEvent transportEvent) {
            if (transportEvent.Kind == LoopbackTransportEventKind.Connected) {
                OnPeerConnected?.Invoke(transportEvent.Peer);
                return;
            }

            if (transportEvent.Kind == LoopbackTransportEventKind.Disconnected) {
                OnPeerDisconnected?.Invoke(transportEvent.Peer, transportEvent.Reason);
                return;
            }

            OnData?.Invoke(transportEvent.Peer, new ArraySegment<byte>(transportEvent.Payload), transportEvent.Delivery);
        }
    }
}
