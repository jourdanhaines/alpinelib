using System.Numerics;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Replication.Messages;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// What a free repeat inside a resync burst can be made to carry, when the velocity it is judged
    /// against is chosen by the client and when the client ignores the send rate it is assumed to keep.
    /// </summary>
    /// <remarks>
    /// <c>MovementValidator.IsResyncBurstContinuation</c> takes its bar from the greater of the gait
    /// ceiling and the speed the server already holds for the pawn, and the held speed arrived on a
    /// claim nothing validates. These pin the worst case that buys, so the numbers in the trust-boundary
    /// notes are measurements rather than estimates, and so a velocity sanity check has a test that
    /// changes when it lands.
    /// </remarks>
    public sealed class ResyncBurstReview7Tests {
        private const int TicksPerSecond = 30;

        /// <summary>
        /// The fastest planar velocity the wire can carry: both horizontal axes at the fixed-point
        /// ceiling <c>NetQuantization</c> clamps to. A client cannot report more than this however it
        /// lies, so it is the true bound on the burst bar.
        /// </summary>
        private static readonly Vector3 WireMaxVelocity =
            new Vector3(NetQuantization.MaxVelocityComponent, 0f, NetQuantization.MaxVelocityComponent);

        /// <summary>
        /// A burst opened by a paid claim carrying an absurd velocity buys three free repeats, and each
        /// one is bounded by that velocity rather than by the gait.
        /// </summary>
        /// <remarks>
        /// The opener is charged and is itself unbounded in distance, so the tail is not a new
        /// capability — this measures its size. At the wire's velocity ceiling one repeat carries about
        /// twenty-seven metres and a burst about eighty-two; against the sprint gait alone the same
        /// repeat would carry under a metre, which is what a velocity sanity check would leave.
        /// </remarks>
        [Fact]
        public void TheFreeTailOfABurstIsBoundedByTheWireVelocityCeiling() {
            using var world = new CarrierReplicationLoopbackWorld();
            NetEntity pawn = SpawnOwnedPawn(world, out CarrierReplicationLoopbackClient owner);

            // The opener: a refused teleport, charged one slot, that leaves the wire's fastest velocity
            // standing as the speed the server holds for this pawn.
            world.Pump(1);
            ReportResync(world, owner, pawn, At(500f, WireMaxVelocity));

            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
            Assert.Equal(500f, pawn.State.Position.X, 1);

            float bar = RepeatBarMetres();
            float travelled = 0f;

            for (int repeat = 0; repeat < 3; repeat++) {
                world.Pump(1);
                float next = pawn.State.Position.X + bar - 0.01f;

                ReportResync(world, owner, pawn, At(next, WireMaxVelocity));

                Assert.Equal(next, pawn.State.Position.X, 1);
                travelled += bar - 0.01f;
            }

            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
            Assert.InRange(bar, 27f, 28f);
            Assert.InRange(travelled, 80f, 82f);

            // The fourth flagged send is outside the window: it opens a new burst and is charged.
            world.Pump(1);
            ReportResync(world, owner, pawn, At(pawn.State.Position.X + 500f, WireMaxVelocity));

            Assert.Equal(2, pawn.CarrierSwitchesInWindow);
        }

        /// <summary>
        /// A repeat past the bar the held velocity sets is refused, so the free tail cannot be widened by
        /// claiming more distance than the announced speed covers.
        /// </summary>
        [Fact]
        public void ARepeatPastTheHeldVelocitysBarIsRefused() {
            using var world = new CarrierReplicationLoopbackWorld();
            NetEntity pawn = SpawnOwnedPawn(world, out CarrierReplicationLoopbackClient owner);

            world.Pump(1);
            ReportResync(world, owner, pawn, At(500f, WireMaxVelocity));

            world.Pump(1);
            ReportResync(world, owner, pawn, At(500f + RepeatBarMetres() + 1f, WireMaxVelocity));

            Assert.Equal(500f, pawn.State.Position.X, 1);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
        }

        /// <summary>
        /// The burst anchor is the send that paid, never a repeat, so a client cannot hold one paid burst
        /// open by flagging every send.
        /// </summary>
        [Fact]
        public void RepeatsDoNotSlideTheBurstAnchor() {
            using var world = new CarrierReplicationLoopbackWorld();
            NetEntity pawn = SpawnOwnedPawn(world, out CarrierReplicationLoopbackClient owner);

            world.Pump(1);
            ReportResync(world, owner, pawn, At(500f, WireMaxVelocity));

            uint anchor = pawn.LastAcceptedResyncTick;

            for (int repeat = 0; repeat < 3; repeat++) {
                world.Pump(1);
                ReportResync(world, owner, pawn, At(pawn.State.Position.X + RepeatBarMetres() - 0.01f, WireMaxVelocity));
            }

            Assert.Equal(anchor, pawn.LastAcceptedResyncTick);
            Assert.True(pawn.HasAcceptedResync);
        }

        /// <summary>
        /// A pawn spawned after a disconnect is a new entity, so no burst window survives a respawn or a
        /// rejoin.
        /// </summary>
        /// <remarks>
        /// The server never reassigns an entity's owner — a leaving peer's entities are removed and a
        /// rejoin spawns fresh ones under the next id — so the stamp cannot be inherited. The two new
        /// fields are initialised in <c>NetEntity</c>'s constructor and nothing else clears them, which
        /// is only correct while that stays true.
        /// </remarks>
        [Fact]
        public void ARespawnedPawnCarriesNoBurstWindow() {
            using var world = new CarrierReplicationLoopbackWorld();
            NetEntity first = SpawnOwnedPawn(world, out CarrierReplicationLoopbackClient owner);

            world.Pump(1);
            ReportResync(world, owner, first, At(500f, WireMaxVelocity));

            Assert.True(first.HasAcceptedResync);

            world.Replication.DespawnEntity(first.Id);

            NetEntity second = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                At(0f, Vector3.Zero));

            Assert.NotEqual(first.Id, second.Id);
            Assert.False(second.HasAcceptedResync);
            Assert.Equal(0u, second.LastAcceptedResyncTick);

            world.Pump(1);
            ReportResync(world, owner, second, At(500f, WireMaxVelocity));

            Assert.Equal(1, second.CarrierSwitchesInWindow);
        }

        /// <summary>
        /// A burst adopts at most one claim per server tick, so a client flooding flagged datagrams
        /// between two ticks buys one continuation and is refused loudly for every one after it.
        /// </summary>
        /// <remarks>
        /// How many datagrams share a tick is the sender's choice — the owner channel delivers every
        /// packet with a newer sequence and the server handles the whole arrival queue at one
        /// <c>currentTick</c> — so counting the window in ticks alone left the free side of a burst
        /// bounded by nothing: two hundred claims in one tick carried the pawn five kilometres for one
        /// charged slot, with no violation and no correction. Each refusal here raises one of each, which
        /// is the flood being measured like any other claim.
        /// </remarks>
        [Fact]
        public void OnlyOneContinuationPerTickIsFree() {
            using var world = new CarrierReplicationLoopbackWorld();
            NetEntity pawn = SpawnOwnedPawn(world, out CarrierReplicationLoopbackClient owner);

            var violations = 0;
            world.Replication.OnMovementViolation += (entity, verdict) => violations++;

            world.Pump(1);
            ReportResync(world, owner, pawn, At(500f, WireMaxVelocity));

            world.Pump(1);
            float continuation = pawn.State.Position.X + RepeatBarMetres() - 0.01f;

            ReportResync(world, owner, pawn, At(continuation, WireMaxVelocity));

            Assert.Equal(continuation, pawn.State.Position.X, 1);
            Assert.Equal(0, violations);

            // Two hundred more flagged claims with no pump between them: this tick has already moved the
            // pawn, so none of them is the resumption carried on.
            for (int index = 0; index < 200; index++) {
                ReportResync(world, owner, pawn, At(pawn.State.Position.X + RepeatBarMetres() - 0.01f, WireMaxVelocity));
            }

            Assert.Equal(continuation, pawn.State.Position.X, 1);
            Assert.Equal(200, violations);
            Assert.Equal(1, pawn.CarrierSwitchesInWindow);
        }

        /// <summary>
        /// The distance one free repeat may carry, worked out the way the validator does it, at the
        /// fastest velocity the wire can express and one tick of separation.
        /// </summary>
        private static float RepeatBarMetres() {
            float carriedSpeed = WireMaxVelocity.Length() * 1.5f;
            float allowedDistance = carriedSpeed / TicksPerSecond + MovementValidator.PositionSlackMetres;

            return allowedDistance * MovementValidator.RejectDistanceRatio;
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
