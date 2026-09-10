using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Replication.Messages;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// What the carrier-switch budget costs an honest player, and where its boundary actually sits.
    /// </summary>
    /// <remarks>
    /// The rule bounds a cheat by counting frame changes over a window rather than by refusing a second
    /// one outright, and the difference is exactly these tests. A rider who leaves a deck and comes back
    /// sooner than the window — a hop, a step over a coupler, a bump that breaks grounding for a few
    /// frames — makes the same shape of claim as a cheat and must not be corrected for it, so the honest
    /// bursts are pinned as honoured and uncorrected while the alternation nobody produces by walking is
    /// pinned as refused.
    /// </remarks>
    public sealed class CarrierCooldownAdversarialTests {
        private const ushort DeckCarrierId = 1;
        private const ushort NextCarrierId = 2;

        /// <summary>Where the consist happens to be when the rider leaves its deck.</summary>
        private static readonly Vector3 Takeoff = new Vector3(500f, 0f, 0f);

        /// <summary>Somewhere on a deck; the exact spot never matters here, only the frame it is in.</summary>
        private static readonly Vector3 OnTheDeck = new Vector3(0.5f, 0f, 2f);

        [Fact]
        public void AShortHopOffADeckAndBackKeepsTheRideAndCostsNoCorrection() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = SpawnRiderOnTheDeck(world, owner);
            int correctionsBefore = owner.Corrections.Count;

            // The rider leaves the deck, so the game reports world space: the window opens here.
            ReportOnly(world, owner, pawn, At(Takeoff, PawnState.WorldCarrierId));
            Assert.Equal(PawnState.WorldCarrierId, pawn.State.CarrierId);

            // Four ticks of flight — a hop, not a leap — and the deck again, well inside the window.
            world.Pump(4);
            ReportOnly(world, owner, pawn, At(OnTheDeck, DeckCarrierId));

            Assert.Equal(DeckCarrierId, pawn.State.CarrierId);

            // Corrections reach the owner over the wire, so a tick has to pass before counting them.
            world.Pump(1);
            Assert.Equal(correctionsBefore, owner.Corrections.Count);
        }

        /// <summary>
        /// Walking across a coupler is two frame changes in quick succession, and a rider who hops on the
        /// way over makes it three. The budget covers all three.
        /// </summary>
        [Fact]
        public void ACouplerCrossingIsHonouredWholeAndUncorrected() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = SpawnRiderOnTheDeck(world, owner);
            int correctionsBefore = owner.Corrections.Count;

            ReportChangeOnTheNextTick(world, owner, pawn, PawnState.WorldCarrierId);
            ReportChangeOnTheNextTick(world, owner, pawn, NextCarrierId);
            ReportChangeOnTheNextTick(world, owner, pawn, PawnState.WorldCarrierId);

            Assert.Equal(PawnState.WorldCarrierId, pawn.State.CarrierId);

            world.Pump(1);
            Assert.Equal(correctionsBefore, owner.Corrections.Count);
        }

        /// <summary>
        /// One past the budget is where refusal starts, and it holds the frame the pawn was last honoured
        /// in rather than the one it is claiming.
        /// </summary>
        [Fact]
        public void TheChangePastTheBudgetIsRefusedAndCorrected() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = SpawnRiderOnTheDeck(world, owner);
            SpendTheWindowBudget(world, owner, pawn);

            int correctionsBefore = owner.Corrections.Count;
            ushort heldCarrierId = pawn.State.CarrierId;

            ReportOnly(world, owner, pawn, At(OnTheDeck, NextCarrierId));

            Assert.Equal(heldCarrierId, pawn.State.CarrierId);

            world.Pump(1);
            Assert.Equal(correctionsBefore + 1, owner.Corrections.Count);
            Assert.Equal(heldCarrierId, LastCorrection(owner).CarrierId);
        }

        [Fact]
        public void TheWindowReopensAtExactlyTheDocumentedTickCount() {
            Assert.False(IsAChangeHonouredAtWindowTick(7));
            Assert.True(IsAChangeHonouredAtWindowTick(8));
        }

        /// <summary>
        /// A rejoining client rebuilds every pawn from a keyframe, so the frame has to survive that
        /// message too — it is the one the entity spawner places a view from.
        /// </summary>
        [Fact]
        public void AKeyframeCarriesTheFrameItRebuildsAPawnIn() {
            PawnState onADeck = At(OnTheDeck, DeckCarrierId);
            var keyframe = new SnapshotKeyframe(
                7u,
                new List<EntityKeyframeRecord> {
                    new EntityKeyframeRecord(1u, 0, 3, AuthorityMode.OwnerClient, in onADeck)
                });

            var buffer = new byte[512];
            var writer = new NetWriter(buffer);
            keyframe.Serialize(ref writer);

            var reader = new NetReader(buffer, 0, writer.Written);
            var decoded = new SnapshotKeyframe();
            decoded.Deserialize(ref reader);

            Assert.Equal(0, reader.Remaining);
            Assert.Equal(DeckCarrierId, decoded.Records[0].State.CarrierId);
        }

        /// <summary>
        /// Spends a pawn's whole budget on consecutive ticks and then claims one more frame the given
        /// number of ticks after the window opened, answering whether that last claim was honoured.
        /// </summary>
        private static bool IsAChangeHonouredAtWindowTick(int ticksAfterTheWindowOpened) {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = SpawnRiderOnTheDeck(world, owner);
            SpendTheWindowBudget(world, owner, pawn);

            // The budget went one change per tick from the tick the window opened, so the clock already
            // stands that many ticks into it.
            world.Pump(ticksAfterTheWindowOpened - MovementValidator.MaxCarrierSwitchesPerWindow);
            ReportOnly(world, owner, pawn, At(OnTheDeck, NextCarrierId));

            return pawn.State.CarrierId == NextCarrierId;
        }

        /// <summary>
        /// Uses up a pawn's whole window budget, one frame change per tick, leaving the clock
        /// <see cref="MovementValidator.MaxCarrierSwitchesPerWindow"/> ticks into the window.
        /// </summary>
        private static void SpendTheWindowBudget(
            CarrierReplicationLoopbackWorld world,
            CarrierReplicationLoopbackClient owner,
            NetEntity pawn) {
            for (int change = 0; change < MovementValidator.MaxCarrierSwitchesPerWindow; change++) {
                ushort claimed = pawn.State.CarrierId == DeckCarrierId ? PawnState.WorldCarrierId : DeckCarrierId;

                ReportOnly(world, owner, pawn, At(OnTheDeck, claimed));
                world.Pump(1);
            }
        }

        /// <summary>Advances one tick and claims a frame, for tests that read the outcome afterwards.</summary>
        private static void ReportChangeOnTheNextTick(
            CarrierReplicationLoopbackWorld world,
            CarrierReplicationLoopbackClient owner,
            NetEntity pawn,
            ushort claimedCarrierId) {
            world.Pump(1);
            ReportOnly(world, owner, pawn, At(OnTheDeck, claimedCarrierId));
        }

        private static NetEntity SpawnRiderOnTheDeck(
            CarrierReplicationLoopbackWorld world,
            CarrierReplicationLoopbackClient owner) {
            return world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(OnTheDeck, DeckCarrierId));
        }

        /// <summary>Reports without advancing the clock, for tests that count ticks themselves.</summary>
        private static void ReportOnly(
            CarrierReplicationLoopbackWorld world,
            CarrierReplicationLoopbackClient owner,
            NetEntity pawn,
            PawnState claim) {
            var message = new OwnerPawnUpdate(pawn.Id, 1u, in claim);

            world.Replication.HandleOwnerPawnUpdate(in message, owner.ServerSidePeer);
        }

        private static PawnState LastCorrection(CarrierReplicationLoopbackClient owner) {
            Assert.NotEmpty(owner.Corrections);

            return owner.Corrections[owner.Corrections.Count - 1];
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
