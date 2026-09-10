using System.Collections.Generic;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The switchboard behind <see cref="SessionLoopbackTransport"/>: one server transport, any number of
    /// client transports, and the peer-handle bookkeeping that makes them address each other exactly the
    /// way the real ones do.
    /// </summary>
    internal sealed class SessionLoopbackNetwork {
        private readonly List<SessionLoopbackTransport> _clients = new List<SessionLoopbackTransport>();

        private SessionLoopbackTransport _server;
        private int _nextPeerId = 1;

        /// <summary>The single server-side transport. Peer handles are only meaningful relative to it.</summary>
        public SessionLoopbackTransport CreateServerTransport() {
            _server = new SessionLoopbackTransport(this, true);
            return _server;
        }

        /// <summary>A client transport that dials the server when its facade connects.</summary>
        public SessionLoopbackTransport CreateClientTransport() {
            return new SessionLoopbackTransport(this, false);
        }

        /// <summary>Severs a client's link the way a timeout would: no notice from either side.</summary>
        public void DropClient(SessionLoopbackTransport client) {
            Disconnect(client, SessionLoopbackTransport.ServerPeer, DisconnectReason.Timeout);
        }

        internal void ConnectClient(SessionLoopbackTransport client) {
            client.Handle = new PeerHandle(_nextPeerId);
            _nextPeerId++;
            _clients.Add(client);

            _server.EnqueueConnected(client.Handle);
            client.EnqueueConnected(SessionLoopbackTransport.ServerPeer);
        }

        internal void Send(SessionLoopbackTransport source, PeerHandle target, byte[] payload, DeliveryClass delivery) {
            if (!source.IsServer) {
                _server?.EnqueueData(source.Handle, payload, delivery);
                return;
            }

            SessionLoopbackTransport client = FindClient(target);
            client?.EnqueueData(SessionLoopbackTransport.ServerPeer, payload, delivery);
        }

        internal void Disconnect(SessionLoopbackTransport source, PeerHandle target, DisconnectReason reason) {
            SessionLoopbackTransport client = source.IsServer ? FindClient(target) : source;
            if (client == null || !_clients.Remove(client)) {
                return;
            }

            _server?.EnqueueDisconnected(client.Handle, reason);
            client.EnqueueDisconnected(SessionLoopbackTransport.ServerPeer, reason);
        }

        internal void Forget(SessionLoopbackTransport transport) {
            _clients.Remove(transport);
            if (_server == transport) {
                _server = null;
            }
        }

        private SessionLoopbackTransport FindClient(PeerHandle handle) {
            for (int clientIndex = 0; clientIndex < _clients.Count; clientIndex++) {
                if (_clients[clientIndex].Handle == handle) {
                    return _clients[clientIndex];
                }
            }

            return null;
        }
    }
}
