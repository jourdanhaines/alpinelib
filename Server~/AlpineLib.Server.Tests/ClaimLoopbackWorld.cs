using System;
using System.Collections.Generic;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Sessions.Claims;
using AlpineLib.Netcode.Transport;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// One session's claim registry with real clients on the other end of a real framing path, driven a
    /// pump at a time. Everything the claim tests need that is not the registry itself lives here.
    /// </summary>
    /// <remarks>
    /// The roster is a plain list the test owns rather than <c>NetServer.Peers</c>, because half of what
    /// the registry promises is about peers that are connected but not members of this session — a
    /// connection attached to another lobby, or one already retired from this one. Those cases only exist
    /// when the two lists can disagree.
    /// </remarks>
    internal sealed class ClaimLoopbackWorld : IDisposable {
        private const float TickInterval = 1f / 30f;

        private readonly SessionLoopbackNetwork _network = new SessionLoopbackNetwork();
        private readonly SessionLoopbackTransport _serverTransport;
        private readonly NetServer _server;
        private readonly ServerClaimRegistry _registry;
        private readonly List<PeerHandle> _sessionPeers = new List<PeerHandle>();
        private readonly List<ClaimLoopbackClient> _clients = new List<ClaimLoopbackClient>();
        private readonly List<ClaimWireSpy> _spies = new List<ClaimWireSpy>();

        /// <summary>Stands the session up and starts the server.</summary>
        /// <param name="attachToRouter">
        /// False leaves the claim ids unbound, which is how a test drives the registry the way a
        /// multi-session front desk does — through the public handlers rather than through the router.
        /// </param>
        /// <param name="isClaimAllowed">The game's own authority rule, or null to answer for every slot.</param>
        public ClaimLoopbackWorld(
            bool attachToRouter = true,
            Func<ushort, PeerHandle, ServerClaimRegistry, bool> isClaimAllowed = null) {
            _serverTransport = _network.CreateServerTransport();
            _server = new NetServer(_serverTransport, BuildConfig());
            _registry = new ServerClaimRegistry(_server, () => _sessionPeers, isClaimAllowed);

            if (attachToRouter) {
                _registry.AttachToRouter();
            }

            _server.OnPeerDisconnected += HandlePeerDisconnected;
            _server.Start();
        }

        /// <summary>The session's claim authority.</summary>
        public ServerClaimRegistry Registry => _registry;

        /// <summary>The server facade the registry broadcasts through.</summary>
        public NetServer Server => _server;

        /// <summary>Every connected client, in the order they joined.</summary>
        public IReadOnlyList<ClaimLoopbackClient> Clients => _clients;

        /// <summary>The first client to join — the one most tests treat as "the player".</summary>
        public ClaimLoopbackClient PrimaryClient => _clients[0];

        /// <summary>
        /// The session roster the registry broadcasts to and validates senders against. Mutable so a test
        /// can put a connected peer outside the session.
        /// </summary>
        public IList<PeerHandle> SessionPeers => _sessionPeers;

        /// <summary>Dials a client, waits for the link, and puts it on the session roster.</summary>
        public ClaimLoopbackClient ConnectClient() {
            var client = new ClaimLoopbackClient(_network.CreateClientTransport(), BuildConfig());
            _clients.Add(client);
            client.Connect();
            WaitForConnection(() => client.IsConnected, "The loopback claim client never finished connecting.");
            client.BindPeerId(NewestPeer());
            _sessionPeers.Add(client.ServerSidePeer);
            return client;
        }

        /// <summary>
        /// Dials a peer that records the claim verdicts straight off the wire instead of folding them
        /// into a view, so a test can assert on what the server actually said and in what order.
        /// </summary>
        public ClaimWireSpy ConnectSpy() {
            var spy = new ClaimWireSpy(_network.CreateClientTransport(), BuildConfig());
            _spies.Add(spy);
            spy.Connect();
            WaitForConnection(() => spy.IsConnected, "The claim wire spy never finished connecting.");
            spy.BindPeerId(NewestPeer());
            _sessionPeers.Add(spy.ServerSidePeer);
            return spy;
        }

        /// <summary>Severs a client's link the way a timeout would. Pump afterwards to see the fallout.</summary>
        public void DropClient(ClaimLoopbackClient client) {
            _network.DropClient(client.Transport);
        }

        /// <summary>Advances the server and every attached peer by whole ticks.</summary>
        public void Pump(int ticks) {
            for (int tick = 0; tick < ticks; tick++) {
                _server.Update(TickInterval);

                for (int clientIndex = 0; clientIndex < _clients.Count; clientIndex++) {
                    _clients[clientIndex].Update(TickInterval);
                }

                for (int spyIndex = 0; spyIndex < _spies.Count; spyIndex++) {
                    _spies[spyIndex].Update(TickInterval);
                }
            }
        }

        /// <inheritdoc />
        public void Dispose() {
            for (int clientIndex = 0; clientIndex < _clients.Count; clientIndex++) {
                _clients[clientIndex].Dispose();
            }

            for (int spyIndex = 0; spyIndex < _spies.Count; spyIndex++) {
                _spies[spyIndex].Dispose();
            }

            _server.OnPeerDisconnected -= HandlePeerDisconnected;
            _server.Dispose();
            _serverTransport.Dispose();
        }

        /// <summary>
        /// Retires a dropped peer exactly the way a front desk does: every slot it was holding freed
        /// first, while it is still on the roster, and only then taken off it. The copy of the free
        /// verdict addressed to the dead peer is part of what the shipped path does.
        /// </summary>
        private void HandlePeerDisconnected(PeerHandle peer, DisconnectReason reason) {
            _registry.ReleaseAllHeldBy(peer);
            _sessionPeers.Remove(peer);
        }

        /// <summary>The handle of the peer that just joined, whatever kind of peer it happens to be.</summary>
        private PeerHandle NewestPeer() {
            return _server.Peers[_server.Peers.Count - 1];
        }

        private void WaitForConnection(Func<bool> isConnected, string failureMessage) {
            for (int attempt = 0; attempt < 32 && !isConnected(); attempt++) {
                Pump(1);
            }

            Assert.True(isConnected(), failureMessage);
        }

        private static NetConfig BuildConfig() {
            return new NetConfig {
                GameProtocolName = "alpinelib-claim-test",
                Port = 1,
                MaxPeers = 8,
                ServerTickRate = 30,
                SnapshotRate = 15,
                ClientSendRate = 30
            };
        }
    }
}
