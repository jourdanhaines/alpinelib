using System;
using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Replication.Messages;
using AlpineLib.Netcode.Replication.Origin;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// A floating-origin rebase end to end over the loopback transport: the server shifts its world, a
    /// walking owner in flight across it is never corrected, stale stamps are translated or refused, and
    /// a newcomer hears the origin before any state written in it.
    /// </summary>
    public sealed class OriginShiftLoopbackTests {
        private const double CellSize = 128.0;
        private const ushort DeckCarrierId = 5;
        private const float WalkStep = 0.05f;

        [Fact]
        public void AWalkerReportingAcrossARebaseIsNeverCorrected() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            var violations = new List<MovementVerdict>();
            world.Replication.OnMovementViolation += (entity, verdict) => violations.Add(verdict);
            NetEntity pawn = SpawnOwned(world, owner, Grounded(new Vector3(120f, 0f, 3f), PawnState.WorldCarrierId));
            world.Pump(2);

            Vector3 walker = pawn.State.Position;
            owner.Replication.OnOriginShifted += (previous, next, delta) => walker += delta;
            int correctionsBefore = owner.Corrections.Count;
            float absoluteBefore = pawn.State.Position.X;
            float largestStep = 0f;

            for (int tick = 1; tick <= 60; tick++) {
                walker.X += WalkStep;
                owner.Replication.SubmitOwnerPawnState(pawn.Id, Grounded(walker, PawnState.WorldCarrierId));

                // The update just sent is still stamped with the old epoch when the server rebases.
                if (tick == 30) {
                    world.Replication.ApplyOriginShift(world.Replication.Origin.Next(1, 0, CellSize));
                }

                world.Pump(1);

                float absolute = pawn.State.Position.X + world.Replication.Origin.CellX * (float)CellSize;
                Assert.True(absolute >= absoluteBefore - 0.001f, "The pawn moved backwards at tick " + tick.ToString() + ".");
                largestStep = MathF.Max(largestStep, absolute - absoluteBefore);
                absoluteBefore = absolute;
            }

            Assert.True(largestStep <= WalkStep + 0.001f, "The rebase showed up as a " + largestStep.ToString("0.000") + " m step.");

            world.Pump(4);

            Assert.Empty(violations);
            Assert.Equal(correctionsBefore, owner.Corrections.Count);
            Assert.Equal(0, world.Replication.StaleOriginUpdatesRejected);
            Assert.Equal(0, owner.Replication.OriginEpochMismatches);
            Assert.Equal(world.Replication.Origin, owner.Replication.Origin);
            Assert.Equal(120f + 60 * WalkStep - (float)CellSize, pawn.State.Position.X, 3);
            Assert.Equal(walker.X, pawn.State.Position.X, 3);
            Assert.Equal(walker.X, owner.Replication.GetEntity(pawn.Id).State.Position.X, 3);
        }

        [Fact]
        public void AnUpdateStampedWithThePreviousEpochIsTranslated() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);
            NetEntity pawn = SpawnOwned(world, owner, Grounded(new Vector3(100f, 0f, 0f), PawnState.WorldCarrierId));
            world.Pump(1);

            world.Replication.ApplyOriginShift(world.Replication.Origin.Next(1, 0, CellSize));
            Assert.Equal(-28f, pawn.State.Position.X, 3);

            Report(world, owner, pawn, Grounded(new Vector3(100.05f, 0f, 0f), PawnState.WorldCarrierId), 0);

            Assert.Equal(-27.95f, pawn.State.Position.X, 3);
            Assert.Equal(0, world.Replication.StaleOriginUpdatesRejected);
        }

        [Fact]
        public void AnUpdateTwoEpochsStaleIsRefusedAndAnsweredWithTheHeldState() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            var violations = new List<MovementVerdict>();
            world.Replication.OnMovementViolation += (entity, verdict) => violations.Add(verdict);
            NetEntity pawn = SpawnOwned(world, owner, Grounded(new Vector3(100f, 0f, 0f), PawnState.WorldCarrierId));
            world.Pump(1);

            world.Replication.ApplyOriginShift(world.Replication.Origin.Next(1, 0, CellSize));
            world.Replication.ApplyOriginShift(world.Replication.Origin.Next(2, 0, CellSize));
            world.Pump(2);
            int correctionsBefore = owner.Corrections.Count;

            Report(world, owner, pawn, Grounded(new Vector3(100.05f, 0f, 0f), PawnState.WorldCarrierId), 0);
            Report(world, owner, pawn, Grounded(new Vector3(100.05f, 0f, 0f), PawnState.WorldCarrierId), 9);
            world.Pump(2);

            Assert.Equal(-156f, pawn.State.Position.X, 3);
            Assert.Equal(2, world.Replication.StaleOriginUpdatesRejected);
            Assert.Empty(violations);
            Assert.Equal(correctionsBefore + 2, owner.Corrections.Count);
            Assert.Equal(-156f, owner.Corrections[owner.Corrections.Count - 1].Position.X, 3);
        }

        [Fact]
        public void CarrierRelativeStatesAreUntouchedAndAcceptedWhateverTheirStamp() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);
            var onDeck = new Vector3(0.5f, 0f, 2f);
            NetEntity rider = SpawnOwned(world, owner, Grounded(onDeck, DeckCarrierId));
            NetEntity simulated = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab, -1, AuthorityMode.Server, Grounded(new Vector3(10f, 0f, 0f), PawnState.WorldCarrierId));
            world.Pump(2);

            world.Replication.ApplyOriginShift(world.Replication.Origin.Next(1, 0, CellSize));
            world.Replication.ApplyOriginShift(world.Replication.Origin.Next(2, 0, CellSize));
            world.Pump(2);

            Assert.Equal(onDeck, rider.State.Position);
            Assert.Equal(onDeck, owner.Replication.GetEntity(rider.Id).State.Position);
            Assert.Equal(10f - 256f, simulated.State.Position.X, 3);

            Report(world, owner, rider, Grounded(onDeck + new Vector3(0f, 0f, 0.05f), DeckCarrierId), 0);

            Assert.Equal(onDeck.Z + 0.05f, rider.State.Position.Z, 3);
            Assert.Equal(0, world.Replication.StaleOriginUpdatesRejected);
        }

        [Fact]
        public void ANewcomerHearsTheOriginBeforeAnyStateWrittenInIt() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient first = world.ConnectClient();
            world.Pump(4);
            NetEntity pawn = SpawnOwned(world, first, Grounded(new Vector3(300f, 0f, 0f), PawnState.WorldCarrierId));
            world.Pump(2);

            world.Replication.ApplyOriginShift(world.Replication.Origin.Next(2, 0, CellSize));
            world.Pump(2);
            Assert.Equal(44f, first.Replication.GetEntity(pawn.Id).State.Position.X, 3);

            var arrivals = new List<string>();
            CarrierReplicationLoopbackClient joiner = world.ConnectClientObserved(client => ObserveArrivals(client, arrivals));

            NetEntity joinerPawn = SpawnOwned(world, joiner, Grounded(new Vector3(50f, 0f, 0f), PawnState.WorldCarrierId));
            world.Replication.SendKeyframeTo(joiner.ServerSidePeer);
            world.Pump(4);

            Assert.NotEmpty(arrivals);
            Assert.Equal("origin", arrivals[0]);
            Assert.Single(arrivals.FindAll(arrival => arrival == "origin"));
            Assert.Equal(world.Replication.Origin, joiner.Replication.Origin);
            Assert.Equal(44f, joiner.Replication.GetEntity(pawn.Id).State.Position.X, 3);
            Assert.Equal(50f, joiner.Replication.GetEntity(joinerPawn.Id).State.Position.X, 3);
            Assert.Equal(0, joiner.Replication.OriginEpochMismatches);
        }

        [Fact]
        public void TryRebaseFollowsThePolicyAndReachesClients() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);
            var authority = new OriginAuthority(1000.0, CellSize);

            Assert.False(world.Replication.TryRebase(authority, new Vector3(999f, 0f, 0f)));
            Assert.True(world.Replication.TryRebase(authority, new Vector3(1001f, 0f, -1f)));
            world.Pump(2);

            Assert.Equal(new SessionOrigin(1, 7, -1, CellSize), world.Replication.Origin);
            Assert.Equal(world.Replication.Origin, owner.Replication.Origin);
        }

        [Fact]
        public void AnOwnedWorldMovesWithTheOrigin() {
            using var world = new CarrierReplicationLoopbackWorld();
            world.Replication.UseWorld(BoxWorld(new Vector3(130f, -0.5f, 0f)), true);

            world.Replication.ApplyOriginShift(world.Replication.Origin.Next(1, 0, CellSize));

            Assert.True(world.Replication.World.TryGetSupport(2f, 0f, 10f, -10f, 1u, out SupportHit _));
            Assert.False(world.Replication.World.TryGetSupport(130f, 0f, 10f, -10f, 1u, out SupportHit _));
        }

        [Fact]
        public void ASharedSceneWorldRefusesToRebase() {
            using var world = new CarrierReplicationLoopbackWorld();
            world.Replication.UseWorld(BoxWorld(new Vector3(130f, -0.5f, 0f)));
            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab, -1, AuthorityMode.Server, Grounded(new Vector3(10f, 0f, 0f), PawnState.WorldCarrierId));

            Assert.Throws<InvalidOperationException>(
                () => world.Replication.ApplyOriginShift(world.Replication.Origin.Next(1, 0, CellSize)));
            Assert.Throws<ArgumentException>(
                () => world.Replication.ApplyOriginShift(new SessionOrigin(3, 1, 0, CellSize)));

            Assert.Equal(SessionOrigin.Initial, world.Replication.Origin);
            Assert.Equal(10f, pawn.State.Position.X);
        }

        private static void ObserveArrivals(CarrierReplicationLoopbackClient client, List<string> arrivals) {
            client.Replication.OnOriginShifted += (previous, next, delta) => arrivals.Add("origin");
            client.Replication.OnEntitySpawned += entity => arrivals.Add("entity");
        }

        private static NetEntity SpawnOwned(
            CarrierReplicationLoopbackWorld world,
            CarrierReplicationLoopbackClient owner,
            PawnState state) {
            return world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                state);
        }

        private static void Report(
            CarrierReplicationLoopbackWorld world,
            CarrierReplicationLoopbackClient owner,
            NetEntity pawn,
            PawnState claim,
            ushort epoch) {
            var message = new OwnerPawnUpdate(pawn.Id, 1u, in claim) { OriginEpoch = epoch };
            world.Replication.HandleOwnerPawnUpdate(in message, owner.ServerSidePeer);
        }

        private static PawnState Grounded(Vector3 position, ushort carrierId) {
            return new PawnState(position, 0f, Vector3.Zero, PawnState.PackFlags(WireLocomotion.Walk, false, true), carrierId);
        }

        private static CollisionWorld BoxWorld(Vector3 center) {
            CollisionShape box = CollisionShape.MakeBox(center, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(2f));
            var geometry = new SceneGeometry("Scene", 1u, new[] { box }, Array.Empty<MoverDefinition>());
            return new CollisionWorld(geometry, 1f / 30f);
        }
    }
}
