using System;
using System.Collections.Generic;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Transport;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// One replication world with real clients on the other end of a real framing path, so a carrier id
    /// can be followed from an owner's send all the way to a bystander's render sample.
    /// </summary>
    /// <remarks>
    /// The claim fixtures next door prove a message reaches a peer; this one has to prove a field
    /// survives four hops — owner update, validator, entity state, snapshot — and the only honest way to
    /// show that is to run all four over the same transport the game does.
    /// </remarks>
    internal sealed class CarrierReplicationLoopbackWorld : IDisposable {
        /// <summary>The prefab every pawn in these tests spawns as; profile zero in the config below.</summary>
        public const ushort PawnPrefab = 0;

        private const float TickInterval = 1f / 30f;

        private readonly SessionLoopbackNetwork _network = new SessionLoopbackNetwork();
        private readonly SessionLoopbackTransport _serverTransport;
        private readonly NetServer _server;
        private readonly ServerReplication _replication;
        private readonly List<PeerHandle> _sessionPeers = new List<PeerHandle>();
        private readonly List<CarrierReplicationLoopbackClient> _clients =
            new List<CarrierReplicationLoopbackClient>();

        private uint _serverTick;

        /// <summary>Stands the session up, attaches the replication ids and starts the server.</summary>
        public CarrierReplicationLoopbackWorld() {
            _serverTransport = _network.CreateServerTransport();
            _server = new NetServer(_serverTransport, BuildConfig());
            _replication = new ServerReplication(_server, () => _sessionPeers, new MovementValidator(BuildConfig()));
            _replication.AttachToRouter();
            _server.Start();
        }

        /// <summary>The authority every client in this world talks to.</summary>
        public ServerReplication Replication => _replication;

        /// <summary>Every connected client, in the order they joined.</summary>
        public IReadOnlyList<CarrierReplicationLoopbackClient> Clients => _clients;

        /// <summary>Dials a client, waits for the link, and puts it on the session roster.</summary>
        public CarrierReplicationLoopbackClient ConnectClient() {
            var client = new CarrierReplicationLoopbackClient(_network.CreateClientTransport(), BuildConfig());
            _clients.Add(client);
            client.Connect();
            WaitForConnection(client);
            client.BindPeerId(_server.Peers[_server.Peers.Count - 1]);
            _sessionPeers.Add(client.ServerSidePeer);
            return client;
        }

        /// <summary>Advances the server, its world and every attached client by whole ticks.</summary>
        public void Pump(int ticks) {
            for (int tick = 0; tick < ticks; tick++) {
                _server.Update(TickInterval);
                _serverTick++;
                _replication.Tick(_serverTick, TickInterval);

                for (int clientIndex = 0; clientIndex < _clients.Count; clientIndex++) {
                    _clients[clientIndex].Update(TickInterval);
                }
            }
        }

        /// <inheritdoc />
        public void Dispose() {
            for (int clientIndex = 0; clientIndex < _clients.Count; clientIndex++) {
                _clients[clientIndex].Dispose();
            }

            _replication.DetachFromRouter();
            _server.Dispose();
            _serverTransport.Dispose();
        }

        private void WaitForConnection(CarrierReplicationLoopbackClient client) {
            for (int attempt = 0; attempt < 32 && !client.IsConnected; attempt++) {
                Pump(1);
            }

            Assert.True(client.IsConnected, "The loopback replication client never finished connecting.");
        }

        private static NetConfig BuildConfig() {
            return new NetConfig {
                GameProtocolName = "alpinelib-carrier-test",
                Port = 1,
                MaxPeers = 8,
                ServerTickRate = 30,
                SnapshotRate = 15,
                ClientSendRate = 30,
                MovementToleranceMultiplier = 1.5f,
                MovementProfiles = new[] {
                    new MovementProfile {
                        DisplayName = "pawn",
                        WalkSlowSpeed = 0.8f,
                        WalkSpeed = 2f,
                        JogSpeed = 3.5f,
                        SprintSpeed = 5.5f,
                        CrouchSpeed = 1.2f,
                        CrouchFastSpeed = 2.2f
                    }
                }
            };
        }
    }
}
