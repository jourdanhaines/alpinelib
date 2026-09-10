using System.Numerics;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The whole owner-client carrier path over a real transport: an owner reports a deck-local pose, the
    /// server judges and stores it, and a second client is told about the deck rather than about a place
    /// in the world.
    /// </summary>
    /// <remarks>
    /// Every hop between those two ends rebuilds a <see cref="PawnState"/> from parts — the validator's
    /// verdict, the entity's stored state, the snapshot record, the interpolator's blend — and each is a
    /// place a five-argument constructor can quietly become a four-argument one. A codec round trip
    /// cannot see that; only running the hops can.
    /// </remarks>
    public sealed class CarrierReplicationLoopbackTests {
        private const ushort TrainCarrierId = 7;

        [Fact]
        public void ACarrierRelativeOwnerUpdateReachesASecondClientWithItsCarrierIntact() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            CarrierReplicationLoopbackClient friend = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                Grounded(new Vector3(120f, 0f, -40f), PawnState.WorldCarrierId));
            world.Pump(8);

            PawnState onTheDeck = Grounded(new Vector3(0.4f, 0f, 2.1f), TrainCarrierId);
            owner.Replication.SubmitOwnerPawnState(pawn.Id, in onTheDeck);
            world.Pump(16);

            Assert.True(world.Replication.Entities.TryGet(pawn.Id, out NetEntity authoritative));
            Assert.Equal(TrainCarrierId, authoritative.State.CarrierId);

            NetEntity mirrored = friend.Replication.GetEntity(pawn.Id);
            Assert.NotNull(mirrored);
            Assert.Equal(TrainCarrierId, mirrored.State.CarrierId);
            Assert.Equal(onTheDeck.Position.Z, mirrored.State.Position.Z, 3);

            Assert.True(friend.Replication.SampleRemote(pawn.Id, out PawnState sampled), "The friend never sampled the pawn.");
            Assert.Equal(TrainCarrierId, sampled.CarrierId);
        }

        [Fact]
        public void SteppingOffACarrierIsReplicatedAsAFrameChangeNotAsAJump() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            CarrierReplicationLoopbackClient friend = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                Grounded(new Vector3(0.4f, 0f, 2.1f), TrainCarrierId));
            world.Pump(8);

            PawnState ashore = Grounded(new Vector3(120f, 0f, -40f), PawnState.WorldCarrierId);
            owner.Replication.SubmitOwnerPawnState(pawn.Id, in ashore);
            world.Pump(16);

            NetEntity mirrored = friend.Replication.GetEntity(pawn.Id);
            Assert.NotNull(mirrored);
            Assert.Equal(PawnState.WorldCarrierId, mirrored.State.CarrierId);
            Assert.Equal(ashore.Position.X, mirrored.State.Position.X, 3);
            Assert.Empty(owner.Corrections);
        }

        /// <summary>
        /// A clamp inside a carrier's frame comes back to the owner still wearing that frame, which is
        /// what the engine-side correction handler has to undo before it treats the numbers as a place.
        /// </summary>
        [Fact]
        public void AClampedCarrierRelativeMoveIsCorrectedBackInTheCarriersFrame() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                Grounded(new Vector3(0f, 0f, 0f), TrainCarrierId));
            world.Pump(8);

            // Far enough along the deck to be clamped, not far enough to be thrown away outright.
            PawnState overrun = Grounded(new Vector3(0f, 0f, 1.5f), TrainCarrierId);
            owner.Replication.SubmitOwnerPawnState(pawn.Id, in overrun);
            world.Pump(16);

            Assert.NotEmpty(owner.Corrections);
            PawnState correction = owner.Corrections[owner.Corrections.Count - 1];
            Assert.Equal(TrainCarrierId, correction.CarrierId);
            Assert.True(correction.Position.Z < overrun.Position.Z, "The overrun was not clamped at all.");
        }

        private static PawnState Grounded(Vector3 position, ushort carrierId) {
            return new PawnState(
                position,
                0f,
                Vector3.Zero,
                PawnState.PackFlags(WireLocomotion.Walk, false, true),
                carrierId);
        }
    }
}
