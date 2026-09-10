using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Replication.Messages;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// What the owner's repeated resync flags cost the per-window budget, and what the measure-first
    /// ordering left of the ceiling on a client that lies.
    /// </summary>
    /// <remarks>
    /// The owner sends one resumption several times because the datagram carrying it may be lost, and
    /// the server treats the sends landing inside <c>MovementValidator.ResyncBurstTicks</c> of the first
    /// as that one claim arriving again: charged once, and only while each send is the resumption
    /// carried on rather than a fresh teleport. These pin both ends of that — the canonical withhold
    /// exit, a rider leaving a carrier that was doing thirty metres a second, and a client flagging
    /// everything it sends.
    /// </remarks>
    public sealed class ResyncRepeatReview6Tests {
        private const ushort DeckCarrierId = 1;
        private const int TicksPerSecond = 30;

        /// <summary>Metres a rider carried by a consist covers in one server tick.</summary>
        private const float ConsistMetresPerTick = 30f / TicksPerSecond;

        /// <summary>The speed such a rider is still travelling at for the first moments after the exit.</summary>
        private static readonly Vector3 ConsistVelocity = new Vector3(30f, 0f, 0f);

        /// <summary>
        /// A rider whose withhold ends while they are still travelling at the carrier's speed pays one
        /// slot for the whole burst, so the car they board next is still affordable inside the same
        /// window.
        /// </summary>
        /// <remarks>
        /// The repeats are a metre apart because the rider is still moving at the consist's speed, which
        /// is a teleport to the gait ceiling — that is why they reach the resync path at all, and why
        /// charging each of them spent the entire budget on one exit.
        /// </remarks>
        [Fact]
        public void RepeatsAfterAFastExitCostTheExitOneSlot() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            var violations = new List<MovementVerdict>();
            world.Replication.OnMovementViolation += (entity, verdict) => violations.Add(verdict);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(Vector3.Zero, PawnState.WorldCarrierId));

            world.Pump(1);
            Report(world, owner, pawn, At(new Vector3(0.06f, 0f, 0f), PawnState.WorldCarrierId));

            // Twenty seconds of withheld updates on a thirty-metre-a-second consist.
            world.Pump(TicksPerSecond * 20);

            float carried = 600f;

            // Send 1 of the burst: the resumption. Adopted, one slot.
            world.Pump(1);
            ReportResync(world, owner, pawn, Carried(carried));
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);

            // Sends 2 and 3, one send tick apart, still flying at the consist's speed.
            for (int repeat = 0; repeat < 2; repeat++) {
                carried += ConsistMetresPerTick;
                world.Pump(1);
                ReportResync(world, owner, pawn, Carried(carried));
            }

            Assert.Equal(carried, pawn.State.Position.X, 3);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
            Assert.Empty(violations);

            // One slot spent, so the rider landing on the next car inside the same window is affordable.
            world.Pump(1);
            Report(world, owner, pawn, At(new Vector3(0.5f, 0f, 2f), DeckCarrierId));

            Assert.Equal(DeckCarrierId, pawn.State.CarrierId);
            Assert.Equal(2, pawn.CarrierSwitchesInWindow);
            Assert.Empty(violations);
        }

        /// <summary>
        /// The shipped owner's three repeats land on consecutive send ticks, and the densest honest
        /// window still holds only three charges: the burst is one of them, not three, so the source's
        /// next legal frame change is still affordable.
        /// </summary>
        /// <remarks>
        /// This is the shipped-client counterpart of
        /// <c>ResyncFlagReview5Tests.TheDensestHonestWindowStillHoldsOnlyThreeCharges</c>, which sends one
        /// resync where <c>NetActorSync</c> sends <c>ResyncSendRepeats</c> of them. The two agree because
        /// the repeats charge nothing.
        /// </remarks>
        [Fact]
        public void TheShippedThreeSendBurstFitsTheDensestHonestWindow() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(new Vector3(0.5f, 0f, 2f), PawnState.WorldCarrierId));

            // Tick 0: a frame change. Tick 1: the silent tick a withhold costs.
            Report(world, owner, pawn, At(new Vector3(0.5f, 0f, 2f), DeckCarrierId));

            // Ticks 2, 3 and 4: the owner's three repeats, from a rider still carrying the consist's speed.
            float carried = 12f;

            for (int repeat = 0; repeat < 3; repeat++) {
                world.Pump(repeat == 0 ? 2 : 1);
                ReportResync(world, owner, pawn, CarriedOnDeck(carried));
                carried += ConsistMetresPerTick;
            }

            Assert.Equal(2, pawn.CarrierSwitchesInWindow);

            // Tick 5: the earliest the source may honestly change frame again, and it is affordable.
            world.Pump(1);
            Report(world, owner, pawn, At(new Vector3(0.5f, 0f, 2f), PawnState.WorldCarrierId));

            Assert.Equal(PawnState.WorldCarrierId, pawn.State.CarrierId);
            Assert.Equal(MovementValidator.MaxCarrierSwitchesPerWindow, pawn.CarrierSwitchesInWindow);
        }

        /// <summary>
        /// The flag rescues one refused claim per burst window and no more, so a client flagging every
        /// update gets fewer unmeasured teleports a second than the budget alone would have given it.
        /// </summary>
        /// <remarks>
        /// This is the characterization of the trust boundary and it should be kept whatever else moves.
        /// The ceiling has come down twice: round 5 measured sixteen teleports a second and six kilometres
        /// of travel, measuring before consulting the flag took it to twelve, and charging a burst once
        /// takes it to one per <c>MovementValidator.ResyncBurstTicks</c> — because the claims in between
        /// are not continuations of the pose the server just adopted and are refused outright.
        /// </remarks>
        [Fact]
        public void TheFlagRescuesOneRefusedClaimPerBurstWindow() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(Vector3.Zero, PawnState.WorldCarrierId));

            int adopted = 0;

            for (int tick = 0; tick < TicksPerSecond; tick++) {
                world.Pump(1);
                float heldBefore = pawn.State.Position.X;

                ReportResync(
                    world,
                    owner,
                    pawn,
                    At(new Vector3(heldBefore + 500f, 0f, 0f), PawnState.WorldCarrierId));

                if (pawn.State.Position.X != heldBefore) adopted++;
            }

            // One burst every ResyncBurstTicks, counting the one on the second's first tick.
            int burstsPerSecond = (TicksPerSecond + (int)MovementValidator.ResyncBurstTicks - 1)
                / (int)MovementValidator.ResyncBurstTicks;

            Assert.Equal(burstsPerSecond, adopted);
            Assert.True(adopted < 12, $"teleports a second {adopted}");
            Assert.Equal(adopted * 500f, pawn.State.Position.X, 1);
        }

        /// <summary>
        /// A flagged claim the measurement would have accepted anyway costs nothing, which is why an
        /// owner may flag a resumption it turns out not to have needed.
        /// </summary>
        [Fact]
        public void AFlaggedStepThatMeasuresCleanIsNotCharged() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(Vector3.Zero, PawnState.WorldCarrierId));

            world.Pump(1);
            ReportResync(world, owner, pawn, At(new Vector3(0.06f, 0f, 0f), PawnState.WorldCarrierId));

            Assert.Equal(0, pawn.CarrierSwitchesInWindow);
            Assert.Equal(0.06f, pawn.State.Position.X, 3);
        }

        /// <summary>
        /// A claim inside the burst window that is a teleport rather than the resumption carried on is
        /// not a repeat, and the free window does not cover it.
        /// </summary>
        [Fact]
        public void ATeleportInsideTheBurstWindowIsRefused() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(Vector3.Zero, PawnState.WorldCarrierId));

            world.Pump(1);
            ReportResync(world, owner, pawn, Carried(600f));

            Assert.Equal(600f, pawn.State.Position.X, 3);

            world.Pump(1);
            ReportResync(world, owner, pawn, Carried(1100f));

            Assert.Equal(600f, pawn.State.Position.X, 3);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
        }

        private static void Report(
            CarrierReplicationLoopbackWorld world,
            CarrierReplicationLoopbackClient owner,
            NetEntity pawn,
            PawnState claim) {
            var message = new OwnerPawnUpdate(pawn.Id, 1u, in claim);

            world.Replication.HandleOwnerPawnUpdate(in message, owner.ServerSidePeer);
        }

        private static void ReportResync(
            CarrierReplicationLoopbackWorld world,
            CarrierReplicationLoopbackClient owner,
            NetEntity pawn,
            PawnState claim) {
            var message = new OwnerPawnUpdate(pawn.Id, 1u, OwnerPawnUpdate.ResyncFlag, in claim);

            world.Replication.HandleOwnerPawnUpdate(in message, owner.ServerSidePeer);
        }

        /// <summary>A rider in world space still carrying the consist's speed, as the owner reports it.</summary>
        private static PawnState Carried(float metresDownTheTrack) {
            return At(new Vector3(metresDownTheTrack, 0f, 0f), PawnState.WorldCarrierId, ConsistVelocity);
        }

        /// <summary>The same rider, reported in the deck's frame.</summary>
        private static PawnState CarriedOnDeck(float metresDownTheDeck) {
            return At(new Vector3(metresDownTheDeck, 0f, 2f), DeckCarrierId, ConsistVelocity);
        }

        private static PawnState At(Vector3 position, ushort carrierId) {
            return At(position, carrierId, Vector3.Zero);
        }

        private static PawnState At(Vector3 position, ushort carrierId, Vector3 velocity) {
            return new PawnState(
                position,
                0f,
                velocity,
                PawnState.PackFlags(WireLocomotion.Walk, false, true),
                carrierId);
        }
    }
}
