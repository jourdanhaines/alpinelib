using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Replication.Messages;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Spawning;
using AlpineLib.Netcode.Transport;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The spawner driven by a real <see cref="SessionHost"/> with real peers on the other end of a real
    /// framing path: who gets a body, when it is taken away, and what the rest of the session is told.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The membership events these react to are things a session decides, so nothing here stages them by
    /// hand — a test attaches and detaches peers and the host raises what it raises. That is what makes
    /// the departure cases meaningful: the host retires a member <em>before</em> announcing the
    /// departure, so a despawn keyed by owner peer id silently does nothing, and only a real host puts
    /// the event in that shape.
    /// </para>
    /// <para>
    /// Every member is a wire spy as well as a peer, because the failure that matters is not a stale
    /// entry in the server's registry — it is a body still standing in everybody else's lobby.
    /// </para>
    /// </remarks>
    public sealed class SessionPawnSpawnerTests {
        /// <summary>Prefab id the fixtures spawn as, distinct from zero so a defaulted field is visible.</summary>
        private const ushort PawnPrefabId = 3;

        private const float TickInterval = 1f / 30f;

        private static int nextPort = 45000;

        [Fact]
        public void AJoiningMemberIsGivenABody() {
            using var world = new SpawnWorld(AuthorityMode.Server);
            SpawnPeer member = world.Join("Owner");

            NetEntity pawn = Assert.Single(world.Replication.Entities.Entities);
            Assert.Equal(PawnPrefabId, pawn.PrefabId);
            Assert.Equal(member.ServerSidePeer.Id, pawn.OwnerPeerId);
            Assert.Equal(AuthorityMode.Server, pawn.Authority);
            Assert.Equal(EntityKind.Pawn, pawn.Kind);
            Assert.True(world.Spawner.TryGetPawn(member.PlayerId, out uint entityId));
            Assert.Equal(pawn.Id, entityId);
        }

        [Fact]
        public void AnOwnerSimulatedSessionSpawnsOwnerSimulatedPawns() {
            using var world = new SpawnWorld(AuthorityMode.OwnerClient);
            world.Join("Owner");

            NetEntity pawn = Assert.Single(world.Replication.Entities.Entities);
            Assert.Equal(AuthorityMode.OwnerClient, pawn.Authority);
        }

        [Fact]
        public void TheSpawnIsAnnouncedWithWhatItWas() {
            using var world = new SpawnWorld(AuthorityMode.Server);
            var announced = new List<uint>();
            world.Spawner.OnPawnSpawned += (member, pawn) => announced.Add(pawn.Id);

            world.Join("Owner");

            Assert.Equal(new[] { Assert.Single(world.Replication.Entities.Entities).Id }, announced);
        }

        [Fact]
        public void EveryMemberSeesTheNewBodyArrive() {
            using var world = new SpawnWorld(AuthorityMode.Server);
            SpawnPeer owner = world.Join("Owner");
            SpawnPeer guest = world.Join("Guest");
            world.Pump(2);

            // The owner was already in when the guest's pawn went out, so it hears about both.
            Assert.Equal(2, owner.Spawned.Count);
            Assert.Contains(guest.ServerSidePeer.Id, owner.Spawned);
        }

        [Fact]
        public void AGracefulLeaveTakesTheBodyWithIt() {
            using var world = new SpawnWorld(AuthorityMode.Server);
            SpawnPeer owner = world.Join("Owner");
            SpawnPeer guest = world.Join("Guest");
            world.Pump(2);
            uint guestPawnId = RequirePawn(world, guest);
            owner.ClearLog();

            world.Leave(guest, LeaveReason.Quit);
            world.Pump(2);

            // The host cleared the guest's peer id before announcing the departure, so a despawn keyed by
            // owner would have found nothing and left this body standing in the owner's lobby.
            Assert.Single(world.Replication.Entities.Entities);
            Assert.False(world.Spawner.TryGetPawn(guest.PlayerId, out _));
            Assert.Equal(new[] { guestPawnId }, owner.Despawned);
        }

        [Fact]
        public void ALostLinkTakesTheBodyWithItToo() {
            using var world = new SpawnWorld(AuthorityMode.Server);
            SpawnPeer owner = world.Join("Owner");
            SpawnPeer guest = world.Join("Guest");
            world.Pump(2);
            uint guestPawnId = RequirePawn(world, guest);
            owner.ClearLog();

            world.Leave(guest, LeaveReason.TransportLost);
            world.Pump(2);

            // The seat is reserved for a rejoin; the body is not.
            Assert.NotNull(world.Host.FindMember(guest.PlayerId));
            Assert.Single(world.Replication.Entities.Entities);
            Assert.False(world.Spawner.TryGetPawn(guest.PlayerId, out _));
            Assert.Equal(new[] { guestPawnId }, owner.Despawned);
        }

        [Fact]
        public void ARejoinGetsAFreshBodyUnderItsNewPeerId() {
            using var world = new SpawnWorld(AuthorityMode.Server);
            world.Join("Owner");
            SpawnPeer first = world.Join("Guest");
            uint firstPawnId = RequirePawn(world, first);

            world.Leave(first, LeaveReason.TransportLost);
            SpawnPeer second = world.Rejoin(first);

            Assert.True(world.Spawner.TryGetPawn(first.PlayerId, out uint rejoinedPawnId));
            Assert.NotEqual(firstPawnId, rejoinedPawnId);

            Assert.True(world.Replication.Entities.TryGet(rejoinedPawnId, out NetEntity pawn));
            Assert.Equal(second.ServerSidePeer.Id, pawn.OwnerPeerId);
            Assert.NotEqual(first.ServerSidePeer.Id, second.ServerSidePeer.Id);
        }

        [Fact]
        public void ARejoinNeverLeavesTwoBodiesBehind() {
            using var world = new SpawnWorld(AuthorityMode.Server);
            SpawnPeer guest = world.Join("Guest");

            // Both the departure and the arrival despawn what the player had, so a rejoin has two
            // chances to leave a second body behind.
            world.Leave(guest, LeaveReason.TransportLost);
            world.Rejoin(guest);

            Assert.Single(world.Replication.Entities.Entities);
        }

        [Fact]
        public void AJoiningMemberIsSentTheWorldInFull() {
            using var world = new SpawnWorld(AuthorityMode.Server);
            world.Join("Owner");
            SpawnPeer guest = world.Join("Guest");
            world.Pump(2);

            SnapshotKeyframe keyframe = Assert.Single(guest.Keyframes);

            // Both bodies, the owner's included: a newcomer's world starts empty and the keyframe is what
            // fills it.
            Assert.Equal(2, keyframe.Records.Count);
        }

        [Fact]
        public void ADisposedSpawnerStopsReactingToTheSession() {
            using var world = new SpawnWorld(AuthorityMode.Server);
            SpawnPeer owner = world.Join("Owner");
            world.Pump(2);
            uint ownerPawnId = RequirePawn(world, owner);

            world.Spawner.Dispose();
            SpawnPeer guest = world.Join("Guest");
            world.Leave(owner, LeaveReason.Quit);
            world.Pump(2);

            // Nothing spawned, nothing despawned, and the bookkeeping is empty — but the body that was
            // already there is left alone, because disposal is a teardown and not a purge.
            Assert.Equal(ownerPawnId, Assert.Single(world.Replication.Entities.Entities).Id);
            Assert.False(world.Spawner.TryGetPawn(owner.PlayerId, out _));
            Assert.False(world.Spawner.TryGetPawn(guest.PlayerId, out _));
        }

        [Fact]
        public void DisposingTwiceIsHarmless() {
            using var world = new SpawnWorld(AuthorityMode.Server);
            world.Spawner.Dispose();
            world.Spawner.Dispose();
        }

        [Fact]
        public void APlayerWhoNeverJoinedHasNoBody() {
            using var world = new SpawnWorld(AuthorityMode.Server);

            Assert.False(world.Spawner.TryGetPawn(PlayerId.NewId(), out uint entityId));
            Assert.Equal(0u, entityId);
        }

        [Fact]
        public void TheSpawnerNeedsASessionAWorldAndAPlacement() {
            using var world = new SpawnWorld(AuthorityMode.Server);

            Assert.Throws<ArgumentNullException>(() => new SessionPawnSpawner(
                null, world.Replication, 0, AuthorityMode.Server, new RingSpawnPlacement()));
            Assert.Throws<ArgumentNullException>(() => new SessionPawnSpawner(
                world.Host, null, 0, AuthorityMode.Server, new RingSpawnPlacement()));
            Assert.Throws<ArgumentNullException>(() => new SessionPawnSpawner(
                world.Host, world.Replication, 0, AuthorityMode.Server, null));
        }

        [Fact]
        public void EachArrivalIsPlacedWhereThePlacementSaid() {
            var points = new[] {
                new SpawnPoint(new Vector3(5f, 0f, 0f)),
                new SpawnPoint(new Vector3(-5f, 0f, 0f))
            };

            using var world = new SpawnWorld(AuthorityMode.Server, new ListSpawnPlacement(points));
            SpawnPeer first = world.Join("First");
            SpawnPeer second = world.Join("Second");

            Assert.Equal(5f, PawnOf(world, first).State.Position.X, 4);
            Assert.Equal(-5f, PawnOf(world, second).State.Position.X, 4);
        }

        private static uint RequirePawn(SpawnWorld world, SpawnPeer peer) {
            Assert.True(world.Spawner.TryGetPawn(peer.PlayerId, out uint entityId));
            return entityId;
        }

        private static NetEntity PawnOf(SpawnWorld world, SpawnPeer peer) {
            Assert.True(world.Replication.Entities.TryGet(RequirePawn(world, peer), out NetEntity pawn));
            return pawn;
        }

        private static NetConfig BuildConfig(int port) {
            return new NetConfig {
                GameProtocolName = "alpinelib-spawn-test",
                Port = port,
                MaxPeers = 8,
                ServerTickRate = 30,
                SnapshotRate = 15,
                ClientSendRate = 30
            };
        }

        private static SessionConfigData BuildSessionConfig() {
            return new SessionConfigData {
                Profile = new SessionProfileData { RejoinPolicy = RejoinPolicy.AnyTime },
                Lobby = new LobbyConfigData()
            };
        }

        /// <summary>
        /// One session with a spawner on it and real connections underneath: the smallest thing that can
        /// raise the membership events the spawner exists to answer.
        /// </summary>
        private sealed class SpawnWorld : IDisposable {
            private readonly int _port;
            private readonly FakeNetTransport _serverTransport = new FakeNetTransport();
            private readonly NetServer _server;
            private readonly List<SpawnPeer> _peers = new List<SpawnPeer>();

            public SpawnWorld(AuthorityMode authority, ISpawnPlacement placement = null) {
                _port = Interlocked.Increment(ref nextPort);
                NetConfig config = BuildConfig(_port);

                _server = new NetServer(_serverTransport, config);
                Host = new SessionHost("spawn-test", "CODE", BuildSessionConfig(), _server);
                Replication = new ServerReplication(_server, () => Host.ConnectedPeers, new MovementValidator(config));
                Spawner = new SessionPawnSpawner(
                    Host, Replication, PawnPrefabId, authority, placement ?? new RingSpawnPlacement());

                _server.Start();
                Host.Open();
            }

            /// <summary>The session whose roster drives the spawner.</summary>
            public SessionHost Host { get; }

            /// <summary>The world the bodies live in.</summary>
            public ServerReplication Replication { get; }

            /// <summary>The spawner under test.</summary>
            public SessionPawnSpawner Spawner { get; }

            /// <summary>Dials a peer, waits for the link, and puts it in the session under a new identity.</summary>
            public SpawnPeer Join(string displayName) {
                SpawnPeer peer = Connect();
                peer.BindPlayer(PlayerId.NewId(), displayName);
                Attach(peer);
                return peer;
            }

            /// <summary>Dials a second connection for a player whose seat is still reserved.</summary>
            public SpawnPeer Rejoin(SpawnPeer previous) {
                SpawnPeer peer = Connect();
                peer.BindPlayer(previous.PlayerId, previous.DisplayName);
                Attach(peer);
                return peer;
            }

            /// <summary>Releases a peer the way the front desk does when the session is finished with it.</summary>
            public void Leave(SpawnPeer peer, LeaveReason reason) {
                Host.DetachPeer(peer.ServerSidePeer, reason);
            }

            /// <summary>Advances the server and every attached peer by whole ticks.</summary>
            public void Pump(int ticks) {
                for (int tick = 0; tick < ticks; tick++) {
                    _server.Update(TickInterval);

                    for (int peerIndex = 0; peerIndex < _peers.Count; peerIndex++) {
                        _peers[peerIndex].Update(TickInterval);
                    }
                }
            }

            public void Dispose() {
                for (int peerIndex = 0; peerIndex < _peers.Count; peerIndex++) {
                    _peers[peerIndex].Dispose();
                }

                Spawner.Dispose();
                _server.Dispose();
                _serverTransport.Dispose();
            }

            private SpawnPeer Connect() {
                var peer = new SpawnPeer(BuildConfig(_port));
                _peers.Add(peer);
                peer.Connect(_port);

                for (int attempt = 0; attempt < 32 && !peer.IsConnected; attempt++) {
                    Pump(1);
                }

                Assert.True(peer.IsConnected, "The spawn-test peer never finished connecting.");
                peer.BindPeerId(_server.Peers[_server.Peers.Count - 1]);
                peer.ClearLog();
                return peer;
            }

            private void Attach(SpawnPeer peer) {
                SessionAttachResult result = Host.AttachPeer(
                    peer.ServerSidePeer, new PlayerIdentity(peer.PlayerId, peer.DisplayName, AuthMethod.Anonymous));

                Assert.True(result.IsAccepted, "The spawn-test peer was refused a seat: " + result.DenialReason);
            }
        }

        /// <summary>
        /// One member's end of the session: a real connection, and a log of the replication traffic the
        /// server actually sent it.
        /// </summary>
        private sealed class SpawnPeer : IDisposable {
            private readonly FakeNetTransport _transport = new FakeNetTransport();
            private readonly NetClient _client;
            private readonly List<int> _spawned = new List<int>();
            private readonly List<uint> _despawned = new List<uint>();
            private readonly List<SnapshotKeyframe> _keyframes = new List<SnapshotKeyframe>();

            public SpawnPeer(NetConfig config) {
                _client = new NetClient(_transport, config);
                _client.Router.Register<SpawnEntity>(ReplicationMessageIds.SpawnEntity, RecordSpawn);
                _client.Router.Register<DespawnEntity>(ReplicationMessageIds.DespawnEntity, RecordDespawn);
                _client.Router.Register<SnapshotKeyframe>(ReplicationMessageIds.SnapshotKeyframe, RecordKeyframe);
            }

            /// <summary>How the server addresses this peer.</summary>
            public PeerHandle ServerSidePeer { get; private set; } = PeerHandle.None;

            /// <summary>The identity this peer attaches under.</summary>
            public PlayerId PlayerId { get; private set; } = PlayerId.None;

            /// <summary>The name this peer attaches under.</summary>
            public string DisplayName { get; private set; } = string.Empty;

            /// <summary>True once the dial has completed.</summary>
            public bool IsConnected => _client.IsConnected;

            /// <summary>Owner peer id of every body this peer was told about, in arrival order.</summary>
            public IReadOnlyList<int> Spawned => _spawned;

            /// <summary>Entity id of every body this peer was told had gone, in arrival order.</summary>
            public IReadOnlyList<uint> Despawned => _despawned;

            /// <summary>Every keyframe this peer was sent, in arrival order.</summary>
            public IReadOnlyList<SnapshotKeyframe> Keyframes => _keyframes;

            public void Connect(int port) {
                _client.Connect(NetEndpoint.Direct("127.0.0.1", port));
            }

            /// <summary>Remembers which peer the server thinks this is, so a test can address it by hand.</summary>
            public void BindPeerId(PeerHandle serverSidePeer) {
                ServerSidePeer = serverSidePeer;
            }

            /// <summary>Chooses the identity this peer will claim when it attaches.</summary>
            public void BindPlayer(PlayerId playerId, string displayName) {
                PlayerId = playerId;
                DisplayName = displayName;
            }

            public void Update(float deltaSeconds) {
                _client.Update(deltaSeconds);
            }

            /// <summary>Forgets every recorded message, so a test can assert on one phase at a time.</summary>
            public void ClearLog() {
                _spawned.Clear();
                _despawned.Clear();
                _keyframes.Clear();
            }

            public void Dispose() {
                _client.Dispose();
                _transport.Dispose();
            }

            private void RecordSpawn(in SpawnEntity message, PeerHandle sender) {
                _spawned.Add(message.OwnerPeerId);
            }

            private void RecordDespawn(in DespawnEntity message, PeerHandle sender) {
                _despawned.Add(message.EntityId);
            }

            private void RecordKeyframe(in SnapshotKeyframe message, PeerHandle sender) {
                _keyframes.Add(message);
            }
        }
    }
}
