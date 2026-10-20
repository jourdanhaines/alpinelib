using System.Numerics;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The holding-prop bit over a real transport: an owner reports it, the server judges and stores
    /// the state, and observers — including one who joins afterwards — sample it back.
    /// </summary>
    public sealed class HoldingPropLoopbackTests {
        [Fact]
        public void AnObserverSeesTheOwnerPickSomethingUp() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            CarrierReplicationLoopbackClient observer = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = SpawnOwned(world, owner);
            world.Pump(8);

            PawnState holding = Standing(true);
            owner.Replication.SubmitOwnerPawnState(pawn.Id, in holding);
            world.Pump(16);

            Assert.True(world.Replication.Entities.TryGet(pawn.Id, out NetEntity authoritative));
            Assert.True(authoritative.State.HoldingProp);
            Assert.True(observer.Replication.SampleRemote(pawn.Id, out PawnState sampled), "The observer never sampled the pawn.");
            Assert.True(sampled.HoldingProp);
            Assert.Empty(owner.Corrections);
        }

        [Fact]
        public void ALateJoinersFirstSampleIsAlreadyHolding() {
            using var world = new CarrierReplicationLoopbackWorld();
            CarrierReplicationLoopbackClient owner = world.ConnectClient();
            world.Pump(4);

            NetEntity pawn = SpawnOwned(world, owner);
            world.Pump(8);

            PawnState holding = Standing(true);
            owner.Replication.SubmitOwnerPawnState(pawn.Id, in holding);
            world.Pump(16);

            // The session's spawner sends a joiner the world as a keyframe; do the same here.
            CarrierReplicationLoopbackClient lateJoiner = world.ConnectClient();
            world.Replication.SendKeyframeTo(lateJoiner.ServerSidePeer);
            world.Pump(16);

            Assert.True(lateJoiner.Replication.SampleRemote(pawn.Id, out PawnState sampled), "The late joiner never sampled the pawn.");
            Assert.True(sampled.HoldingProp);
        }

        private static NetEntity SpawnOwned(CarrierReplicationLoopbackWorld world, CarrierReplicationLoopbackClient owner) {
            return world.Replication.SpawnEntity(
                CarrierReplicationLoopbackWorld.PawnPrefab,
                owner.ServerSidePeer.Id,
                AuthorityMode.OwnerClient,
                Standing(false));
        }

        private static PawnState Standing(bool holding) {
            var state = new PawnState(
                new Vector3(3f, 0f, -4f),
                0f,
                Vector3.Zero,
                PawnState.PackFlags(WireLocomotion.Walk, false, true));
            return state.WithHoldingProp(holding);
        }
    }
}
