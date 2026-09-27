using System.Collections.Generic;

namespace AlpineLib.Netcode.Transport {
    /// <summary>
    /// An in-memory network of one server and any number of clients, for running real
    /// <see cref="NetServer"/>/<see cref="NetClient"/> pairs in one process (gates, tests, tools).
    /// </summary>
    /// <remarks>
    /// Delivery is immediate and lossless in send order; nothing arrives until the receiver polls, so a
    /// caller pumping both ends by hand controls exactly when every message lands.
    /// </remarks>
    public sealed class LoopbackNetwork {
        private readonly List<LoopbackTransport> _clients = new List<LoopbackTransport>();

        private LoopbackTransport _server;
        private int _nextPeerId = 1;

        /// <summary>The server end; a second call replaces the first.</summary>
        public LoopbackTransport CreateServerTransport() {
            _server = new LoopbackTransport(this, true);
            return _server;
        }

        /// <summary>A client end, connected to the server when its owner calls Connect.</summary>
        public LoopbackTransport CreateClientTransport() {
            return new LoopbackTransport(this, false);
        }

        /// <summary>Cuts a client off as if it timed out.</summary>
        public void DropClient(LoopbackTransport client) {
            Disconnect(client, LoopbackTransport.ServerPeer, DisconnectReason.Timeout);
        }

        internal void ConnectClient(LoopbackTransport client) {
            client.Handle = new PeerHandle(_nextPeerId);
            _nextPeerId++;
            _clients.Add(client);

            _server?.EnqueueConnected(client.Handle);
            client.EnqueueConnected(LoopbackTransport.ServerPeer);
        }

        internal void Send(LoopbackTransport source, PeerHandle target, byte[] payload, DeliveryClass delivery) {
            if (!source.IsServer) {
                _server?.EnqueueData(source.Handle, payload, delivery);
                return;
            }

            LoopbackTransport client = FindClient(target);
            client?.EnqueueData(LoopbackTransport.ServerPeer, payload, delivery);
        }

        internal void Disconnect(LoopbackTransport source, PeerHandle target, DisconnectReason reason) {
            LoopbackTransport client = source.IsServer ? FindClient(target) : source;
            if (client == null || !_clients.Remove(client)) return;

            _server?.EnqueueDisconnected(client.Handle, reason);
            client.EnqueueDisconnected(LoopbackTransport.ServerPeer, reason);
        }

        internal void Forget(LoopbackTransport transport) {
            _clients.Remove(transport);
            if (_server == transport) _server = null;
        }

        private LoopbackTransport FindClient(PeerHandle handle) {
            foreach (LoopbackTransport client in _clients) {
                if (client.Handle == handle) return client;
            }

            return null;
        }
    }
}
