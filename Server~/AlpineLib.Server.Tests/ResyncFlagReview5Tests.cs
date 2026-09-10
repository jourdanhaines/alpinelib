using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Replication.Messages;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// What <c>OwnerPawnUpdate.ResyncFlag</c> costs a server that believes it, and what the capped
    /// measurement interval costs a client that is merely unlucky.
    /// </summary>
    /// <remarks>
    /// The flag hands an owner an unmeasured move on its own say-so. These pin the ceiling that puts on
    /// a client that lies on every update, the charge a resync arriving alongside a frame change
    /// actually pays, the free move the shipped owner side takes at every spawn, and the corrections a
    /// transport gap now earns an honest client that never flagged anything.
    /// </remarks>
    public sealed class ResyncFlagReview5Tests {
        private const ushort DeckCarrierId = 1;
        private const ushort SecondCarrierId = 2;
        private const int TicksPerSecond = 30;

        /// <summary>Metres a lying client claims per update, far past anything the validator would allow.</summary>
        private const float TeleportMetres = 500f;

        private static readonly Vector3 OnTheDeck = new Vector3(0.5f, 0f, 2f);

        /// <summary>
        /// A client that sets the flag on every update is bounded in frequency but not in distance: it
        /// gets one unmeasured teleport of any size per burst window.
        /// </summary>
        /// <remarks>
        /// The bound is real and it was the frame-change bound, exactly as the remark on
        /// <c>ServerReplication.TryAcceptResync</c> claims. What the remark does not say is what the
        /// bound is worth: nothing caps how far one unmeasured move may travel, so the ceiling on a
        /// cheat is a rate of teleports rather than a speed. Round 5 measured a whole budget per window,
        /// twelve a second; charging a burst once rather than per datagram took that to one per
        /// <c>MovementValidator.ResyncBurstTicks</c>, because the flagged claims in between must be the
        /// adopted pose carried on and a teleport is not.
        /// </remarks>
        [Fact]
        public void AClientFlaggingEveryUpdateGetsOneTeleportPerBurstWindow() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(Vector3.Zero, PawnState.WorldCarrierId));

            int adopted = 0;

            // One second of a thirty-hertz send rate, every update flagged as a resync and each one
            // claiming the same half-kilometre step from wherever the server currently holds the pawn.
            for (int tick = 0; tick < TicksPerSecond; tick++) {
                world.Pump(1);
                float heldBefore = pawn.State.Position.X;

                ReportResync(
                    world,
                    owner,
                    pawn,
                    At(new Vector3(heldBefore + TeleportMetres, 0f, 0f), PawnState.WorldCarrierId));

                if (pawn.State.Position.X != heldBefore) adopted++;
            }

            // One accepted then three refused, per burst window, for the whole second.
            int expected = (TicksPerSecond + (int)MovementValidator.ResyncBurstTicks - 1)
                / (int)MovementValidator.ResyncBurstTicks;

            Assert.Equal(expected, adopted);
            Assert.True(expected < MovementValidator.MaxCarrierSwitchesPerWindow * 4, "ceiling rose");
            Assert.Equal(expected * TeleportMetres, pawn.State.Position.X, 1);
        }

        /// <summary>
        /// A resync that also names a different carrier is charged one slot, not two: the resync path
        /// stands aside and the frame-change path pays.
        /// </summary>
        [Fact]
        public void AResyncArrivingWithAFrameChangeIsChargedOnce() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(Vector3.Zero, PawnState.WorldCarrierId));

            ReportResync(world, owner, pawn, At(new Vector3(60f, 0f, 0f), DeckCarrierId));

            Assert.Equal(DeckCarrierId, pawn.State.CarrierId);
            Assert.Equal(60f, pawn.State.Position.X, 3);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
        }

        /// <summary>
        /// A flagged first report is adopted whole however old the session is, which is exactly why the
        /// owner side no longer flags an ordinary spawn.
        /// </summary>
        /// <remarks>
        /// The cap on the measured interval closes "silence is not credit" for every report the server
        /// measures, and a resync is not measured at all — so a client that flagged its first report
        /// would walk through the cap with the largest gap of the session behind it. The owner therefore
        /// raises the latch only for a placement it <em>waited</em> for, never for a spawn whose carrier
        /// resolved on the binding frame; see <c>NetActorSync.PlaceOnResolvedCarrier</c>. The unflagged
        /// half of the pair is
        /// <c>WithheldOwnerUpdateReview4Tests.TheFirstReportOnALongLivedSessionIsMeasuredOverTheCapNotTheUptime</c>.
        /// </remarks>
        [Fact]
        public void AFlaggedFirstReportIsAdoptedWholeWhichIsWhyAPlainSpawnNoLongerFlagsOne() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();

            var violations = new List<MovementVerdict>();
            world.Replication.OnMovementViolation += (entity, verdict) => violations.Add(verdict);

            world.Pump(TicksPerSecond * 40);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(Vector3.Zero, PawnState.WorldCarrierId));

            ReportResync(world, owner, pawn, At(new Vector3(100f, 0f, 0f), PawnState.WorldCarrierId));

            Assert.Empty(violations);
            Assert.Equal(100f, pawn.State.Position.X, 3);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
        }

        /// <summary>
        /// An honest client whose packets stop arriving for a couple of seconds is clamped where it used
        /// to be accepted, and one quiet for five seconds is rejected outright and snapped back.
        /// </summary>
        /// <remarks>
        /// Nothing sets the resync flag for a transport gap: <c>NetActorSync</c> raises its latch only
        /// when it withholds a state itself or places the pawn itself, and a client whose datagrams are
        /// dropped has done neither and cannot tell. The capped interval therefore lands entirely on the
        /// honest side of this case. The fixture walks at two metres a second with a 1.5 tolerance, so
        /// the capped allowance is 3.05 m: two seconds of walking is 4 m and clamps, five seconds is
        /// 10 m and is past the reject ratio.
        /// </remarks>
        [Fact]
        public void AnHonestTransportGapIsClampedAtTwoSecondsAndRejectedAtFive() {
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

            float lastHeardX = pawn.State.Position.X;

            // Two seconds of loss, during which the player walked four metres.
            world.Pump(TicksPerSecond * 2);
            Report(world, owner, pawn, At(new Vector3(lastHeardX + 4f, 0f, 0f), PawnState.WorldCarrierId));

            MovementVerdict clamped = Assert.Single(violations);
            Assert.Equal(MovementVerdictKind.Clamped, clamped.Kind);
            Assert.Equal(lastHeardX + CappedWalkAllowance(), pawn.State.Position.X, 3);

            lastHeardX = pawn.State.Position.X;
            violations.Clear();

            // Five seconds of loss, ten metres of honest walking, and the whole ten are thrown away.
            world.Pump(TicksPerSecond * 5);
            Report(world, owner, pawn, At(new Vector3(lastHeardX + 10f, 0f, 0f), PawnState.WorldCarrierId));

            MovementVerdict rejected = Assert.Single(violations);
            Assert.Equal(MovementVerdictKind.Rejected, rejected.Kind);
            Assert.Equal(lastHeardX, pawn.State.Position.X, 3);
        }


        /// <summary>
        /// The burst the fourth budget slot was added for cannot happen: a resync needs a silent send
        /// tick in front of it, and by the time one has passed after a conforming source's third change
        /// the window has already reopened.
        /// </summary>
        /// <remarks>
        /// An owner pushes at most one sample per send tick — <c>NetActorSync.AccumulateAndSend</c> is a
        /// single guarded send, not a drain — so a resync at tick <c>k</c> means nothing at all was sent
        /// at <c>k-1</c>, and a frame change cannot have been charged there either. A conforming source's
        /// densest burst is ticks 0, 3 and 6 of an eight-tick window, so the earliest resync that can
        /// follow it is tick 8, which reopens the window. That is why the budget does not need a fourth
        /// slot for the resync; the burst itself is pinned by
        /// <c>WithheldOwnerUpdateReview4Tests.ASourceDwellingTheMandatedMinimumFillsTheBudgetExactly</c>.
        /// One resync is the whole of what the shipped owner costs even though it sends three of them,
        /// because the repeats charge nothing —
        /// <c>ResyncRepeatReview6Tests.TheShippedThreeSendBurstFitsTheDensestHonestWindow</c>.
        /// </remarks>
        [Fact]
        public void AResyncCannotLandInsideAConformingSourcesThreeChangeBurst() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(OnTheDeck, PawnState.WorldCarrierId));

            // Ticks 0, 3 and 6 of one window: the fastest a source honouring the dwell can change frame.
            Report(world, owner, pawn, At(OnTheDeck, DeckCarrierId));
            world.Pump(3);
            Report(world, owner, pawn, At(OnTheDeck, SecondCarrierId));
            world.Pump(3);
            Report(world, owner, pawn, At(OnTheDeck, DeckCarrierId));

            Assert.Equal(3, pawn.CarrierSwitchesInWindow);

            // Tick 7: the silent tick a withhold costs, which is what makes the next report a resync.
            world.Pump(1);

            // Tick 8: the earliest a resync can follow, and the window has already reopened for it.
            world.Pump(1);
            ReportResync(world, owner, pawn, At(new Vector3(40f, 0f, 0f), DeckCarrierId));

            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
            Assert.Equal(40f, pawn.State.Position.X, 3);
        }

        /// <summary>
        /// With the silent tick honoured, the densest honest window holds exactly the budget: a resync
        /// early in the window pushes the source's remaining changes past its end rather than stacking
        /// with them.
        /// </summary>
        /// <remarks>
        /// One flagged send stands for the whole burst the owner actually pushes, which is what the
        /// server charges for; <c>ResyncRepeatReview6Tests.TheShippedThreeSendBurstFitsTheDensestHonestWindow</c>
        /// runs the same window with all three sends and reaches the same count.
        /// </remarks>
        [Fact]
        public void TheDensestHonestWindowStillHoldsOnlyThreeCharges() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(OnTheDeck, PawnState.WorldCarrierId));

            // Tick 0: a frame change. Tick 1: silent. Tick 2: the resync that silence produced.
            Report(world, owner, pawn, At(OnTheDeck, DeckCarrierId));
            world.Pump(2);
            ReportResync(world, owner, pawn, At(new Vector3(12f, 0f, 0f), DeckCarrierId));

            // Tick 5: the earliest the source may change frame again after settling at tick 2.
            world.Pump(3);
            Report(world, owner, pawn, At(OnTheDeck, SecondCarrierId));

            Assert.Equal(3, pawn.CarrierSwitchesInWindow);
            Assert.Equal(MovementValidator.MaxCarrierSwitchesPerWindow, pawn.CarrierSwitchesInWindow);
        }

        /// <summary>
        /// The owner repeats the resync flag, so losing the datagram that carried it costs nothing: a
        /// later repeat lands the resumption, and the repeats after it are ordinary measured steps.
        /// </summary>
        /// <remarks>
        /// Owner updates ride an unreliable sequenced channel, which neither retransmits a lost datagram
        /// nor delivers a late one — so a single loss on the tick a withhold ends used to strand the
        /// latch and snap the rider back the whole length of their ride. The first send below is simply
        /// never delivered, which is what a loss looks like to the server. The burst still costs one slot
        /// of the budget, because <c>ServerReplication.HandleOwnerPawnUpdate</c> consults the flag only
        /// after the measurement has refused a claim.
        /// </remarks>
        [Fact]
        public void ARepeatedResyncSurvivesALostDatagramAndStillCostsOneSlot() {
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

            // A second of withheld updates while the rider's game carries them sixty metres, then the
            // first flagged resumption — which never arrives.
            world.Pump(TicksPerSecond);

            var carried = new Vector3(60f, 0f, 0f);

            world.Pump(1);
            ReportResync(world, owner, pawn, At(carried, PawnState.WorldCarrierId));

            Assert.Equal(60f, pawn.State.Position.X, 3);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
            Assert.Empty(violations);

            // The third send of the burst is a walking step from a pose the server now agrees with, so
            // the measurement takes it and nothing is charged for the repeat.
            world.Pump(1);
            ReportResync(world, owner, pawn, At(new Vector3(60.06f, 0f, 0f), PawnState.WorldCarrierId));

            Assert.Equal(60.06f, pawn.State.Position.X, 3);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
            Assert.Empty(violations);
        }

        /// <summary>Metres the fixture's walk buys over the longest interval the server will measure.</summary>
        private static float CappedWalkAllowance() {
            return 2f * 1.5f * ServerReplication.MaxMeasuredIntervalSeconds + MovementValidator.PositionSlackMetres;
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
