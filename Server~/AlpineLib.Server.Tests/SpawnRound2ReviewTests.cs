using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Claims;
using AlpineLib.Netcode.Sessions.Spawning;
using AlpineLib.Netcode.Transport;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Round-two probes: how far the bounded overflow ring can carry a pawn from the marker it was
    /// authored on, and whether a newcomer is told the world exists before it is told who holds what in it.
    /// </summary>
    public sealed class SpawnRound2ReviewTests {
        private const uint AnyTick = 7u;

        /// <summary>Two pawns overlap once their centres are closer than two capsule radii.</summary>
        private const float PawnDiameterMetres = 2f * 0.35f;

        /// <summary>How far the outermost overflow seat sits from its marker.</summary>
        private const float RingReachMetres =
            ListSpawnPlacement.OverflowRadiusMetres * ListSpawnPlacement.OverflowRevolutions;

        private static int nextPort = 47500;

        /// <summary>
        /// A hand-authored row of markers one overflow radius apart, filled well past the list. Every
        /// arrival gets a place of its own, and because each marker turns its ring one seat further round
        /// than its neighbour, two arrivals in a row lean a quarter turn apart rather than an eighth.
        /// </summary>
        [Fact]
        public void AdjacentMarkersLeanTheirOverflowArrivalsAQuarterTurnApart() {
            SpawnPoint[] points = RowOfThree();
            var placement = new ListSpawnPlacement(points);
            var seen = new List<Vector3>();

            for (int arrival = 0; arrival < 40; arrival++) {
                seen.Add(NextPosition(placement));
            }

            Assert.Equal(40, CountDistinct(seen));

            // Arrivals 3 and 4 are the first overflow seats of markers 0 and 1. One seat of turn comes
            // from the arrival ordinal and one from the marker index, so the leans are perpendicular.
            Vector3 firstLean = Vector3.Normalize(seen[3] - points[0].Position);
            Vector3 secondLean = Vector3.Normalize(seen[4] - points[1].Position);

            Assert.True(
                Vector3.Dot(firstLean, secondLean) <= 0.001f,
                $"neighbouring markers leaned less than a quarter turn apart: {firstLean} and {secondLean}");
        }

        /// <summary>
        /// The separation the placement does <i>not</i> promise. Overflow seats are seeded from the
        /// arrival ordinal and the marker index and never look at where the markers actually are, so two
        /// arrivals belonging to different markers of the same row stand closer than two pawn capsules
        /// fit. Pinned as a characterisation because the remarks say so: a scene that needs a guaranteed
        /// gap authors more points rather than expecting the ring to find one.
        /// </summary>
        [Fact]
        public void NothingSpacesOverflowSeatsBelongingToDifferentMarkers() {
            var placement = new ListSpawnPlacement(RowOfThree());
            var seen = new List<Vector3>();

            for (int arrival = 0; arrival < 40; arrival++) {
                seen.Add(NextPosition(placement));
            }

            (int first, int second, float distance) closest = ClosestPair(seen);

            Assert.True(
                closest.distance > 0f,
                $"arrivals {closest.first} and {closest.second} shared a place outright");

            Assert.True(
                closest.distance < PawnDiameterMetres,
                "the ring now spaces neighbouring markers' overflow seats a pawn width apart — say so in " +
                $"ListSpawnPlacement's remarks (closest pair {closest.distance:0.0000} m)");
        }

        /// <summary>
        /// One authored marker in the middle of a platform four metres above the ground, filled by a lobby
        /// with churn. The ring stops widening after <see cref="ListSpawnPlacement.OverflowRevolutions"/>
        /// revolutions, so no arrival is carried off the surface the marker was authored on and dropped to
        /// the ground below it. The platform is sized to the ring's reach, which is what a marker asks of
        /// the scene around it.
        /// </summary>
        [Fact]
        public void TheOverflowRingStaysOnTheAuthoredFloor() {
            CollisionWorld world = PlatformOverGround(platformHalfExtentMetres: 3.5f, platformTopMetres: 4f);
            var marker = new Vector3(0f, 4f, 0f);
            var placement = new ListSpawnPlacement(new[] { new SpawnPoint(marker) });

            var fellOff = new List<int>();

            for (int arrival = 0; arrival < 40; arrival++) {
                PawnState state = placement.NextSpawnState(Member(), false, world, AnyTick);

                if (MathF.Abs(state.Position.Y - 4f) > 0.01f) {
                    fellOff.Add(arrival);
                }

                Assert.True(
                    HorizontalDistance(state.Position, marker) <= RingReachMetres + 0.001f,
                    $"arrival {arrival} was seated past the ring's reach, at {state.Position}");
            }

            Assert.True(
                fellOff.Count == 0,
                $"{fellOff.Count} of 40 arrivals were seated off the platform the marker sits on " +
                $"(first at arrival {(fellOff.Count == 0 ? -1 : fellOff[0])}), four metres below it");
        }

        /// <summary>
        /// The ring is bounded because it starts over: a marker holds
        /// <c>OverflowSeats * OverflowRevolutions</c> overflow places, and the arrival after those is back
        /// on the first of them. Sharing a place with somebody who has most likely left beats standing off
        /// the platform the marker was authored on.
        /// </summary>
        [Fact]
        public void AMarkersOverflowSeatsStartOverOnceTheRingHasWidened() {
            var placement = new ListSpawnPlacement(new[] { new SpawnPoint(Vector3.Zero) });
            var seen = new List<Vector3>();

            int overflowPlaces = ListSpawnPlacement.OverflowSeats * ListSpawnPlacement.OverflowRevolutions;

            for (int arrival = 0; arrival <= overflowPlaces + 1; arrival++) {
                seen.Add(NextPosition(placement));
            }

            // The authored point and every overflow place, then the ring hands out its first seat again.
            Assert.Equal(overflowPlaces + 1, CountDistinct(seen));
            Assert.Equal(seen[1], seen[overflowPlaces + 1]);
        }

        /// <summary>
        /// One player, one authored marker, and a flaky link. Every rejoin is a fresh arrival and burns the
        /// next overflow seat, so a player who drops nine times stands at the widest seat the ring has —
        /// still on the platform the marker was authored on, not four metres below it.
        /// </summary>
        [Fact]
        public void AReconnectingPlayerStaysOnTheAuthoredMarkersPlatform() {
            CollisionWorld world = PlatformOverGround(platformHalfExtentMetres: 3.5f, platformTopMetres: 4f);
            var placement = new ListSpawnPlacement(new[] { new SpawnPoint(new Vector3(0f, 4f, 0f)) });

            using var fixture = new RejoinFixture(placement, world);
            PlayerId player = fixture.Attach(new PeerHandle(1), "Flaky");

            for (int reconnect = 1; reconnect <= 9; reconnect++) {
                fixture.Host.DetachPeer(new PeerHandle(reconnect), LeaveReason.TransportLost);
                fixture.Attach(new PeerHandle(reconnect + 1), "Flaky", player);
            }

            PawnState state = Assert.Single(fixture.Replication.Entities.Entities).State;

            Assert.Equal(4f, state.Position.Y, 2);
        }

        /// <summary>
        /// The desk's subscription order, reproduced: the spawner is built first, the claim keyframe hooks
        /// after it. A newcomer must see the world in full before it hears about a held slot.
        /// </summary>
        [Fact]
        public void TheWorldKeyframeReachesANewcomerBeforeTheClaimKeyframe() {
            using var fixture = new DeskOrderFixture();

            PeerHandle holder = new PeerHandle(1);
            fixture.Attach(holder, "Holder");
            Assert.True(fixture.Claims.TryClaim(3, holder));

            PeerHandle newcomer = new PeerHandle(2);
            fixture.Recorder.Clear();
            fixture.Attach(newcomer, "Newcomer");

            IReadOnlyList<ushort> toNewcomer = fixture.Recorder.MessageIdsFor(newcomer);
            int keyframeIndex = FirstIndexOf(toNewcomer, 131);
            int claimIndex = FirstIndexOf(toNewcomer, 86);

            Assert.True(keyframeIndex >= 0, "no world keyframe reached the newcomer");
            Assert.True(claimIndex >= 0, "no claim keyframe reached the newcomer");
            Assert.True(
                keyframeIndex < claimIndex,
                $"claim keyframe at {claimIndex} arrived before the world keyframe at {keyframeIndex}: " +
                string.Join(", ", toNewcomer));
        }

        /// <summary>
        /// The desk sends the world once. The spawner answers OnMemberNeedsKeyframe from inside the
        /// attach, ordered against the pawn spawn, so a second send from the desk afterwards would only
        /// repeat the largest message of a join.
        /// </summary>
        [Fact]
        public void ANewcomerIsSentTheWholeWorldOnce() {
            using var fixture = new DeskOrderFixture();

            PeerHandle newcomer = new PeerHandle(1);
            fixture.Recorder.Clear();
            fixture.Attach(newcomer, "Newcomer");

            IReadOnlyList<ushort> toNewcomer = fixture.Recorder.MessageIdsFor(newcomer);
            int keyframes = 0;

            for (int index = 0; index < toNewcomer.Count; index++) {
                if (toNewcomer[index] == 131) {
                    keyframes++;
                }
            }

            Assert.Equal(1, keyframes);
        }

        /// <summary>A hand-authored row of markers one overflow radius apart along +X.</summary>
        private static SpawnPoint[] RowOfThree() {
            return new[] {
                new SpawnPoint(new Vector3(0f, 0f, 0f)),
                new SpawnPoint(new Vector3(ListSpawnPlacement.OverflowRadiusMetres, 0f, 0f)),
                new SpawnPoint(new Vector3(2f * ListSpawnPlacement.OverflowRadiusMetres, 0f, 0f))
            };
        }

        private static int CountDistinct(IReadOnlyList<Vector3> positions) {
            var distinct = new HashSet<Vector3>();

            for (int index = 0; index < positions.Count; index++) {
                distinct.Add(positions[index]);
            }

            return distinct.Count;
        }

        /// <summary>How far a seat is from its marker on the ground plane, which is where the ring turns.</summary>
        private static float HorizontalDistance(Vector3 left, Vector3 right) {
            float deltaX = left.X - right.X;
            float deltaZ = left.Z - right.Z;

            return MathF.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        }

        private static int FirstIndexOf(IReadOnlyList<ushort> messageIds, ushort wanted) {
            for (int index = 0; index < messageIds.Count; index++) {
                if (messageIds[index] == wanted) {
                    return index;
                }
            }

            return -1;
        }

        private static (int first, int second, float distance) ClosestPair(IReadOnlyList<Vector3> positions) {
            (int first, int second, float distance) closest = (-1, -1, float.MaxValue);

            for (int left = 0; left < positions.Count; left++) {
                for (int right = left + 1; right < positions.Count; right++) {
                    float distance = Vector3.Distance(positions[left], positions[right]);

                    if (distance < closest.distance) {
                        closest = (left, right, distance);
                    }
                }
            }

            return closest;
        }

        private static CollisionWorld PlatformOverGround(float platformHalfExtentMetres, float platformTopMetres) {
            CollisionShape ground = CollisionShape.MakeBox(
                new Vector3(0f, -0.5f, 0f),
                Vector3.UnitX,
                Vector3.UnitY,
                Vector3.UnitZ,
                new Vector3(60f, 0.5f, 60f));

            CollisionShape platform = CollisionShape.MakeBox(
                new Vector3(0f, platformTopMetres - 0.5f, 0f),
                Vector3.UnitX,
                Vector3.UnitY,
                Vector3.UnitZ,
                new Vector3(platformHalfExtentMetres, 0.5f, platformHalfExtentMetres));

            var geometry = new SceneGeometry(
                "Round2Platform", 0u, new[] { ground, platform }, Array.Empty<MoverDefinition>());
            return new CollisionWorld(geometry, CollisionWorld.DefaultTickIntervalSeconds);
        }

        private static Vector3 NextPosition(ISpawnPlacement placement) {
            return placement.NextSpawnState(Member(), false, null, AnyTick).Position;
        }

        private static SessionMember Member() {
            return new SessionMember(1, PlayerId.NewId(), "Reviewer", false, 0);
        }

        /// <summary>A session with a spawner on it, driven through drop-and-rejoin cycles.</summary>
        private sealed class RejoinFixture : IDisposable {
            private readonly FakeNetTransport _transport = new FakeNetTransport();
            private readonly NetServer _server;
            private readonly SessionPawnSpawner _spawner;

            public RejoinFixture(ISpawnPlacement placement, CollisionWorld world) {
                int port = Interlocked.Increment(ref nextPort);
                NetConfig config = new NetConfig {
                    GameProtocolName = "alpinelib-rejoin-review",
                    Port = port,
                    MaxPeers = 8,
                    ServerTickRate = 30,
                    SnapshotRate = 15,
                    ClientSendRate = 30
                };

                _server = new NetServer(_transport, config);
                Host = new SessionHost("rejoin-review", "CODE", BuildSessionConfig(), _server);
                Replication = new ServerReplication(_server, () => Host.ConnectedPeers, new MovementValidator(config), world);
                _spawner = new SessionPawnSpawner(Host, Replication, 0, AuthorityMode.Server, placement);

                Host.Open();
            }

            public SessionHost Host { get; }

            public ServerReplication Replication { get; }

            public PlayerId Attach(PeerHandle peer, string displayName, PlayerId player = default) {
                PlayerId identityId = player.IsValid ? player : PlayerId.NewId();
                SessionAttachResult result = Host.AttachPeer(
                    peer, new PlayerIdentity(identityId, displayName, AuthMethod.Anonymous));

                Assert.True(result.IsAccepted, "the review peer was refused a seat: " + result.DenialReason);
                return identityId;
            }

            public void Dispose() {
                _spawner.Dispose();
                _server.Dispose();
                _transport.Dispose();
            }

            private static SessionConfigData BuildSessionConfig() {
                return new SessionConfigData {
                    Profile = new SessionProfileData { RejoinPolicy = RejoinPolicy.AnyTime },
                    Lobby = new LobbyConfigData()
                };
            }
        }

        /// <summary>Records the envelope id of every payload the server sends, per peer, in order.</summary>
        private sealed class RecordingTransport : INetTransport {
            private readonly INetTransport _inner;
            private readonly Dictionary<int, List<ushort>> _byPeer = new Dictionary<int, List<ushort>>();

            public RecordingTransport(INetTransport inner) {
                _inner = inner;
                _inner.OnPeerConnected += peer => OnPeerConnected?.Invoke(peer);
                _inner.OnPeerDisconnected += (peer, reason) => OnPeerDisconnected?.Invoke(peer, reason);
                _inner.OnData += (peer, data, delivery) => OnData?.Invoke(peer, data, delivery);
            }

            public event Action<PeerHandle> OnPeerConnected;

            public event Action<PeerHandle, DisconnectReason> OnPeerDisconnected;

            public event Action<PeerHandle, ArraySegment<byte>, DeliveryClass> OnData;

            public IReadOnlyList<ushort> MessageIdsFor(PeerHandle peer) {
                return _byPeer.TryGetValue(peer.Id, out List<ushort> ids) ? ids : (IReadOnlyList<ushort>)Array.Empty<ushort>();
            }

            public void Clear() {
                _byPeer.Clear();
            }

            public void Send(PeerHandle peer, ReadOnlySpan<byte> payload, DeliveryClass delivery) {
                if (payload.Length >= 2) {
                    Record(peer, (ushort)(payload[0] | (payload[1] << 8)));
                }

                _inner.Send(peer, payload, delivery);
            }

            public void StartServer(int port, int maxPeers, string protocolKey) {
                _inner.StartServer(port, maxPeers, protocolKey);
            }

            public void StartClient(string protocolKey) {
                _inner.StartClient(protocolKey);
            }

            public void Connect(NetEndpoint endpoint) {
                _inner.Connect(endpoint);
            }

            public void Disconnect(PeerHandle peer) {
                _inner.Disconnect(peer);
            }

            public void Poll() {
                _inner.Poll();
            }

            public void Stop() {
                _inner.Stop();
            }

            public int GetPingMs(PeerHandle peer) {
                return _inner.GetPingMs(peer);
            }

            public void Dispose() {
                _inner.Dispose();
            }

            private void Record(PeerHandle peer, ushort messageId) {
                if (!_byPeer.TryGetValue(peer.Id, out List<ushort> ids)) {
                    ids = new List<ushort>();
                    _byPeer[peer.Id] = ids;
                }

                ids.Add(messageId);
            }
        }

        /// <summary>
        /// The listen desk's session, stood up in the desk's own order: host, replication, claims,
        /// spawner, then the claim-keyframe subscription. Attach mirrors the desk's AttachPeer.
        /// </summary>
        private sealed class DeskOrderFixture : IDisposable {
            private readonly FakeNetTransport _transport = new FakeNetTransport();
            private readonly NetServer _server;
            private readonly ServerReplication _replication;
            private readonly SessionPawnSpawner _spawner;

            public DeskOrderFixture() {
                int port = Interlocked.Increment(ref nextPort);
                NetConfig config = new NetConfig {
                    GameProtocolName = "alpinelib-desk-order",
                    Port = port,
                    MaxPeers = 8,
                    ServerTickRate = 30,
                    SnapshotRate = 15,
                    ClientSendRate = 30
                };

                Recorder = new RecordingTransport(_transport);
                _server = new NetServer(Recorder, config);

                Host = new SessionHost("desk-order", "CODE", BuildSessionConfig(), _server);
                Host.Open();

                _replication = new ServerReplication(_server, () => Host.ConnectedPeers, new MovementValidator(config));
                Claims = new ServerClaimRegistry(_server, () => Host.ConnectedPeers);
                _spawner = new SessionPawnSpawner(Host, _replication, 0, AuthorityMode.Server, new RingSpawnPlacement());

                Host.OnMemberNeedsKeyframe += HandleMemberNeedsKeyframe;
            }

            public SessionHost Host { get; }

            public ServerClaimRegistry Claims { get; }

            public RecordingTransport Recorder { get; }

            /// <summary>What the desk's AttachPeer does: seat the peer and send nothing else.</summary>
            public void Attach(PeerHandle peer, string displayName) {
                SessionAttachResult result = Host.AttachPeer(
                    peer, new PlayerIdentity(PlayerId.NewId(), displayName, AuthMethod.Anonymous));

                Assert.True(result.IsAccepted, "the peer was refused a seat: " + result.DenialReason);
            }

            public void Dispose() {
                _spawner.Dispose();
                _server.Dispose();
                _transport.Dispose();
            }

            private void HandleMemberNeedsKeyframe(SessionMember member) {
                if (member == null || member.PeerId == SessionMember.NoPeerId) {
                    return;
                }

                Claims.SendKeyframeTo(new PeerHandle(member.PeerId));
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
