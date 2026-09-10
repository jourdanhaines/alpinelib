using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Replication.Messages;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// What the dwell <c>INetCarrierSource</c> demands costs when the two frames are not moving together,
    /// and where the switch budget's boundaries actually sit.
    /// </summary>
    /// <remarks>
    /// The dwell is free for the case it was written for — a contact alternating between two cars of one
    /// consist, which share a velocity — and these tests measure the case it is not: a rider leaving a
    /// moving deck for the ground, where the reported frame recedes at the consist's own speed for the
    /// whole dwell and every tick of it is measured against a walking gait. That residue is what pins the
    /// dwell to the shortest length the server's budget allows rather than to a comfortable one.
    /// </remarks>
    public sealed class CarrierHysteresisReview3Tests {
        private const ushort DeckCarrierId = 1;
        private const ushort SecondCarrierId = 2;
        private const ushort ThirdCarrierId = 3;
        private const ushort FourthCarrierId = 4;

        /// <summary>Metres a thirty-metre-a-second consist covers in one thirty-hertz tick.</summary>
        private const float DeckTravelPerTick = 1f;

        /// <summary>
        /// Ticks in the dwell the interface mandates: <c>NetCarrier.SourceHysteresisSeconds</c>, 0.1 s, at
        /// the server's 30 Hz. Kept as a literal because the constant lives in the Unity assembly, which
        /// these engine-free tests cannot reference; the two are meant to move together.
        /// </summary>
        private const int DwellTicks = 3;

        private static readonly Vector3 OnTheDeck = new Vector3(0.5f, 0f, 2f);

        /// <summary>
        /// A rider who steps off a moving deck keeps reporting that deck for the mandated dwell, and its
        /// deck-local position recedes at the consist's speed the whole time.
        /// </summary>
        [Fact]
        public void SteppingOffAMovingDeckIsRefusedForEveryTickOfTheMandatedDwell() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            var violations = new List<MovementVerdict>();
            world.Replication.OnMovementViolation += (entity, verdict) => violations.Add(verdict);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(OnTheDeck, DeckCarrierId));

            int correctionsBefore = owner.Corrections.Count;

            // The rider is standing still on the ground; the deck it is still reporting is not.
            for (int tick = 0; tick < DwellTicks; tick++) {
                world.Pump(1);

                Vector3 recededDeckLocal = new Vector3(
                    OnTheDeck.X - DeckTravelPerTick * (tick + 1),
                    OnTheDeck.Y,
                    OnTheDeck.Z);

                Report(world, owner, pawn, At(recededDeckLocal, DeckCarrierId));
            }

            world.Pump(1);

            Assert.Equal(DwellTicks, violations.Count);
            Assert.Equal(correctionsBefore + DwellTicks, owner.Corrections.Count);

            // The authority is still within a metre of where the rider boarded, while the rider itself
            // reported three metres of deck-frame travel: the whole dwell is spent being corrected.
            Assert.Equal(DeckCarrierId, pawn.State.CarrierId);
            Assert.True(pawn.State.Position.X > OnTheDeck.X - 1f, $"held x {pawn.State.Position.X}");
        }

        /// <summary>
        /// The same arithmetic with the frames the other way round, and the reason an owner with an
        /// unusable carrier now sends nothing at all rather than reporting world coordinates.
        /// </summary>
        /// <remarks>
        /// A game carries a rider on a carrier the registry cannot resolve — a duplicate id, a scaled
        /// deck — and the body travels at the consist's speed with no frame anyone can name. Reporting
        /// those world coordinates buys a rejection, a correction and a violation every tick for as long
        /// as it lasts, which is what <c>NetActorSync.IsCarrierUsable</c> withholds the update to avoid;
        /// this pins what the wire would look like if it did not.
        /// </remarks>
        [Fact]
        public void ACarriedRiderReportingWorldSpaceIsRefusedAtDeckSpeed() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            var violations = new List<MovementVerdict>();
            world.Replication.OnMovementViolation += (entity, verdict) => violations.Add(verdict);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(new Vector3(500f, 0f, 0f), PawnState.WorldCarrierId));

            for (int tick = 0; tick < 5; tick++) {
                world.Pump(1);

                Vector3 carried = new Vector3(500f + DeckTravelPerTick * (tick + 1), 0f, 0f);

                Report(world, owner, pawn, At(carried, PawnState.WorldCarrierId));
            }

            world.Pump(1);

            Assert.Equal(5, violations.Count);
            Assert.True(pawn.State.Position.X < 501.5f, $"held x {pawn.State.Position.X}");
        }

        /// <summary>
        /// Three frame changes inside one window are accepted and the fourth is not, counted from the
        /// tick the window opened rather than from any later claim.
        /// </summary>
        [Fact]
        public void ExactlyThreeChangesFitInOneWindowAndTheFourthDoesNot() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = SpawnRider(world, owner);

            Report(world, owner, pawn, At(OnTheDeck, SecondCarrierId));
            Assert.Equal(SecondCarrierId, pawn.State.CarrierId);

            Report(world, owner, pawn, At(OnTheDeck, ThirdCarrierId));
            Assert.Equal(ThirdCarrierId, pawn.State.CarrierId);

            Report(world, owner, pawn, At(OnTheDeck, FourthCarrierId));
            Assert.Equal(FourthCarrierId, pawn.State.CarrierId);

            Report(world, owner, pawn, At(OnTheDeck, DeckCarrierId));
            Assert.Equal(FourthCarrierId, pawn.State.CarrierId);
        }

        /// <summary>
        /// A refused claim does not push the window out: the window still reopens eight ticks after the
        /// change that opened it, however many refusals landed in between.
        /// </summary>
        [Fact]
        public void RefusedClaimsDoNotHoldTheWindowOpen() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = SpawnRider(world, owner);

            Report(world, owner, pawn, At(OnTheDeck, SecondCarrierId));
            Report(world, owner, pawn, At(OnTheDeck, ThirdCarrierId));
            Report(world, owner, pawn, At(OnTheDeck, FourthCarrierId));

            // Seven ticks of refusals, each one counted into the window.
            for (int tick = 0; tick < 7; tick++) {
                world.Pump(1);
                Report(world, owner, pawn, At(OnTheDeck, DeckCarrierId));
                Assert.Equal(FourthCarrierId, pawn.State.CarrierId);
            }

            world.Pump(1);
            Report(world, owner, pawn, At(OnTheDeck, DeckCarrierId));

            Assert.Equal(DeckCarrierId, pawn.State.CarrierId);
        }

        /// <summary>
        /// The budget saturates rather than wrapping: a client claiming a new frame every tick for far
        /// more than a byte's worth of ticks is still refused, not let back in at 256.
        /// </summary>
        [Fact]
        public void TheWindowCounterSaturatesRatherThanWrapping() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = SpawnRider(world, owner);

            // Four hundred claims in the one tick, each one a different id so every claim is a change
            // and the window never reopens under them.
            for (ushort claimed = 100; claimed < 500; claimed++) {
                Report(world, owner, pawn, At(OnTheDeck, claimed));
            }

            Assert.Equal(byte.MaxValue, pawn.CarrierSwitchesInWindow);
            Assert.Equal(102, pawn.State.CarrierId);
        }

        private static NetEntity SpawnRider(
            CarrierReplicationLoopbackWorld world,
            CarrierReplicationLoopbackClient owner) {
            return world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(OnTheDeck, DeckCarrierId));
        }

        private static void Report(
            CarrierReplicationLoopbackWorld world,
            CarrierReplicationLoopbackClient owner,
            NetEntity pawn,
            PawnState claim) {
            var message = new OwnerPawnUpdate(pawn.Id, 1u, in claim);

            world.Replication.HandleOwnerPawnUpdate(in message, owner.ServerSidePeer);
        }

        private static PawnState At(Vector3 position, ushort carrierId) {
            return new PawnState(
                position,
                0f,
                Vector3.Zero,
                PawnState.PackFlags(WireLocomotion.Walk, false, true),
                carrierId);
        }
    }
}
