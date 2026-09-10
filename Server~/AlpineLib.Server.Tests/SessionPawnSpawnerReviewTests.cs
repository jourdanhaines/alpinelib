using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Spawning;
using AlpineLib.Netcode.Transport;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Adversarial probes at the spawner: the join paths nothing drives it through, and what a
    /// game-supplied placement can do to the session that raised the event.
    /// </summary>
    /// <remarks>
    /// Kept separate from <c>SessionPawnSpawnerTests</c> because every fact here is a join path nothing
    /// else drives. No client is dialled — the host only needs peer handles to address, and what is
    /// asserted here is server-side bookkeeping, not wire traffic.
    /// </remarks>
    public sealed class SessionPawnSpawnerReviewTests {
        private const ushort PawnPrefabId = 3;

        private static int nextPort = 46500;

        /// <summary>The seat came back and so did the keyframe, and the keyframe carries the new body.</summary>
        [Fact]
        public void ARejoinIsToldAboutItsOwnNewBodyInTheKeyframe() {
            using var fixture = new SpawnFixture();
            var keyframedPawnCounts = new List<int>();
            fixture.Host.OnMemberNeedsKeyframe += member => keyframedPawnCounts.Add(fixture.Replication.Entities.Entities.Count);

            PlayerId player = fixture.Attach(new PeerHandle(1), "Guest");
            fixture.Host.DetachPeer(new PeerHandle(1), LeaveReason.TransportLost);
            fixture.Attach(new PeerHandle(2), "Guest", player);

            // Both the fresh join and the rejoin must see a world that already contains the arrival's
            // own pawn, or the client's first frame is a lobby it is not standing in.
            Assert.Equal(new[] { 1, 1 }, keyframedPawnCounts);
        }

        /// <summary>A second connection under a player id already in the session is refused, body and all.</summary>
        [Fact]
        public void ASecondConnectionForAConnectedPlayerLeavesTheFirstBodyAlone() {
            using var fixture = new SpawnFixture();
            PlayerId player = fixture.Attach(new PeerHandle(1), "Guest");
            uint firstPawn = RequirePawn(fixture, player);

            SessionAttachResult second = fixture.Host.AttachPeer(
                new PeerHandle(2), new PlayerIdentity(player, "Guest", AuthMethod.Anonymous));

            Assert.False(second.IsAccepted);
            Assert.Equal(firstPawn, Assert.Single(fixture.Replication.Entities.Entities).Id);
            Assert.True(fixture.Spawner.TryGetPawn(player, out uint stillThere));
            Assert.Equal(firstPawn, stillThere);
        }

        /// <summary>
        /// A placement that answers in a carrier's frame under server authority. Replication refuses such
        /// a spawn, and the throw would escape the membership event with the member already seated and
        /// announced, so the spawner pins the answer to world space rather than taking the join down.
        /// </summary>
        [Fact]
        public void APlacementAnsweringInACarrierFrameDoesNotBreakTheJoin() {
            using var fixture = new SpawnFixture(new CarrierFramePlacement());

            Exception thrown = Record.Exception(() => fixture.Attach(new PeerHandle(1), "Guest"));

            // The damage a throw would do: the member is seated and every peer was told it arrived, but
            // the caller never got an accepted result, so nothing sent it a world.
            Assert.Null(thrown);
            Assert.Single(fixture.Replication.Entities.Entities);
            Assert.NotNull(fixture.Host.FindMemberByPeer(new PeerHandle(1)));
        }

        /// <summary>
        /// The pinning is a relabelling: the pawn spawns in world space, at the numbers given. The spawner
        /// counts it, because nothing else can tell that answer apart from one meant in world space and a
        /// game whose train-car spawn silently became a spawn beside the track deserves the trace.
        /// </summary>
        [Fact]
        public void AServerSimulatedPawnIsPinnedToWorldSpace() {
            using var fixture = new SpawnFixture(new CarrierFramePlacement());

            fixture.Attach(new PeerHandle(1), "Guest");

            NetEntity pawn = Assert.Single(fixture.Replication.Entities.Entities);
            Assert.Equal(PawnState.WorldCarrierId, pawn.State.CarrierId);
            Assert.Equal(CarrierFramePlacement.Place, pawn.State.Position);
            Assert.Equal(1, fixture.Spawner.CoercedCarrierFrames);
        }

        /// <summary>
        /// The pinning is only for server authority. An owner-simulated pawn is allowed a carrier's frame
        /// and must keep the one the placement asked for, or a spawn authored on a moving train car is
        /// silently reinterpreted as a spawn beside the track.
        /// </summary>
        [Fact]
        public void AnOwnerSimulatedPawnKeepsTheCarrierFrameItWasGiven() {
            using var fixture = new SpawnFixture(new CarrierFramePlacement(), authority: AuthorityMode.OwnerClient);

            fixture.Attach(new PeerHandle(1), "Guest");

            NetEntity pawn = Assert.Single(fixture.Replication.Entities.Entities);
            Assert.Equal(CarrierFramePlacement.CarrierId, pawn.State.CarrierId);
            Assert.Equal(0, fixture.Spawner.CoercedCarrierFrames);
        }

        /// <summary>
        /// Disposal from a co-subscriber of the same event. Removing a handler during a raise does not
        /// unhook it from the raise already in flight, so the spawner checks the flag as well and hands
        /// out no body into a world that is about to be discarded.
        /// </summary>
        [Fact]
        public void ASpawnerDisposedWhileTheJoinIsBeingRaisedGivesOutNoBody() {
            using var fixture = new SpawnFixture(subscribeFirst: true);

            fixture.Attach(new PeerHandle(1), "Guest");

            Assert.Empty(fixture.Replication.Entities.Entities);
        }

        /// <summary>
        /// Disposal from inside the replication spawn event, which fires before the spawner records the
        /// entity. The record must not land in a map <c>Dispose</c> has just cleared, or a spawner that is
        /// unsubscribed from everything reports a live body for a player forever.
        /// </summary>
        [Fact]
        public void ADisposedSpawnerRemembersNoPawns() {
            using var fixture = new SpawnFixture();
            fixture.Replication.OnEntitySpawned += entity => fixture.Spawner.Dispose();

            PlayerId player = fixture.Attach(new PeerHandle(1), "Guest");

            Assert.False(fixture.Spawner.TryGetPawn(player, out _));
        }

        /// <summary>A kick retires the member outright, and the body goes with the seat.</summary>
        [Fact]
        public void AKickTakesTheBodyWithIt() {
            using var fixture = new SpawnFixture();
            PlayerId player = fixture.Attach(new PeerHandle(1), "Guest");
            Assert.Single(fixture.Replication.Entities.Entities);

            fixture.Host.Kick(player, "Reviewed out.");

            Assert.Empty(fixture.Replication.Entities.Entities);
            Assert.False(fixture.Spawner.TryGetPawn(player, out _));
        }

        /// <summary>
        /// The join a carrier-frame answer used to abort now completes end to end: the attach is accepted,
        /// the member is seated and announced, and it has a body to be told about in its keyframe.
        /// </summary>
        [Fact]
        public void ACarrierFrameAnswerStillCompletesTheJoin() {
            using var fixture = new SpawnFixture(new CarrierFramePlacement());

            SessionAttachResult result = fixture.Host.AttachPeer(
                new PeerHandle(1), new PlayerIdentity(PlayerId.NewId(), "Guest", AuthMethod.Anonymous));

            Assert.True(result.IsAccepted);
            Assert.NotNull(fixture.Host.FindMemberByPeer(new PeerHandle(1)));
            Assert.Single(fixture.Replication.Entities.Entities);
        }

        private static uint RequirePawn(SpawnFixture fixture, PlayerId player) {
            Assert.True(fixture.Spawner.TryGetPawn(player, out uint entityId));
            return entityId;
        }

        /// <summary>A placement that puts its pawn in a carrier's local frame — what a game with movers wants.</summary>
        private sealed class CarrierFramePlacement : ISpawnPlacement {
            /// <summary>The carrier the answer is expressed in.</summary>
            public const ushort CarrierId = 12;

            /// <summary>Where in that carrier's frame the pawn stands, so the pinning can be shown to relabel only.</summary>
            public static readonly Vector3 Place = new Vector3(0.5f, 0f, -1.25f);

            public PawnState NextSpawnState(SessionMember member, bool isRejoin, CollisionWorld world, uint serverTick) {
                byte flags = PawnState.PackFlags(WireLocomotion.Walk, false, true);
                return new PawnState(Place, 0f, Vector3.Zero, flags, CarrierId);
            }
        }

        /// <summary>A session with a spawner on it and no clients: the host only ever addresses handles.</summary>
        private sealed class SpawnFixture : IDisposable {
            private readonly FakeNetTransport _transport = new FakeNetTransport();
            private readonly NetServer _server;

            public SpawnFixture(
                ISpawnPlacement placement = null,
                bool subscribeFirst = false,
                AuthorityMode authority = AuthorityMode.Server) {
                int port = Interlocked.Increment(ref nextPort);
                NetConfig config = new NetConfig {
                    GameProtocolName = "alpinelib-spawn-review",
                    Port = port,
                    MaxPeers = 8,
                    ServerTickRate = 30,
                    SnapshotRate = 15,
                    ClientSendRate = 30
                };

                _server = new NetServer(_transport, config);
                Host = new SessionHost("spawn-review", "CODE", BuildSessionConfig(), _server);
                Replication = new ServerReplication(_server, () => Host.ConnectedPeers, new MovementValidator(config));

                if (subscribeFirst) {
                    Host.OnMemberJoined += DisposeSpawner;
                }

                Spawner = new SessionPawnSpawner(
                    Host, Replication, PawnPrefabId, authority, placement ?? new RingSpawnPlacement());

                Host.Open();
            }

            public SessionHost Host { get; }

            public ServerReplication Replication { get; }

            public SessionPawnSpawner Spawner { get; }

            /// <summary>Seats a peer under a fresh player id, or under one that already held a seat.</summary>
            public PlayerId Attach(PeerHandle peer, string displayName, PlayerId player = default) {
                PlayerId identityId = player.IsValid ? player : PlayerId.NewId();
                SessionAttachResult result = Host.AttachPeer(
                    peer, new PlayerIdentity(identityId, displayName, AuthMethod.Anonymous));

                Assert.True(result.IsAccepted, "The review peer was refused a seat: " + result.DenialReason);
                return identityId;
            }

            public void Dispose() {
                Spawner.Dispose();
                _server.Dispose();
                _transport.Dispose();
            }

            private void DisposeSpawner(SessionMember member, bool isRejoin) {
                Spawner.Dispose();
            }

            private static SessionConfigData BuildSessionConfig() {
                return new SessionConfigData {
                    Profile = new SessionProfileData { RejoinPolicy = RejoinPolicy.AnyTime },
                    Lobby = new LobbyConfigData()
                };
            }
        }
    }
}
