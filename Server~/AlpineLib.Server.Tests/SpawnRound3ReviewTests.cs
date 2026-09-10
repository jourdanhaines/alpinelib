using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Threading;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Replication.Messages;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Claims;
using AlpineLib.Netcode.Sessions.Spawning;
using AlpineLib.Netcode.Transport;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Round-three probes: how many places the bounded overflow ring really holds for a marker, whatever
    /// length list the scene authored, and whether a peer still gets the world in full now that the listen
    /// desk sends no keyframe of its own.
    /// </summary>
    public sealed class SpawnRound3ReviewTests {
        private const uint AnyTick = 7u;

        /// <summary>How far the outermost overflow seat sits from its marker.</summary>
        private const float RingReachMetres =
            ListSpawnPlacement.OverflowRadiusMetres * ListSpawnPlacement.OverflowRevolutions;

        /// <summary>What the class remarks promise a marker holds before its seats start over.</summary>
        private const int PromisedOverflowPlaces =
            ListSpawnPlacement.OverflowSeats * ListSpawnPlacement.OverflowRevolutions;

        private static int nextPort = 48500;

        /// <summary>
        /// One marker on a platform sized to the ring's reach, and a hundred arrivals through it. Every one
        /// stands within the reach of the ring and on the surface the marker was authored on, whatever the
        /// arrival count.
        /// </summary>
        [Fact]
        public void AOneMarkerListKeepsAHundredArrivalsOnItsPlatform() {
            CollisionWorld world = PlatformOverGround(platformHalfExtentMetres: 3f, platformTopMetres: 4f);
            var marker = new Vector3(0f, 4f, 0f);
            var placement = new ListSpawnPlacement(new[] { new SpawnPoint(marker) });

            for (int arrival = 0; arrival < 100; arrival++) {
                PawnState state = placement.NextSpawnState(Member(), false, world, AnyTick);

                Assert.True(
                    HorizontalDistance(state.Position, marker) <= RingReachMetres + 0.001f,
                    $"arrival {arrival} was seated past the ring's reach, at {state.Position}");

                Assert.True(
                    MathF.Abs(state.Position.Y - 4f) <= 0.01f,
                    $"arrival {arrival} was seated off the marker's platform, at {state.Position}");
            }
        }

        /// <summary>
        /// A scene that authors one marker per seat of the usual lobby cap — the list length that shares
        /// every factor with <see cref="ListSpawnPlacement.OverflowSeats"/>. The seat is seeded from the
        /// marker's own trip round the list rather than the arrival ordinal, so it advances once per trip
        /// and the marker holds every place the remarks promise instead of a handful.
        /// </summary>
        [Fact]
        public void AMarkerHoldsEveryOverflowPlaceWhenTheSceneAuthorsEight() {
            var placement = new ListSpawnPlacement(RowOf(8));

            IReadOnlyList<Vector3> forFirstMarker = OverflowPlacesForMarker(placement, markerCount: 8, marker: 0);

            Assert.Equal(PromisedOverflowPlaces, CountDistinct(forFirstMarker));
        }

        /// <summary>
        /// The same arithmetic taken to its end: a list as long as the ring holds places. Both the seat and
        /// the revolution are keyed off the marker's own trip count, so the marker still widens and still
        /// reaches all sixteen places rather than repeating one.
        /// </summary>
        [Fact]
        public void SixteenMarkersStillGiveEachMarkerEveryOverflowPlace() {
            var placement = new ListSpawnPlacement(RowOf(16));

            IReadOnlyList<Vector3> forFirstMarker = OverflowPlacesForMarker(placement, markerCount: 16, marker: 0);

            Assert.Equal(PromisedOverflowPlaces, CountDistinct(forFirstMarker));
        }

        /// <summary>
        /// What that buys on the ground: on an eight-marker scene the ninth arrival and the twenty-fifth
        /// stand apart, so a lobby of eight with two rounds of churn does not put two players who are both
        /// present on one spot.
        /// </summary>
        [Fact]
        public void TheNinthAndTwentyFifthArrivalsStandApartOnAnEightMarkerScene() {
            var placement = new ListSpawnPlacement(RowOf(8));
            var seen = new List<Vector3>();

            for (int arrival = 0; arrival < 25; arrival++) {
                seen.Add(placement.NextSpawnState(Member(), false, null, AnyTick).Position);
            }

            Assert.NotEqual(seen[8], seen[24]);
        }

        /// <summary>
        /// The remarks promise the place count for any authored list, not just the convenient lengths, so
        /// every length up to twice the ring is swept: each one gives its first marker the full set.
        /// </summary>
        [Fact]
        public void EveryAuthoredListLengthGivesAMarkerTheFullSetOfOverflowPlaces() {
            for (int markerCount = 1; markerCount <= 32; markerCount++) {
                var placement = new ListSpawnPlacement(RowOf(markerCount));

                IReadOnlyList<Vector3> places = OverflowPlacesForMarker(placement, markerCount, marker: 0);

                Assert.Equal(PromisedOverflowPlaces, CountDistinct(places));
            }
        }

        /// <summary>
        /// A list longer than the ring has seats still turns each marker's ring off its neighbour's: two
        /// seats of turn per marker index, so neighbours in a nine-point row lean a quarter turn apart.
        /// </summary>
        [Fact]
        public void MoreMarkersThanSeatsStillTurnNeighbouringRingsApart() {
            SpawnPoint[] points = RowOf(9);
            var placement = new ListSpawnPlacement(points);
            var seen = new List<Vector3>();

            for (int arrival = 0; arrival < 12; arrival++) {
                seen.Add(placement.NextSpawnState(Member(), false, null, AnyTick).Position);
            }

            Vector3 firstLean = Vector3.Normalize(seen[9] - points[0].Position);
            Vector3 secondLean = Vector3.Normalize(seen[10] - points[1].Position);

            Assert.True(
                Vector3.Dot(firstLean, secondLean) <= 0.001f,
                $"markers 0 and 1 of a nine-point list leaned less than a quarter turn apart: {firstLean} and {secondLean}");
        }

        /// <summary>
        /// The arrival ordinal is a long and a session cannot count that high, but the ring's arithmetic is
        /// checked at the top of the range anyway: the radius stays bounded and the seat stays on the ring
        /// for an arrival ordinal a step short of <see cref="long.MaxValue"/>.
        /// </summary>
        [Fact]
        public void ARingSeatStaysBoundedAtTheTopOfTheArrivalRange() {
            var placement = new ListSpawnPlacement(new[] { new SpawnPoint(Vector3.Zero) });

            SetArrivalCursor(placement, long.MaxValue - 3L);

            for (int step = 0; step < 3; step++) {
                Vector3 position = placement.NextSpawnState(Member(), false, null, AnyTick).Position;

                Assert.True(
                    HorizontalDistance(position, Vector3.Zero) <= RingReachMetres + 0.001f,
                    $"a near-maximal arrival ordinal was seated at {position}");
            }
        }

        /// <summary>
        /// The listen desk sends no keyframe of its own any more, so the session's own arrival announcement
        /// is the only thing that gets the world to a newcomer. It reaches a first join and a reclaimed
        /// seat alike, and exactly once each.
        /// </summary>
        [Fact]
        public void EveryAcceptedAttachStillSendsTheWorldExactlyOnce() {
            using var fixture = new DeskFixture();

            var peer = new PeerHandle(1);
            fixture.Recorder.Clear();
            PlayerId player = fixture.Attach(peer, "Newcomer");

            Assert.Equal(1, fixture.Recorder.CountFor(peer, ReplicationMessageIds.SnapshotKeyframe));

            fixture.Host.DetachPeer(peer, LeaveReason.TransportLost);

            var rejoined = new PeerHandle(2);
            fixture.Recorder.Clear();
            fixture.Attach(rejoined, "Newcomer", player);

            Assert.Equal(1, fixture.Recorder.CountFor(rejoined, ReplicationMessageIds.SnapshotKeyframe));
        }

        /// <summary>A row of markers one overflow radius apart along +X.</summary>
        private static SpawnPoint[] RowOf(int count) {
            var points = new SpawnPoint[count];

            for (int index = 0; index < count; index++) {
                points[index] = new SpawnPoint(new Vector3(index * ListSpawnPlacement.OverflowRadiusMetres, 0f, 0f));
            }

            return points;
        }

        /// <summary>
        /// The offsets one marker hands its overflow arrivals, over as many arrivals as the remarks say the
        /// ring holds. Taken relative to the marker so a marker's own places are compared, not the row's.
        /// </summary>
        private static IReadOnlyList<Vector3> OverflowPlacesForMarker(
            ISpawnPlacement placement, int markerCount, int marker) {
            var places = new List<Vector3>();
            int arrivals = markerCount * (PromisedOverflowPlaces + 1);

            for (int arrival = 0; arrival < arrivals; arrival++) {
                Vector3 position = placement.NextSpawnState(Member(), false, null, AnyTick).Position;

                if (arrival < markerCount || arrival % markerCount != marker) {
                    continue;
                }

                places.Add(position);
            }

            return places;
        }

        private static int CountDistinct(IReadOnlyList<Vector3> positions) {
            var distinct = new HashSet<Vector3>();

            for (int index = 0; index < positions.Count; index++) {
                distinct.Add(positions[index]);
            }

            return distinct.Count;
        }

        private static float HorizontalDistance(Vector3 left, Vector3 right) {
            float deltaX = left.X - right.X;
            float deltaZ = left.Z - right.Z;

            return MathF.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        }

        /// <summary>Winds the placement's arrival ordinal on, which nothing but a very long session can do.</summary>
        private static void SetArrivalCursor(ListSpawnPlacement placement, long arrival) {
            FieldInfo field = typeof(ListSpawnPlacement).GetField(
                "_nextArrival", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(field);
            field.SetValue(placement, arrival);
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
                "Round3Platform", 0u, new[] { ground, platform }, Array.Empty<MoverDefinition>());
            return new CollisionWorld(geometry, CollisionWorld.DefaultTickIntervalSeconds);
        }

        private static SessionMember Member() {
            return new SessionMember(1, PlayerId.NewId(), "Reviewer", false, 0);
        }

        /// <summary>Counts the envelope id of every payload the server sends, per peer.</summary>
        private sealed class CountingTransport : INetTransport {
            private readonly INetTransport _inner;
            private readonly Dictionary<int, List<ushort>> _byPeer = new Dictionary<int, List<ushort>>();

            public CountingTransport(INetTransport inner) {
                _inner = inner;
                _inner.OnPeerConnected += peer => OnPeerConnected?.Invoke(peer);
                _inner.OnPeerDisconnected += (peer, reason) => OnPeerDisconnected?.Invoke(peer, reason);
                _inner.OnData += (peer, data, delivery) => OnData?.Invoke(peer, data, delivery);
            }

            public event Action<PeerHandle> OnPeerConnected;

            public event Action<PeerHandle, DisconnectReason> OnPeerDisconnected;

            public event Action<PeerHandle, ArraySegment<byte>, DeliveryClass> OnData;

            public int CountFor(PeerHandle peer, ushort messageId) {
                if (!_byPeer.TryGetValue(peer.Id, out List<ushort> ids)) {
                    return 0;
                }

                int count = 0;

                for (int index = 0; index < ids.Count; index++) {
                    if (ids[index] == messageId) {
                        count++;
                    }
                }

                return count;
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
        /// The listen desk's session stood up in the desk's own order, with the desk's AttachPeer mirrored:
        /// seat the peer and send nothing else.
        /// </summary>
        private sealed class DeskFixture : IDisposable {
            private readonly FakeNetTransport _transport = new FakeNetTransport();
            private readonly NetServer _server;
            private readonly ServerReplication _replication;
            private readonly ServerClaimRegistry _claims;
            private readonly SessionPawnSpawner _spawner;

            public DeskFixture() {
                int port = Interlocked.Increment(ref nextPort);
                var config = new NetConfig {
                    GameProtocolName = "alpinelib-desk-round3",
                    Port = port,
                    MaxPeers = 8,
                    ServerTickRate = 30,
                    SnapshotRate = 15,
                    ClientSendRate = 30
                };

                Recorder = new CountingTransport(_transport);
                _server = new NetServer(Recorder, config);

                Host = new SessionHost("desk-round3", "CODE", BuildSessionConfig(), _server);
                Host.Open();

                _replication = new ServerReplication(_server, () => Host.ConnectedPeers, new MovementValidator(config));
                _claims = new ServerClaimRegistry(_server, () => Host.ConnectedPeers);
                _spawner = new SessionPawnSpawner(Host, _replication, 0, AuthorityMode.Server, new RingSpawnPlacement());

                Host.OnMemberNeedsKeyframe += HandleMemberNeedsKeyframe;
            }

            public SessionHost Host { get; }

            public CountingTransport Recorder { get; }

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

            private void HandleMemberNeedsKeyframe(SessionMember member) {
                if (member == null || member.PeerId == SessionMember.NoPeerId) {
                    return;
                }

                _claims.SendKeyframeTo(new PeerHandle(member.PeerId));
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
