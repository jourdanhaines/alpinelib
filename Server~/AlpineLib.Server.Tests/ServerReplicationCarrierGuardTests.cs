using System;
using System.Numerics;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Transport;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The server world's refusal to simulate a state it cannot read: a carrier-relative spawn under
    /// server authority is rejected at the door rather than stepped against world geometry.
    /// </summary>
    /// <remarks>
    /// Left unguarded this is one of the quieter failures in the system. The motor would take deck-local
    /// metres for world ones, resolve them against whatever is at those coordinates, and publish a pawn
    /// standing somewhere plausible and entirely wrong — no exception, no log, just a player at the
    /// scene origin. Owner-simulated pawns are the whole point of the frame and must still be allowed.
    /// </remarks>
    public sealed class ServerReplicationCarrierGuardTests {
        private const ushort PawnPrefab = 0;
        private const ushort TrainCarrierId = 1;

        [Fact]
        public void AServerSimulatedPawnMayNotSpawnOnACarrier() {
            using var world = new GuardWorld();
            PawnState onADeck = Standing(TrainCarrierId);

            Assert.Throws<ArgumentException>(
                () => world.Replication.SpawnEntity(PawnPrefab, 1, AuthorityMode.Server, in onADeck));
        }

        [Fact]
        public void AServerSimulatedPawnInWorldSpaceSpawnsAsBefore() {
            using var world = new GuardWorld();
            PawnState onTheGround = Standing(PawnState.WorldCarrierId);

            NetEntity entity = world.Replication.SpawnEntity(PawnPrefab, 1, AuthorityMode.Server, in onTheGround);

            Assert.NotNull(entity);
            Assert.Equal(PawnState.WorldCarrierId, entity.State.CarrierId);
        }

        [Fact]
        public void AnOwnerSimulatedPawnMaySpawnStraightOntoACarrier() {
            using var world = new GuardWorld();
            PawnState onADeck = Standing(TrainCarrierId);

            NetEntity entity = world.Replication.SpawnEntity(PawnPrefab, 1, AuthorityMode.OwnerClient, in onADeck);

            Assert.NotNull(entity);
            Assert.Equal(TrainCarrierId, entity.State.CarrierId);
        }

        private static PawnState Standing(ushort carrierId) {
            return new PawnState(
                new Vector3(1f, 0f, 2f),
                0f,
                Vector3.Zero,
                PawnState.PackFlags(WireLocomotion.Walk, false, true),
                carrierId);
        }

        /// <summary>
        /// The smallest world a spawn can be attempted in: a server over an in-memory transport with no
        /// peers, so a spawn broadcast goes nowhere and the test is about the guard alone.
        /// </summary>
        private sealed class GuardWorld : IDisposable {
            private readonly FakeNetTransport transport = new FakeNetTransport();
            private readonly NetServer server;

            public GuardWorld() {
                NetConfig config = BuildConfig();
                server = new NetServer(transport, config);
                Replication = new ServerReplication(server, () => server.Peers, new MovementValidator(config));
            }

            /// <summary>The world under test.</summary>
            public ServerReplication Replication { get; }

            public void Dispose() {
                server.Stop();
                transport.Dispose();
            }

            private static NetConfig BuildConfig() {
                return new NetConfig {
                    GameProtocolName = "alpinelib-carrier-test",
                    Port = 0,
                    MaxPeers = 4,
                    ServerTickRate = 30,
                    SnapshotRate = 15,
                    ClientSendRate = 30,
                    InterpolationDelayMs = 100,
                    MovementToleranceMultiplier = 1.5f,
                    MovementProfiles = new[] {
                        new MovementProfile {
                            DisplayName = "pawn",
                            WalkSlowSpeed = 0.8f,
                            WalkSpeed = 2f,
                            JogSpeed = 3.5f,
                            SprintSpeed = 5.5f,
                            CrouchSpeed = 1.2f,
                            CrouchFastSpeed = 2.2f,
                            Gravity = -20f,
                            JumpVelocity = 6f
                        }
                    }
                };
            }
        }
    }
}
