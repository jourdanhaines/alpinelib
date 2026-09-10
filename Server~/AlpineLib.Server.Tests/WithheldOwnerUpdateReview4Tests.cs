using System.Collections.Generic;
using System.Numerics;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Replication.Messages;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// What the server does while an owner-simulated pawn stops reporting, and what it does on the tick
    /// the reports come back.
    /// </summary>
    /// <remarks>
    /// <c>NetActorSync.TryCaptureState</c> withholds an owner's update outright whenever the carrier its
    /// game hands out cannot be named on the wire, which is the first thing in this codebase that
    /// produces a multi-tick silence in honest play. These pin what that silence costs: whether anything
    /// times the pawn out, what the resumption is measured against, and how much travel the gap buys.
    /// Two of them are the exit the withhold takes — a resumption on the same carrier is refused unless
    /// the owner flags it a resync, and a flagged one is charged against the frame-change budget rather
    /// than being free — and two pin the cap that stops the silence itself buying allowance.
    /// </remarks>
    public sealed class WithheldOwnerUpdateReview4Tests {
        private const ushort DeckCarrierId = 1;
        private const ushort SecondCarrierId = 2;
        private const int TicksPerSecond = 30;
        private const float TickInterval = 1f / TicksPerSecond;

        /// <summary>Ticks an unregistered carrier keeps an owner silent in the scenario under test.</summary>
        private const int WithheldTicks = 60;

        /// <summary>
        /// Metres the loopback fixture's walk allows over the longest interval the server will measure:
        /// 2 m/s × 1.5 tolerance × <c>ServerReplication.MaxMeasuredIntervalSeconds</c>, plus the slack.
        /// </summary>
        private const float CappedWalkAllowanceMetres = 2f * 1.5f * 1f + MovementValidator.PositionSlackMetres;

        private static readonly Vector3 OnTheDeck = new Vector3(0.5f, 0f, 2f);

        /// <summary>
        /// A silence ended by a report naming a different carrier is a frame change, so it is accepted
        /// unmeasured however far the pawn moved while it was quiet.
        /// </summary>
        /// <remarks>
        /// This is the shape the withhold actually produces: the owner goes quiet because its carrier is
        /// unregistered, and the tick it comes back is the tick that carrier became nameable, so the
        /// report carries a carrier id the server has not seen on this pawn before. The budget is wide
        /// open by then — a gap longer than the window reopens it — so nothing here is refused.
        /// </remarks>
        [Fact]
        public void ASilenceEndedByANewCarrierIsAcceptedAsABudgetedSwitch() {
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

            world.Pump(WithheldTicks);

            // Sixty ticks of a thirty-metre-a-second consist is sixty metres of carry the server never saw.
            Report(world, owner, pawn, At(OnTheDeck, DeckCarrierId));

            Assert.Empty(violations);
            Assert.Equal(DeckCarrierId, pawn.State.CarrierId);
            Assert.Equal(OnTheDeck.X, pawn.State.Position.X, 3);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
        }

        /// <summary>
        /// A pawn nobody reports for is neither timed out nor re-sent: it holds its last state, stays out
        /// of every snapshot, and keeps its place in the keyframe.
        /// </summary>
        [Fact]
        public void ASilentPawnHoldsItsLastStateAndIsNeverTimedOut() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(OnTheDeck, DeckCarrierId));

            Report(world, owner, pawn, At(OnTheDeck, DeckCarrierId));

            uint dirtyTickBefore = pawn.LastDirtyTick;
            PawnState stateBefore = pawn.State;

            // Ten seconds of silence, far past every cadence the server runs on.
            world.Pump(TicksPerSecond * 10);

            Assert.True(world.Replication.Entities.TryGet(pawn.Id, out NetEntity held));
            Assert.Same(pawn, held);
            Assert.Equal(stateBefore.Position.X, held.State.Position.X, 4);
            Assert.Equal(DeckCarrierId, held.State.CarrierId);
            Assert.Equal(dirtyTickBefore, held.LastDirtyTick);
            Assert.False(held.IsDirtySince(dirtyTickBefore));
            Assert.True(owner.IsConnected);
        }

        /// <summary>
        /// The delta a resumed report is measured against is the age of the pawn's last <em>change</em>,
        /// not the age of its last report — but it is capped, so the allowance stops growing after
        /// <c>ServerReplication.MaxMeasuredIntervalSeconds</c> however long the silence runs.
        /// </summary>
        /// <remarks>
        /// Silence is not credit. Without the cap the same four metres refused inside one tick were
        /// accepted whole two seconds later, from the very same held state, and a long enough gap bought
        /// a claim of any size; with it, two seconds and four buy exactly what one second buys. The
        /// withhold's own honest resumption is unaffected because it is flagged as a resync, and a client
        /// reporting continuously never reaches the cap at all.
        /// </remarks>
        [Fact]
        public void ASilenceBuysAtMostOneSecondOfAllowance() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(Vector3.Zero, DeckCarrierId));

            // One honest step, so the pawn has a real LastDirtyTick to age from.
            world.Pump(1);
            Report(world, owner, pawn, At(new Vector3(0.05f, 0f, 0f), DeckCarrierId));

            float heldX = pawn.State.Position.X;

            // Four metres in the very next tick: three times the allowance, so it is thrown away.
            world.Pump(1);
            Report(world, owner, pawn, At(new Vector3(heldX + 4f, 0f, 0f), DeckCarrierId));
            Assert.Equal(heldX, pawn.State.Position.X, 3);

            // The same four metres after two seconds of quiet: clamped to the capped second's allowance.
            world.Pump(TicksPerSecond * 2);
            Report(world, owner, pawn, At(new Vector3(heldX + 4f, 0f, 0f), DeckCarrierId));
            Assert.Equal(heldX + CappedWalkAllowanceMetres, pawn.State.Position.X, 3);

            // And eight more seconds of quiet buy no more than the two did.
            float clampedX = pawn.State.Position.X;

            world.Pump(TicksPerSecond * 8);
            Report(world, owner, pawn, At(new Vector3(clampedX + 4f, 0f, 0f), DeckCarrierId));
            Assert.Equal(clampedX + CappedWalkAllowanceMetres, pawn.State.Position.X, 3);
        }

        /// <summary>
        /// A pawn spawned into a session that has been up for a while has never been dirty, so its very
        /// first reported move ages from tick zero — and the cap is what stops that being the whole
        /// uptime's worth of allowance.
        /// </summary>
        /// <remarks>
        /// <c>NetEntity.LastDirtyTick</c> is zero from the constructor and is only stamped when a state
        /// actually changes, so the interval reads the age of the session rather than the age of the
        /// pawn. Forty seconds of server uptime was a hundred and twenty metres of allowance on the first
        /// claim, and a hundred-metre teleport was accepted with no violation raised at all; capped, the
        /// same claim is thrown away like any other teleport.
        ///
        /// The claim is unflagged, which is what a real client now sends after an ordinary spawn: the
        /// owner raises its resync latch only for a placement it waited for. A flagged first report is
        /// still adopted whole — <c>ResyncFlagReview5Tests</c> pins that — which is precisely why an
        /// undisplaced spawn must not flag one.
        /// </remarks>
        [Fact]
        public void TheFirstReportOnALongLivedSessionIsMeasuredOverTheCapNotTheUptime() {
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

            Assert.Equal(0u, pawn.LastDirtyTick);

            Report(world, owner, pawn, At(new Vector3(100f, 0f, 0f), PawnState.WorldCarrierId));

            MovementVerdict verdict = Assert.Single(violations);
            Assert.Equal(MovementVerdictKind.Rejected, verdict.Kind);
            Assert.Equal(0f, pawn.State.Position.X, 3);
        }

        /// <summary>
        /// The dwell residue at All Aboard's own numbers: a rider reporting the frame it is leaving while
        /// a thirty-metre-a-second consist recedes is refused on every tick of the dwell, and the refusals
        /// do not relent as the held state ages.
        /// </summary>
        /// <remarks>
        /// The loopback fixture's profile walks at two metres a second; the shipped registry walks at
        /// four, with the same 1.5 tolerance, so the allowance is a quarter of a metre a tick against a
        /// full metre of deck. A rejection holds the previous state, which never changes and so never
        /// dirties the entity, and the growing delta only widens the allowance to a fifth of the travel.
        /// Three ticks is the whole cost at the constant's current length; at the round-three length it
        /// was nine.
        /// </remarks>
        [Fact]
        public void TheDwellResidueIsThreeRefusalsAtTheShippedWalkSpeed() {
            MovementValidator validator = BuildAllAboardValidator();
            PawnState held = At(OnTheDeck, DeckCarrierId);

            for (int tick = 1; tick <= 3; tick++) {
                PawnState receding = At(
                    new Vector3(OnTheDeck.X - tick, OnTheDeck.Y, OnTheDeck.Z),
                    DeckCarrierId);

                MovementVerdict verdict = validator.Validate(
                    CarrierReplicationLoopbackWorld.PawnPrefab,
                    in held,
                    in receding,
                    tick * TickInterval,
                    true);

                Assert.Equal(MovementVerdictKind.Rejected, verdict.Kind);
                Assert.Equal(OnTheDeck.X, verdict.ResolvedState.Position.X, 4);
            }
        }

        /// <summary>
        /// The same rider settling onto the frame it is joining: the tick the dwell ends is a frame
        /// change, so it costs nothing however far the deck has carried the body.
        /// </summary>
        [Fact]
        public void TheTickTheDwellEndsIsAFrameChangeAndCostsNothing() {
            MovementValidator validator = BuildAllAboardValidator();
            PawnState held = At(new Vector3(0f, 0f, 0f), PawnState.WorldCarrierId);
            PawnState settled = At(OnTheDeck, SecondCarrierId);

            MovementVerdict verdict = validator.Validate(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                in held,
                in settled,
                TickInterval,
                true);

            Assert.Equal(MovementVerdictKind.Accepted, verdict.Kind);
            Assert.Equal(SecondCarrierId, verdict.ResolvedState.CarrierId);
        }

        /// <summary>
        /// A silence ended by a report on the <em>same</em> carrier is measured and refused, unless the
        /// owner says it is a resync — which is what the owner side now does.
        /// </summary>
        /// <remarks>
        /// This is the exit the withhold takes when the frame never changes on the wire: the owner boards
        /// a carrier that cannot be named, so nothing carrier-relative ever reaches the server, and the
        /// reports resume in world space under the same world id the server is already holding. Measured,
        /// the carried distance loses against a walking gait over the gap and the rejection hands the
        /// owner back the boarding pose — far enough away to be applied as an outright teleport, which is
        /// the player being snapped the length of their ride back up the track. Flagged, the same claim
        /// is adopted whole and costs one slot of the frame-change budget.
        /// </remarks>
        [Fact]
        public void ASilenceEndedOnTheSameCarrierIsRefusedUnlessTheOwnerFlagsAResync() {
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
            Report(world, owner, pawn, At(new Vector3(0.05f, 0f, 0f), PawnState.WorldCarrierId));

            float boardedX = pawn.State.Position.X;

            // Two seconds of silence while a thirty-metre-a-second consist carries the body sixty metres.
            world.Pump(WithheldTicks);
            Report(world, owner, pawn, At(new Vector3(boardedX + 60f, 0f, 0f), PawnState.WorldCarrierId));

            MovementVerdict verdict = Assert.Single(violations);
            Assert.Equal(MovementVerdictKind.Rejected, verdict.Kind);
            Assert.Equal(boardedX, verdict.ResolvedState.Position.X, 3);
            Assert.False(verdict.ResolvedState.IsCarrierRelative);
            Assert.Equal(boardedX, pawn.State.Position.X, 3);

            // The same claim, flagged the way NetActorSync flags its first report after a withhold.
            ReportResync(world, owner, pawn, At(new Vector3(boardedX + 60f, 0f, 0f), PawnState.WorldCarrierId));

            Assert.Single(violations);
            Assert.Equal(boardedX + 60f, pawn.State.Position.X, 3);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
        }

        /// <summary>
        /// A resync is bounded by the same budget a frame change is: the ones past it are measured like
        /// any other claim, so flagging every update buys nothing beyond the budget.
        /// </summary>
        /// <remarks>
        /// The trust boundary the flag opens is exactly the frame-change one — the server cannot check
        /// either — so it is given the same terms and no better ones. The two claimants share the window,
        /// which is why the budget is filled here with frame changes and spent by a resync: a resync
        /// cannot fill it on its own any more, because a second burst has to wait
        /// <c>MovementValidator.ResyncBurstTicks</c> and the claims in between are repeats rather than
        /// new resumptions.
        /// </remarks>
        [Fact]
        public void AResyncPastTheBudgetIsMeasuredLikeAnyOtherClaim() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(OnTheDeck, PawnState.WorldCarrierId));

            // Ticks 0 and 1 of the window: two frame changes, two slots.
            Report(world, owner, pawn, At(OnTheDeck, DeckCarrierId));
            world.Pump(1);
            Report(world, owner, pawn, At(OnTheDeck, SecondCarrierId));

            // Tick 2: a resumption opens a burst and takes the last slot, unmeasured and whole.
            world.Pump(1);
            ReportResync(world, owner, pawn, At(new Vector3(50f, 0f, 0f), SecondCarrierId));

            Assert.Equal(50f, pawn.State.Position.X, 3);
            Assert.Equal(MovementValidator.MaxCarrierSwitchesPerWindow, pawn.CarrierSwitchesInWindow);

            // Tick 6: a new burst inside the same window, with the budget spent. Measured, and thrown
            // away for the teleport it is.
            world.Pump(4);
            ReportResync(world, owner, pawn, At(new Vector3(100f, 0f, 0f), SecondCarrierId));

            Assert.Equal(50f, pawn.State.Position.X, 3);
        }

        /// <summary>
        /// A source dwelling for exactly <c>NetCarrier.SourceHysteresisSeconds</c> fills the switch
        /// budget inside one window, exactly and with nothing to spare.
        /// </summary>
        /// <remarks>
        /// The dwell is three ticks of a thirty-hertz send clock, and the window is eight ticks, so a
        /// conforming source's changes land on ticks 0, 3 and 6 — all inside one window, because it only
        /// reopens at elapsed ≥ 8. That is the arithmetic behind
        /// <c>MovementValidator.MaxCarrierSwitchesPerWindow</c> being three: honest play produces exactly
        /// this and the count cannot be lowered without lengthening the dwell first. A resync does not
        /// need a slot of its own here, because it cannot land inside this burst —
        /// <c>ResyncFlagReview5Tests.AResyncCannotLandInsideAConformingSourcesThreeChangeBurst</c> is
        /// where that is measured.
        /// </remarks>
        [Fact]
        public void ASourceDwellingTheMandatedMinimumFillsTheBudgetExactly() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(OnTheDeck, PawnState.WorldCarrierId));

            // Ticks 0, 3 and 6 of one eight-tick window: the fastest a source honouring the dwell can go.
            Report(world, owner, pawn, At(OnTheDeck, DeckCarrierId));
            world.Pump(3);
            Report(world, owner, pawn, At(OnTheDeck, SecondCarrierId));
            world.Pump(3);
            Report(world, owner, pawn, At(OnTheDeck, DeckCarrierId));

            Assert.Equal(3, pawn.CarrierSwitchesInWindow);
            Assert.Equal(MovementValidator.MaxCarrierSwitchesPerWindow, pawn.CarrierSwitchesInWindow);
            Assert.Equal(DeckCarrierId, pawn.State.CarrierId);

            // A fourth change inside the same window is one the dwell cannot have produced, and it is
            // refused: the pawn keeps the frame it was in.
            world.Pump(1);
            Report(world, owner, pawn, At(OnTheDeck, SecondCarrierId));

            Assert.Equal(DeckCarrierId, pawn.State.CarrierId);
        }

        /// <summary>
        /// The resync bit survives the wire, and an update without it decodes as an ordinary report.
        /// </summary>
        [Fact]
        public void TheResyncFlagSurvivesTheWire() {
            PawnState onADeck = At(OnTheDeck, DeckCarrierId);

            OwnerPawnUpdate plain = RoundTrip(new OwnerPawnUpdate(4u, 12u, in onADeck));
            OwnerPawnUpdate flagged = RoundTrip(
                new OwnerPawnUpdate(4u, 12u, OwnerPawnUpdate.ResyncFlag, in onADeck));

            Assert.False(plain.IsResync);
            Assert.True(flagged.IsResync);
            Assert.Equal(DeckCarrierId, flagged.State.CarrierId);
            Assert.Equal(12u, flagged.ClientTick);
        }

        private static MovementValidator BuildAllAboardValidator() {
            var config = new NetConfig {
                ServerTickRate = TicksPerSecond,
                ClientSendRate = TicksPerSecond,
                MovementToleranceMultiplier = 1.5f,
                MovementProfiles = new[] {
                    new MovementProfile {
                        DisplayName = "Player",
                        WalkSlowSpeed = 4f,
                        WalkSpeed = 4f,
                        JogSpeed = 8f,
                        SprintSpeed = 7f,
                        CrouchSpeed = 1.6f,
                        CrouchFastSpeed = 2.8f
                    }
                }
            };

            return new MovementValidator(config);
        }

        private static void Report(
            CarrierReplicationLoopbackWorld world,
            CarrierReplicationLoopbackClient owner,
            NetEntity pawn,
            PawnState claim) {
            var message = new OwnerPawnUpdate(pawn.Id, 1u, in claim);

            world.Replication.HandleOwnerPawnUpdate(in message, owner.ServerSidePeer);
        }

        /// <summary>Reports the way an owner does on its first update after a gap in reporting.</summary>
        private static void ReportResync(
            CarrierReplicationLoopbackWorld world,
            CarrierReplicationLoopbackClient owner,
            NetEntity pawn,
            PawnState claim) {
            var message = new OwnerPawnUpdate(pawn.Id, 1u, OwnerPawnUpdate.ResyncFlag, in claim);

            world.Replication.HandleOwnerPawnUpdate(in message, owner.ServerSidePeer);
        }

        private static OwnerPawnUpdate RoundTrip(in OwnerPawnUpdate message) {
            var buffer = new byte[256];
            var writer = new NetWriter(buffer);
            message.Serialize(ref writer);

            var reader = new NetReader(buffer, 0, writer.Written);
            var decoded = new OwnerPawnUpdate();
            decoded.Deserialize(ref reader);

            Assert.Equal(0, reader.Remaining);
            return decoded;
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
