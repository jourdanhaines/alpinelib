using System.Numerics;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Replication.Messages;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Round-8 verification of the one-adoption-per-tick guard: what the flood is bounded to, what an
    /// honest burst spread over three ticks costs, and what the guard does to a flagged send that shares
    /// a tick with an ordinary measured one.
    /// </summary>
    /// <remarks>
    /// The guard reads <c>NetEntity.LastDirtyTick</c>, which every claim that moves the pawn stamps —
    /// not only the resync path — so the interesting question is not the flood but the honest client
    /// whose transport bunched two sends into one tick interval.
    /// </remarks>
    public sealed class Wp1Review8Tests {
        private const int TicksPerSecond = 30;

        private static readonly Vector3 WireMaxVelocity =
            new Vector3(NetQuantization.MaxVelocityComponent, 0f, NetQuantization.MaxVelocityComponent);

        /// <summary>
        /// The round-7 attack in its worst shape — the flood shares the tick the burst was charged on,
        /// with no pump at all — is bounded to the opener alone.
        /// </summary>
        [Fact]
        public void AFloodOnTheChargedOpenersOwnTickAdoptsNothingPastTheOpener() {
            using var world = new CarrierReplicationLoopbackWorld();
            NetEntity pawn = SpawnOwnedPawn(world, out CarrierReplicationLoopbackClient owner);

            var violations = 0;
            world.Replication.OnMovementViolation += (entity, verdict) => violations++;

            world.Pump(1);
            ReportResync(world, owner, pawn, At(500f, WireMaxVelocity));

            Assert.Equal(500f, pawn.State.Position.X, 1);

            for (int index = 0; index < 200; index++) {
                ReportResync(world, owner, pawn, At(pawn.State.Position.X + RepeatBarMetres() - 0.01f, WireMaxVelocity));
            }

            Assert.Equal(500f, pawn.State.Position.X, 1);
            Assert.Equal(200, violations);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
        }

        /// <summary>
        /// A charged resync cannot chain into a second one on the same tick: adopting the first opens the
        /// burst, so every further flagged claim in that tick meets the guard rather than the budget.
        /// </summary>
        [Fact]
        public void AChargedResyncCannotChainIntoASecondOnTheSameTick() {
            using var world = new CarrierReplicationLoopbackWorld();
            NetEntity pawn = SpawnOwnedPawn(world, out CarrierReplicationLoopbackClient owner);

            world.Pump(1);
            ReportResync(world, owner, pawn, At(500f, Vector3.Zero));
            ReportResync(world, owner, pawn, At(5000f, Vector3.Zero));
            ReportResync(world, owner, pawn, At(50000f, Vector3.Zero));

            Assert.Equal(500f, pawn.State.Position.X, 1);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
        }

        /// <summary>
        /// The shipped client's three flagged sends, one per send tick, still cost the window one slot and
        /// raise nothing — the guard never sees them because they never share a tick.
        /// </summary>
        [Fact]
        public void AnHonestThreeSendBurstAcrossThreeTicksCostsOneSlot() {
            using var world = new CarrierReplicationLoopbackWorld();
            NetEntity pawn = SpawnOwnedPawn(world, out CarrierReplicationLoopbackClient owner);

            var violations = 0;
            world.Replication.OnMovementViolation += (entity, verdict) => violations++;

            // A rider who spent twenty seconds on a consist and stepped off it at deck speed: the server's
            // held pose is stale by a kilometre and the resumption announces the deck's velocity.
            var deckVelocity = new Vector3(30f, 0f, 0f);

            world.Pump(1);
            ReportResync(world, owner, pawn, At(1000f, deckVelocity));

            Assert.Equal(1000f, pawn.State.Position.X, 1);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);

            // Repeats two and three, each a send tick of deck travel later.
            for (int repeat = 1; repeat <= 2; repeat++) {
                world.Pump(1);
                ReportResync(world, owner, pawn, At(1000f + repeat * 1f, deckVelocity));
            }

            Assert.Equal(1002f, pawn.State.Position.X, 1);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
            Assert.Equal(0, violations);
        }

        /// <summary>
        /// The same three sends when each one is a measured refusal — the case the continuation bar exists
        /// for — still cost one slot and still raise nothing, one per tick.
        /// </summary>
        [Fact]
        public void ThreeRefusedRepeatsOneTickApartStillCostOneSlot() {
            using var world = new CarrierReplicationLoopbackWorld();
            NetEntity pawn = SpawnOwnedPawn(world, out CarrierReplicationLoopbackClient owner);

            var violations = 0;
            world.Replication.OnMovementViolation += (entity, verdict) => violations++;

            var deckVelocity = new Vector3(30f, 0f, 0f);
            float step = ContinuationBarMetres(deckVelocity) - 0.01f;

            world.Pump(1);
            ReportResync(world, owner, pawn, At(1000f, deckVelocity));

            for (int repeat = 0; repeat < 3; repeat++) {
                world.Pump(1);
                ReportResync(world, owner, pawn, At(pawn.State.Position.X + step, deckVelocity));
            }

            Assert.Equal(1000f + 3f * step, pawn.State.Position.X, 1);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
            Assert.Equal(0, violations);
        }

        /// <summary>
        /// The jitter case: inside an open burst, an ordinary measured update earlier in the same tick
        /// makes the guard refuse the first flagged send of that tick.
        /// </summary>
        [Fact]
        public void AMeasuredUpdateEarlierInTheTickRefusesTheFirstFlaggedSendOfThatTick() {
            using var world = new CarrierReplicationLoopbackWorld();
            NetEntity pawn = SpawnOwnedPawn(world, out CarrierReplicationLoopbackClient owner);

            var violations = 0;
            world.Replication.OnMovementViolation += (entity, verdict) => violations++;

            var deckVelocity = new Vector3(30f, 0f, 0f);

            world.Pump(1);
            ReportResync(world, owner, pawn, At(1000f, deckVelocity));

            Assert.Equal(1, pawn.CarrierSwitchesInWindow);

            world.Pump(1);

            // An ordinary sample, well inside the measured bar, lands first and moves the pawn.
            ReportPlain(world, owner, pawn, At(1000.1f, deckVelocity));
            Assert.Equal(1000.1f, pawn.State.Position.X, 1);
            Assert.Equal(0, violations);

            // The first flagged send of this same tick, sized so the continuation bar would pass it.
            float continuation = pawn.State.Position.X + ContinuationBarMetres(deckVelocity) - 0.01f;
            ReportResync(world, owner, pawn, At(continuation, deckVelocity));

            Assert.Equal(1000.1f, pawn.State.Position.X, 1);
            Assert.Equal(1, violations);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
        }

        /// <summary>
        /// The other order in the same tick is free, which is the ordering the sequenced owner channel
        /// actually delivers when the flagged sample is the older one.
        /// </summary>
        [Fact]
        public void AFlaggedSendArrivingBeforeTheMeasuredOneInATickIsStillFree() {
            using var world = new CarrierReplicationLoopbackWorld();
            NetEntity pawn = SpawnOwnedPawn(world, out CarrierReplicationLoopbackClient owner);

            var violations = 0;
            world.Replication.OnMovementViolation += (entity, verdict) => violations++;

            var deckVelocity = new Vector3(30f, 0f, 0f);

            world.Pump(1);
            ReportResync(world, owner, pawn, At(1000f, deckVelocity));

            world.Pump(1);
            float continuation = pawn.State.Position.X + ContinuationBarMetres(deckVelocity) - 0.01f;
            ReportResync(world, owner, pawn, At(continuation, deckVelocity));

            Assert.Equal(continuation, pawn.State.Position.X, 1);

            ReportPlain(world, owner, pawn, At(continuation + 0.1f, deckVelocity));

            Assert.Equal(continuation + 0.1f, pawn.State.Position.X, 1);
            Assert.Equal(0, violations);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
        }

        /// <summary>
        /// Outside the burst window the guard does not apply, so a measured update earlier in the tick
        /// does not delay a fresh resumption: it is charged and adopted.
        /// </summary>
        [Fact]
        public void OutsideTheWindowAMeasuredUpdateInTheTickDoesNotBlockAResync() {
            using var world = new CarrierReplicationLoopbackWorld();
            NetEntity pawn = SpawnOwnedPawn(world, out CarrierReplicationLoopbackClient owner);

            var deckVelocity = new Vector3(30f, 0f, 0f);

            world.Pump(1);
            ReportResync(world, owner, pawn, At(1000f, deckVelocity));

            world.Pump((int)MovementValidator.ResyncBurstTicks);

            ReportPlain(world, owner, pawn, At(1000.1f, deckVelocity));
            ReportResync(world, owner, pawn, At(2000f, deckVelocity));

            Assert.Equal(2000f, pawn.State.Position.X, 1);
            Assert.Equal(2, pawn.CarrierSwitchesInWindow);
        }

        /// <summary>
        /// The refusal the guard causes is self-recovering: the honest owner's next repeat, one tick on,
        /// is adopted for free with no further cost to the window.
        /// </summary>
        [Fact]
        public void AGuardedRefusalRecoversOnTheOwnersNextTick() {
            using var world = new CarrierReplicationLoopbackWorld();
            NetEntity pawn = SpawnOwnedPawn(world, out CarrierReplicationLoopbackClient owner);

            var violations = 0;
            world.Replication.OnMovementViolation += (entity, verdict) => violations++;

            var deckVelocity = new Vector3(30f, 0f, 0f);

            world.Pump(1);
            ReportResync(world, owner, pawn, At(1000f, deckVelocity));

            world.Pump(1);
            ReportPlain(world, owner, pawn, At(1000.1f, deckVelocity));

            float refused = pawn.State.Position.X + ContinuationBarMetres(deckVelocity) - 0.01f;
            ReportResync(world, owner, pawn, At(refused, deckVelocity));

            Assert.Equal(1000.1f, pawn.State.Position.X, 1);
            Assert.Equal(1, violations);

            world.Pump(1);
            ReportResync(world, owner, pawn, At(refused, deckVelocity));

            Assert.Equal(refused, pawn.State.Position.X, 1);
            Assert.Equal(1, violations);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
        }

        /// <summary>
        /// A byte-identical duplicate inside a burst stamps nothing, so it neither moves the pawn nor
        /// spends the tick's one free continuation.
        /// </summary>
        [Fact]
        public void AnIdenticalDuplicateDoesNotSpendTheTicksFreeContinuation() {
            using var world = new CarrierReplicationLoopbackWorld();
            NetEntity pawn = SpawnOwnedPawn(world, out CarrierReplicationLoopbackClient owner);

            var deckVelocity = new Vector3(30f, 0f, 0f);

            world.Pump(1);
            ReportResync(world, owner, pawn, At(1000f, deckVelocity));

            world.Pump(1);
            PawnState held = pawn.State;

            ReportResync(world, owner, pawn, held);
            Assert.Equal(1000f, pawn.State.Position.X, 1);

            float continuation = 1000f + ContinuationBarMetres(deckVelocity) - 0.01f;
            ReportResync(world, owner, pawn, At(continuation, deckVelocity));

            Assert.Equal(continuation, pawn.State.Position.X, 1);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
        }

        /// <summary>
        /// Claims refused inside the tick do not consume its free continuation: a refused claim writes the
        /// held state back, which stamps nothing, so an attacker gets unlimited attempts and one adoption.
        /// </summary>
        /// <remarks>
        /// This is the safe direction and it is what the guard should do, but it is not what
        /// <c>HasMovedThisTick</c>'s remark says — that sentence lists a refused claim among the ones that
        /// stamp, and <c>NetEntity.ApplyState</c> stamps only when the state actually changed.
        /// </remarks>
        [Fact]
        public void RefusedClaimsInTheTickDoNotConsumeItsFreeContinuation() {
            using var world = new CarrierReplicationLoopbackWorld();
            NetEntity pawn = SpawnOwnedPawn(world, out CarrierReplicationLoopbackClient owner);

            var violations = 0;
            world.Replication.OnMovementViolation += (entity, verdict) => violations++;

            var deckVelocity = new Vector3(30f, 0f, 0f);

            world.Pump(1);
            ReportResync(world, owner, pawn, At(1000f, deckVelocity));

            world.Pump(1);

            // Fifty over-the-bar claims, each refused and each raising a violation and a correction.
            for (int index = 0; index < 50; index++) {
                ReportResync(world, owner, pawn, At(9000f, deckVelocity));
            }

            Assert.Equal(1000f, pawn.State.Position.X, 1);
            Assert.Equal(50, violations);

            // The tick's one free continuation is still there to be taken.
            float continuation = 1000f + ContinuationBarMetres(deckVelocity) - 0.01f;
            ReportResync(world, owner, pawn, At(continuation, deckVelocity));

            Assert.Equal(continuation, pawn.State.Position.X, 1);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
        }

        /// <summary>
        /// A second of the hardest flood the resync path allows — fifty flagged datagrams every tick, each
        /// sized to the bar the wire's fastest velocity buys — carries the pawn a few hundred metres, not
        /// the hundred and sixty kilometres the same second bought before the guard.
        /// </summary>
        /// <remarks>
        /// The ceiling is the sum of the charged openers the budget allows and the one free continuation
        /// each of the ticks behind them yields, so it is a rate rather than the unbounded figure a
        /// per-datagram window gave. It is deliberately asserted as a range: the exact total depends on
        /// how the four-tick burst window and the eight-tick budget window phase against each other.
        /// </remarks>
        [Fact]
        public void ASecondOfTheHardestFloodIsBoundedToARate() {
            using var world = new CarrierReplicationLoopbackWorld();
            NetEntity pawn = SpawnOwnedPawn(world, out CarrierReplicationLoopbackClient owner);

            float bar = RepeatBarMetres();

            for (int tick = 0; tick < TicksPerSecond; tick++) {
                world.Pump(1);

                for (int datagram = 0; datagram < 50; datagram++) {
                    ReportResync(world, owner, pawn, At(pawn.State.Position.X + bar - 0.01f, WireMaxVelocity));
                }
            }

            Assert.InRange(pawn.State.Position.X, 0f, 1200f);
        }

        /// <summary>
        /// The frame-change path has no per-tick rule, so a client alternating carriers still spends its
        /// whole window budget inside one tick — three unmeasured teleports of any size.
        /// </summary>
        /// <remarks>
        /// This is the pre-existing exposure the budget is the only bound on, not something the
        /// one-per-tick guard changed: it is the shape of the hole a replicated carrier pose closes.
        /// </remarks>
        [Fact]
        public void TheFrameChangePathStillSpendsAWholeBudgetInOneTick() {
            using var world = new CarrierReplicationLoopbackWorld();
            NetEntity pawn = SpawnOwnedPawn(world, out CarrierReplicationLoopbackClient owner);

            world.Pump(1);

            for (int index = 0; index < 3; index++) {
                ushort carrier = (ushort)(index % 2 == 0 ? 7 : PawnState.WorldCarrierId);

                ReportPlain(world, owner, pawn, OnCarrier(100000f * (index + 1), carrier));
            }

            Assert.Equal(300000f, pawn.State.Position.X, 0);
            Assert.Equal(3, pawn.CarrierSwitchesInWindow);
        }

        /// <summary>The distance one free repeat may carry at a given held velocity, one tick apart.</summary>
        private static float ContinuationBarMetres(Vector3 heldVelocity) {
            float carriedSpeed = new Vector2(heldVelocity.X, heldVelocity.Z).Length() * 1.5f;
            float allowedDistance = carriedSpeed / TicksPerSecond + MovementValidator.PositionSlackMetres;

            return allowedDistance * MovementValidator.RejectDistanceRatio;
        }

        private static float RepeatBarMetres() {
            return ContinuationBarMetres(WireMaxVelocity);
        }

        private static NetEntity SpawnOwnedPawn(
            CarrierReplicationLoopbackWorld world,
            out CarrierReplicationLoopbackClient owner) {
            owner = world.ConnectClient();
            world.Pump(4);

            return world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(0f, Vector3.Zero));
        }

        private static void ReportResync(
            CarrierReplicationLoopbackWorld world,
            CarrierReplicationLoopbackClient owner,
            NetEntity pawn,
            PawnState claim) {
            var message = new OwnerPawnUpdate(pawn.Id, 1u, OwnerPawnUpdate.ResyncFlag, in claim);

            world.Replication.HandleOwnerPawnUpdate(in message, owner.ServerSidePeer);
        }

        private static void ReportPlain(
            CarrierReplicationLoopbackWorld world,
            CarrierReplicationLoopbackClient owner,
            NetEntity pawn,
            PawnState claim) {
            var message = new OwnerPawnUpdate(pawn.Id, 1u, in claim);

            world.Replication.HandleOwnerPawnUpdate(in message, owner.ServerSidePeer);
        }

        private static PawnState OnCarrier(float metresDownTheTrack, ushort carrierId) {
            return new PawnState(
                new Vector3(metresDownTheTrack, 0f, 0f),
                0f,
                Vector3.Zero,
                PawnState.PackFlags(WireLocomotion.Walk, false, true),
                carrierId);
        }

        private static PawnState At(float metresDownTheTrack, Vector3 velocity) {
            return new PawnState(
                new Vector3(metresDownTheTrack, 0f, 0f),
                0f,
                velocity,
                PawnState.PackFlags(WireLocomotion.Walk, false, true),
                PawnState.WorldCarrierId);
        }
    }
}
