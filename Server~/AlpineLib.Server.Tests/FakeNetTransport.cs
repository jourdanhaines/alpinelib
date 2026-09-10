using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// An in-memory <see cref="INetTransport"/> pair for tests that care about the layers above the
    /// socket. Payloads move by reference to a copied array rather than over UDP, and delivery is
    /// perfect, instant and ordered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The real transport already has its own loopback tests over a genuine socket; what replication
    /// needs from a fake is determinism. A UDP loopback makes "did the snapshot cadence fire" and "did
    /// prediction converge" depend on the scheduler, and a flaky replication test is worse than none.
    /// </para>
    /// <para>
    /// The one promise it does keep exactly is the threading contract: nothing is delivered until
    /// <see cref="Poll"/> is called, and every event — connects included — is raised synchronously from
    /// inside it.
    /// </para>
    /// </remarks>
    public sealed class FakeNetTransport : INetTransport {
        private static readonly object ListenerLock = new object();
        private static readonly Dictionary<int, FakeNetTransport> Listeners = new Dictionary<int, FakeNetTransport>();

        private readonly Queue<QueuedPayload> inbox = new Queue<QueuedPayload>();
        private readonly Queue<Action> pendingEvents = new Queue<Action>();
        private readonly Dictionary<int, FakeNetTransport> links = new Dictionary<int, FakeNetTransport>();

        private int listenPort = -1;
        private int nextPeerId = 1;
        private bool disposed;

        /// <inheritdoc />
        public event Action<PeerHandle> OnPeerConnected;

        /// <inheritdoc />
        public event Action<PeerHandle, DisconnectReason> OnPeerDisconnected;

        /// <inheritdoc />
        public event Action<PeerHandle, ArraySegment<byte>, DeliveryClass> OnData;

        /// <summary>The handle this transport uses to address the peer it dialled, for a client.</summary>
        public PeerHandle RemoteHandle { get; private set; } = PeerHandle.None;

        /// <summary>The handle the peer it dialled uses to address this transport.</summary>
        public PeerHandle LocalHandleAtRemote { get; private set; } = PeerHandle.None;

        /// <inheritdoc />
        public void StartServer(int port, int maxPeers, string protocolKey) {
            lock (ListenerLock) {
                Listeners[port] = this;
            }

            listenPort = port;
        }

        /// <inheritdoc />
        public void StartClient(string protocolKey) {
        }

        /// <inheritdoc />
        public void Connect(NetEndpoint endpoint) {
            FakeNetTransport listener;

            lock (ListenerLock) {
                if (!Listeners.TryGetValue(endpoint.Port, out listener)) {
                    pendingEvents.Enqueue(() => OnPeerDisconnected?.Invoke(PeerHandle.None, DisconnectReason.TransportError));
                    return;
                }
            }

            listener.AcceptFrom(this);
        }

        /// <inheritdoc />
        public void Send(PeerHandle peer, ReadOnlySpan<byte> payload, DeliveryClass delivery) {
            if (!links.TryGetValue(peer.Id, out FakeNetTransport target)) {
                return;
            }

            target.inbox.Enqueue(new QueuedPayload(target.HandleFor(this), payload.ToArray(), delivery));
        }

        /// <inheritdoc />
        public void Disconnect(PeerHandle peer) {
            if (!links.TryGetValue(peer.Id, out FakeNetTransport target)) {
                return;
            }

            links.Remove(peer.Id);
            pendingEvents.Enqueue(() => OnPeerDisconnected?.Invoke(peer, DisconnectReason.Graceful));

            PeerHandle mirrored = target.HandleFor(this);
            target.links.Remove(mirrored.Id);
            target.pendingEvents.Enqueue(() => target.OnPeerDisconnected?.Invoke(mirrored, DisconnectReason.Graceful));
        }

        /// <inheritdoc />
        public void Poll() {
            while (pendingEvents.Count > 0) {
                pendingEvents.Dequeue().Invoke();
            }

            while (inbox.Count > 0) {
                QueuedPayload queued = inbox.Dequeue();
                OnData?.Invoke(queued.Sender, new ArraySegment<byte>(queued.Payload), queued.Delivery);
            }
        }

        /// <inheritdoc />
        public void Stop() {
            if (listenPort >= 0) {
                lock (ListenerLock) {
                    Listeners.Remove(listenPort);
                }

                listenPort = -1;
            }

            links.Clear();
            inbox.Clear();
            pendingEvents.Clear();
        }

        /// <inheritdoc />
        public int GetPingMs(PeerHandle peer) {
            return peer.IsValid ? 0 : -1;
        }

        /// <inheritdoc />
        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            Stop();
        }

        /// <summary>Registers an inbound link and queues the connect event on both ends.</summary>
        private void AcceptFrom(FakeNetTransport client) {
            var clientHandle = new PeerHandle(nextPeerId);
            nextPeerId++;
            var serverHandle = new PeerHandle(0);

            links[clientHandle.Id] = client;
            client.links[serverHandle.Id] = this;
            client.RemoteHandle = serverHandle;
            client.LocalHandleAtRemote = clientHandle;

            pendingEvents.Enqueue(() => OnPeerConnected?.Invoke(clientHandle));
            client.pendingEvents.Enqueue(() => client.OnPeerConnected?.Invoke(serverHandle));
        }

        /// <summary>The handle this transport is known by on the far side of a link.</summary>
        private PeerHandle HandleFor(FakeNetTransport other) {
            foreach (KeyValuePair<int, FakeNetTransport> link in links) {
                if (ReferenceEquals(link.Value, other)) {
                    return new PeerHandle(link.Key);
                }
            }

            return PeerHandle.None;
        }

        /// <summary>One queued payload waiting for the next poll.</summary>
        private readonly struct QueuedPayload {
            public QueuedPayload(PeerHandle sender, byte[] payload, DeliveryClass delivery) {
                Sender = sender;
                Payload = payload;
                Delivery = delivery;
            }

            /// <summary>Who sent it, as the receiver addresses them.</summary>
            public PeerHandle Sender { get; }

            /// <summary>A private copy of the bytes.</summary>
            public byte[] Payload { get; }

            /// <summary>The class it was sent on.</summary>
            public DeliveryClass Delivery { get; }
        }
    }
}
