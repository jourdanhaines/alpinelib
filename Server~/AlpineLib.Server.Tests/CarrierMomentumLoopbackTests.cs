using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Replication.Messages;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The two ends of a ride at consist speed as the server now sees them: boarding reported at once
    /// costs nothing, and the arc off the far end is honoured for as long as the pawn is in the air.
    /// </summary>
    /// <remarks>
    /// The loopback world's walk gait is 2 m/s, so its per-tick allowance is 0.15 m and a consist tick
    /// of a metre is a rejection by a wide margin without the momentum rule.
    /// </remarks>
    public sealed class CarrierMomentumLoopbackTests {
        private const ushort DeckCarrierId = 1;
        private const float ConsistSpeed = 30f;
        private const float DeckTravelPerTick = 1f;

        private static readonly Vector3 OnTheDeck = new Vector3(0.5f, 0f, 2f);
        private static readonly Vector3 Carried = new Vector3(ConsistSpeed, -1f, 0f);

        [Fact]
        public void BoardingAMovingConsistReportedAtOnceCostsNoCorrection() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            var violations = new List<MovementVerdict>();
            world.Replication.OnMovementViolation += (entity, verdict) => violations.Add(verdict);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                Grounded(new Vector3(500f, 0f, 0f), PawnState.WorldCarrierId));
            int correctionsBefore = owner.Corrections.Count;

            // The boarding tick names the deck straight away, then the rider walks it at gait.
            world.Pump(1);
            Report(world, owner, pawn, Grounded(OnTheDeck, DeckCarrierId));

            for (int tick = 1; tick <= 30; tick++) {
                world.Pump(1);
                Vector3 walked = new Vector3(OnTheDeck.X, OnTheDeck.Y, OnTheDeck.Z + 0.05f * tick);
                Report(world, owner, pawn, Grounded(walked, DeckCarrierId));
            }

            world.Pump(1);

            Assert.Empty(violations);
            Assert.Equal(correctionsBefore, owner.Corrections.Count);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
            Assert.Equal(DeckCarrierId, pawn.State.CarrierId);
        }

        [Fact]
        public void LeavingAtConsistSpeedIsHonouredThroughTheArcAndClosedOnLanding() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            var violations = new List<MovementVerdict>();
            world.Replication.OnMovementViolation += (entity, verdict) => violations.Add(verdict);

            NetEntity pawn = SpawnRider(world, owner);

            world.Pump(1);
            Report(world, owner, pawn, Airborne(new Vector3(500f, 1.6f, 0f), Carried));

            Assert.Equal(PawnState.WorldCarrierId, pawn.State.CarrierId);
            Assert.Equal(ConsistSpeed, pawn.CarriedPlanarSpeed, 3);

            for (int tick = 1; tick <= 12; tick++) {
                world.Pump(1);
                Vector3 arc = new Vector3(500f + DeckTravelPerTick * tick, 1.6f - 0.1f * tick, 0f);
                Report(world, owner, pawn, Airborne(arc, Carried));
            }

            Assert.Empty(violations);

            // Feet down: the momentum closes, and the next consist-speed tick is the teleport it is.
            world.Pump(1);
            Report(world, owner, pawn, Grounded(new Vector3(513f, 0f, 0f), PawnState.WorldCarrierId));
            Assert.Empty(violations);
            Assert.Equal(0f, pawn.CarriedPlanarSpeed);

            world.Pump(1);
            Report(world, owner, pawn, Grounded(new Vector3(514f, 0f, 0f), PawnState.WorldCarrierId));
            Assert.Single(violations);
            Assert.Equal(513f, pawn.State.Position.X, 3);

            world.Pump(1);
            Report(world, owner, pawn, Grounded(new Vector3(513.05f, 0f, 0f), PawnState.WorldCarrierId));
            Assert.Single(violations);
        }

        [Fact]
        public void CarriedMomentumExpiresAfterItsWindow() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            var violations = new List<MovementVerdict>();
            world.Replication.OnMovementViolation += (entity, verdict) => violations.Add(verdict);

            NetEntity pawn = SpawnRider(world, owner);

            world.Pump(1);
            Report(world, owner, pawn, Airborne(new Vector3(500f, 50f, 0f), Carried));

            // The pawn keeps reporting through the window so the measured interval stays one tick — a
            // silence would buy allowance of its own. The loopback world ticks at thirty hertz; once a
            // whole momentum window has passed, a consist-speed tick is measured at gait again.
            int windowTicks = (int)System.Math.Ceiling(MovementValidator.CarriedMomentumSeconds * 30f);
            for (int tick = 1; tick <= windowTicks; tick++) {
                world.Pump(1);
                Report(world, owner, pawn, Airborne(new Vector3(500f, 50f - 0.1f * tick, 0f), Carried));
            }

            Assert.Empty(violations);

            world.Pump(1);
            Report(world, owner, pawn, Airborne(new Vector3(501f, 43f, 0f), Carried));

            MovementVerdict refused = Assert.Single(violations);
            Assert.Equal(MovementVerdictKind.Rejected, refused.Kind);
        }

        [Fact]
        public void ALaterClaimCannotRaiseTheLatchedSpeed() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            var violations = new List<MovementVerdict>();
            world.Replication.OnMovementViolation += (entity, verdict) => violations.Add(verdict);

            NetEntity pawn = SpawnRider(world, owner);

            world.Pump(1);
            Report(world, owner, pawn, Airborne(new Vector3(500f, 50f, 0f), Carried));

            world.Pump(1);
            Report(world, owner, pawn, Airborne(new Vector3(501f, 49f, 0f), new Vector3(100f, 0f, 0f)));
            Assert.Empty(violations);
            Assert.Equal(ConsistSpeed, pawn.CarriedPlanarSpeed, 3);

            // Six metres in a tick is past the reject bar at the latched thirty, and would be inside
            // it at the claimed hundred.
            world.Pump(1);
            Report(world, owner, pawn, Airborne(new Vector3(507f, 48f, 0f), new Vector3(100f, 0f, 0f)));

            MovementVerdict refused = Assert.Single(violations);
            Assert.Equal(MovementVerdictKind.Rejected, refused.Kind);
            Assert.Equal(501f, pawn.State.Position.X, 3);
        }

        private static NetEntity SpawnRider(
            CarrierReplicationLoopbackWorld world,
            CarrierReplicationLoopbackClient owner) {
            return world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                Grounded(OnTheDeck, DeckCarrierId));
        }

        private static void Report(
            CarrierReplicationLoopbackWorld world,
            CarrierReplicationLoopbackClient owner,
            NetEntity pawn,
            PawnState claim) {
            var message = new OwnerPawnUpdate(pawn.Id, 1u, in claim);

            world.Replication.HandleOwnerPawnUpdate(in message, owner.ServerSidePeer);
        }

        private static PawnState Grounded(Vector3 position, ushort carrierId) {
            return new PawnState(position, 0f, Vector3.Zero, PawnState.PackFlags(WireLocomotion.Walk, false, true), carrierId);
        }

        private static PawnState Airborne(Vector3 position, Vector3 velocity) {
            return new PawnState(position, 0f, velocity, PawnState.PackFlags(WireLocomotion.Walk, false, false), PawnState.WorldCarrierId);
        }
    }
}
