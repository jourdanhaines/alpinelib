using System.Collections.Generic;
using AlpineLib.Netcode.Sessions.Claims;
using AlpineLib.Netcode.Sessions.Claims.Messages;
using AlpineLib.Netcode.Transport;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The rules the claim authority enforces, asserted on the server side alone: who may take a slot,
    /// who may give it up, and what a peer that is not a member of the session is allowed to do.
    /// </summary>
    public sealed class ClaimRegistryTests {
        private const ushort DriverLever = 4;
        private const ushort BrakeLever = 5;

        [Fact]
        public void AFreeSlotIsHeldByNobody() {
            using var world = new ClaimLoopbackWorld();

            Assert.False(world.Registry.IsHeld(DriverLever));
            Assert.Equal(ServerClaimRegistry.FreeHolderPeerId, world.Registry.Holder(DriverLever));
            Assert.Empty(world.Registry.Holders);
        }

        [Fact]
        public void OnlyOnePeerCanHoldASlot() {
            using var world = new ClaimLoopbackWorld();
            PeerHandle first = SeatPeer(world, 1);
            PeerHandle second = SeatPeer(world, 2);

            Assert.True(world.Registry.TryClaim(DriverLever, first));
            Assert.False(world.Registry.TryClaim(DriverLever, second));
            Assert.Equal(first.Id, world.Registry.Holder(DriverLever));
        }

        [Fact]
        public void ReclaimingASlotYouHoldSucceedsWithoutASecondVerdict() {
            using var world = new ClaimLoopbackWorld();
            PeerHandle peer = SeatPeer(world, 1);
            List<ClaimVerdict> verdicts = RecordVerdicts(world.Registry);

            Assert.True(world.Registry.TryClaim(DriverLever, peer));
            Assert.True(world.Registry.TryClaim(DriverLever, peer));
            Assert.True(world.Registry.TryClaim(DriverLever, peer));

            Assert.Single(verdicts);
            Assert.Equal(peer.Id, verdicts[0].HolderPeerId);
        }

        [Fact]
        public void AnInvalidPeerCannotHoldASlot() {
            using var world = new ClaimLoopbackWorld();

            Assert.False(world.Registry.TryClaim(DriverLever, PeerHandle.None));
            Assert.False(world.Registry.IsHeld(DriverLever));
        }

        [Fact]
        public void OnlyTheHolderCanReleaseASlot() {
            using var world = new ClaimLoopbackWorld();
            PeerHandle holder = SeatPeer(world, 1);
            PeerHandle stranger = SeatPeer(world, 2);
            world.Registry.TryClaim(DriverLever, holder);

            Assert.False(world.Registry.Release(DriverLever, stranger));
            Assert.Equal(holder.Id, world.Registry.Holder(DriverLever));

            Assert.True(world.Registry.Release(DriverLever, holder));
            Assert.False(world.Registry.IsHeld(DriverLever));
        }

        [Fact]
        public void ReleasingAFreeSlotChangesNothing() {
            using var world = new ClaimLoopbackWorld();
            PeerHandle peer = SeatPeer(world, 1);
            List<ClaimVerdict> verdicts = RecordVerdicts(world.Registry);

            Assert.False(world.Registry.Release(DriverLever, peer));
            Assert.Empty(verdicts);
        }

        [Fact]
        public void ReleaseAllHeldByFreesEveryOneOfThatPeersSlotsAndNobodyElses() {
            using var world = new ClaimLoopbackWorld();
            PeerHandle leaving = SeatPeer(world, 1);
            PeerHandle staying = SeatPeer(world, 2);

            world.Registry.TryClaim(DriverLever, leaving);
            world.Registry.TryClaim(BrakeLever, staying);
            List<ClaimVerdict> verdicts = RecordVerdicts(world.Registry);

            world.Registry.ReleaseAllHeldBy(leaving);

            Assert.False(world.Registry.IsHeld(DriverLever));
            Assert.Equal(staying.Id, world.Registry.Holder(BrakeLever));
            Assert.Single(verdicts);
            Assert.Equal(DriverLever, verdicts[0].Slot);
            Assert.Equal(ServerClaimRegistry.FreeHolderPeerId, verdicts[0].HolderPeerId);
        }

        [Fact]
        public void ReleaseAllHeldByLeavesASlotFreeForTheNextTaker() {
            using var world = new ClaimLoopbackWorld();
            PeerHandle leaving = SeatPeer(world, 1);
            PeerHandle arriving = SeatPeer(world, 2);
            world.Registry.TryClaim(DriverLever, leaving);

            world.Registry.ReleaseAllHeldBy(leaving);

            Assert.True(world.Registry.TryClaim(DriverLever, arriving));
            Assert.Equal(arriving.Id, world.Registry.Holder(DriverLever));
        }

        /// <summary>
        /// A keyframe describes what is held and stays silent about what is not — a client starts with
        /// every slot free, so naming the free ones would grow the message with the game's slot space.
        /// </summary>
        [Fact]
        public void AKeyframeCarriesOneVerdictPerHeldSlotAndNothingForFreeOnes() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient holder = world.ConnectClient();
            ClaimWireSpy spy = world.ConnectSpy();

            world.Registry.TryClaim(DriverLever, holder.ServerSidePeer);
            world.Pump(2);
            spy.ClearLog();

            world.Registry.SendKeyframeTo(spy.ServerSidePeer);
            world.Pump(2);

            Assert.Single(spy.Received);
            Assert.Equal(DriverLever, spy.Received[0].Slot);
            Assert.Equal(holder.ServerSidePeer.Id, spy.Received[0].HolderPeerId);
        }

        [Fact]
        public void AKeyframeToAnInvalidPeerSendsNothing() {
            using var world = new ClaimLoopbackWorld();
            ClaimWireSpy spy = world.ConnectSpy();
            world.Registry.TryClaim(DriverLever, spy.ServerSidePeer);
            world.Pump(2);
            spy.ClearLog();

            world.Registry.SendKeyframeTo(PeerHandle.None);
            world.Pump(2);

            Assert.Empty(spy.Received);
        }

        /// <summary>
        /// The membership check is the whole reason a registry takes a peer source rather than reading
        /// the server's peer list: a connection attached to another session must not reach this one's
        /// slots by naming a number.
        /// </summary>
        [Fact]
        public void ARequestFromAPeerOutsideTheSessionIsIgnored() {
            using var world = new ClaimLoopbackWorld();
            var stranger = new PeerHandle(99);
            var request = new ClaimRequest(DriverLever);

            world.Registry.HandleClaimRequest(in request, stranger);

            Assert.False(world.Registry.IsHeld(DriverLever));
        }

        [Fact]
        public void AReleaseFromAPeerOutsideTheSessionIsIgnored() {
            using var world = new ClaimLoopbackWorld();
            PeerHandle holder = SeatPeer(world, 1);
            world.Registry.TryClaim(DriverLever, holder);

            world.SessionPeers.Remove(holder);
            var release = new ClaimRelease(DriverLever);
            world.Registry.HandleClaimRelease(in release, holder);

            Assert.Equal(holder.Id, world.Registry.Holder(DriverLever));
        }

        [Fact]
        public void TheHandlersActForAPeerTheSessionDoesContain() {
            using var world = new ClaimLoopbackWorld(attachToRouter: false);
            PeerHandle member = SeatPeer(world, 1);

            var request = new ClaimRequest(DriverLever);
            world.Registry.HandleClaimRequest(in request, member);
            Assert.Equal(member.Id, world.Registry.Holder(DriverLever));

            var release = new ClaimRelease(DriverLever);
            world.Registry.HandleClaimRelease(in release, member);
            Assert.False(world.Registry.IsHeld(DriverLever));
        }

        [Fact]
        public void AttachingIsIdempotentAndDetachingReleasesTheIds() {
            using var world = new ClaimLoopbackWorld(attachToRouter: false);

            Assert.False(world.Registry.IsAttachedToRouter);
            Assert.False(world.Server.Router.IsRegistered(ClaimMessageIds.ClaimRequest));

            world.Registry.AttachToRouter();
            world.Registry.AttachToRouter();

            Assert.True(world.Registry.IsAttachedToRouter);
            Assert.True(world.Server.Router.IsRegistered(ClaimMessageIds.ClaimRequest));
            Assert.True(world.Server.Router.IsRegistered(ClaimMessageIds.ClaimRelease));

            world.Registry.DetachFromRouter();
            world.Registry.DetachFromRouter();

            Assert.False(world.Registry.IsAttachedToRouter);
            Assert.False(world.Server.Router.IsRegistered(ClaimMessageIds.ClaimRequest));
            Assert.False(world.Server.Router.IsRegistered(ClaimMessageIds.ClaimRelease));
        }

        /// <summary>Puts a bare peer handle on the roster without standing a whole client up behind it.</summary>
        private static PeerHandle SeatPeer(ClaimLoopbackWorld world, int peerId) {
            var peer = new PeerHandle(peerId);
            world.SessionPeers.Add(peer);
            return peer;
        }

        private static List<ClaimVerdict> RecordVerdicts(ServerClaimRegistry registry) {
            var verdicts = new List<ClaimVerdict>();
            registry.OnClaimChanged += (slot, holderPeerId) => verdicts.Add(new ClaimVerdict(slot, holderPeerId));
            return verdicts;
        }
    }
}
