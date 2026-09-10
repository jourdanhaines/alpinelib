using System;
using System.Collections.Generic;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Claims;
using AlpineLib.Netcode.Sessions.Claims.Messages;
using AlpineLib.Netcode.Transport;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The corners the claim pair is easiest to get wrong in: a listener that re-seats a slot from inside
    /// the departure that freed it, an identity that arrives after the verdict it explains, a client
    /// naming numbers the game never meant, and the leave that never reaches the transport.
    /// </summary>
    /// <remarks>
    /// Grown from the probes an adversarial review left behind. Each one pinned a defect at the time; they
    /// are kept, flipped to the fixed behaviour, because every one of them is a scenario a game reaches by
    /// doing something reasonable rather than by abusing the API.
    /// </remarks>
    public sealed class ClaimEdgeCaseTests {
        private static readonly PeerHandle Alice = new PeerHandle(1);
        private static readonly PeerHandle Bob = new PeerHandle(2);

        private const ushort DriverLever = 4;
        private const ushort BrakeLever = 5;

        /// <summary>
        /// The auto-seat case: a game watching for the driver's slot to go free takes the next one in the
        /// same breath. Every slot the departing peer held is already free by the first verdict, so a
        /// claim on a slot the loop has not published yet still succeeds.
        /// </summary>
        [Fact]
        public void AListenerReseatingALaterSlotDuringADepartureWins() {
            var peers = new List<PeerHandle> { Alice, Bob };
            using var transport = new FakeNetTransport();
            using var server = new NetServer(transport, BuildConfig());
            var registry = new ServerClaimRegistry(server, () => peers);

            registry.TryClaim(10, Alice);
            registry.TryClaim(11, Alice);

            bool tried = false;
            bool reseatSucceeded = false;
            registry.OnClaimChanged += (slot, holder) => {
                if (slot != 10 || holder != ServerClaimRegistry.FreeHolderPeerId || tried) {
                    return;
                }

                tried = true;
                reseatSucceeded = registry.TryClaim(11, Bob);
            };

            registry.ReleaseAllHeldBy(Alice);

            Assert.True(tried);
            Assert.True(reseatSucceeded);
            Assert.Equal(Bob.Id, registry.Holder(11));
        }

        /// <summary>
        /// The same re-seat, spelled out the long way by a game that releases before it claims. The
        /// departure loop must not publish a freeing that the listener has already undone, or the session
        /// would be told a slot is free that its new holder was just told it owns.
        /// </summary>
        [Fact]
        public void AReseatDuringADepartureIsNotOverwrittenByTheDepartureItself() {
            var peers = new List<PeerHandle> { Alice, Bob };
            using var transport = new FakeNetTransport();
            using var server = new NetServer(transport, BuildConfig());
            var registry = new ServerClaimRegistry(server, () => peers);

            registry.TryClaim(10, Alice);
            registry.TryClaim(11, Alice);

            bool tried = false;
            bool reseatSucceeded = false;
            var verdicts = new List<ClaimVerdict>();
            registry.OnClaimChanged += (slot, holder) => {
                verdicts.Add(new ClaimVerdict(slot, holder));

                if (slot != 10 || holder != ServerClaimRegistry.FreeHolderPeerId || tried) {
                    return;
                }

                tried = true;
                registry.Release(11, Alice);
                reseatSucceeded = registry.TryClaim(11, Bob);
            };

            registry.ReleaseAllHeldBy(Alice);

            Assert.True(reseatSucceeded);
            Assert.Equal(Bob.Id, registry.Holder(11));
            Assert.Equal(Bob.Id, verdicts[verdicts.Count - 1].HolderPeerId);
        }

        /// <summary>
        /// Why the graceful-leave release hangs off the leave notice and not off the departure event: the
        /// host retires the member — zeroing its peer id — before it announces the departure, so by then
        /// there is no handle left to free slots by.
        /// </summary>
        [Fact]
        public void AMemberLeftAnnouncementNoLongerCarriesThePeerId() {
            using var transport = new FakeNetTransport();
            using var server = new NetServer(transport, BuildConfig());
            var host = new SessionHost("s", "CODE", BuildSessionConfig(), server);
            host.Open();

            int seenPeerId = int.MinValue;
            host.OnMemberLeft += (member, reason) => seenPeerId = member.PeerId;

            host.AttachPeer(Alice, new PlayerIdentity(new PlayerId(Guid.NewGuid()), "Alice", AuthMethod.Anonymous));
            host.DetachPeer(Alice, LeaveReason.Quit);

            Assert.Equal(SessionMember.NoPeerId, seenPeerId);
            Assert.NotEqual(Alice.Id, seenPeerId);
        }

        /// <summary>
        /// A verdict that beats the roster to the client is not lost: adopting the peer id re-reads the
        /// map and grants what was already ours, so a game that learns its identity from its own plumbing
        /// still hears about the lever it is standing at.
        /// </summary>
        [Fact]
        public void AdoptingThePeerIdAfterAVerdictStillRaisesTheGrant() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient client = world.ConnectClient();

            // Undo the harness's early bind: this is a client that has not yet seen the roster.
            client.Claims.LocalPeerId = ClientClaims.FreeHolderPeerId;
            client.ClearLog();

            world.Registry.TryClaim(DriverLever, client.ServerSidePeer);
            world.Pump(4);

            Assert.Equal(client.ServerSidePeer.Id, client.Claims.Holder(DriverLever));
            Assert.Empty(client.Granted);

            client.Claims.LocalPeerId = client.ServerSidePeer.Id;

            Assert.True(client.Claims.IsHeldLocally(DriverLever));
            Assert.Equal(new ushort[] { DriverLever }, client.Granted);
            Assert.Empty(client.Lost);
        }

        /// <summary>
        /// Changing identity the other way — the peer id going back to unknown — reads as losing every
        /// slot that identity held, so nothing is left engaged under a name the client no longer answers
        /// to.
        /// </summary>
        [Fact]
        public void GivingUpThePeerIdReadsAsLosingWhatItHeld() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient client = world.ConnectClient();

            client.Claims.RequestClaim(DriverLever);
            world.Pump(4);
            client.ClearLog();

            client.Claims.LocalPeerId = ClientClaims.FreeHolderPeerId;

            Assert.Equal(new ushort[] { DriverLever }, client.Lost);
            Assert.Empty(client.Granted);
            Assert.False(client.Claims.IsHeldLocally(DriverLever));
        }

        /// <summary>
        /// Setting the peer id to what it already is says nothing: the session service adopts the id from
        /// two events, and a client must not be granted the same lever twice for it.
        /// </summary>
        [Fact]
        public void AdoptingThePeerIdTwiceRaisesNothingTheSecondTime() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient client = world.ConnectClient();

            client.Claims.RequestClaim(DriverLever);
            world.Pump(4);
            client.ClearLog();

            client.Claims.LocalPeerId = client.ServerSidePeer.Id;

            Assert.Empty(client.Granted);
            Assert.Empty(client.Lost);
        }

        /// <summary>Disposing the view reaches the game the same way a kick does, and only once.</summary>
        [Fact]
        public void DisposeReportsTheSlotsWeWereHoldingAsLostExactlyOnce() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient driver = world.ConnectClient();

            var lost = new List<ushort>();
            driver.Claims.OnClaimLost += (slot) => lost.Add(slot);

            driver.Claims.RequestClaim(DriverLever);
            world.Pump(4);

            driver.Claims.Dispose();
            driver.Claims.Dispose();

            Assert.Equal(new ushort[] { DriverLever }, lost);
        }

        /// <summary>
        /// A slot number arrives from a client, so one modded peer could otherwise turn a request loop
        /// into an entry in the server's map and a reliable broadcast to everybody, for the whole ushort
        /// space. The cap stops the map growing; the refusal is silent.
        /// </summary>
        [Fact]
        public void OneMemberCannotTakeMoreSlotsThanTheCap() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient greifer = world.ConnectClient();
            ClaimLoopbackClient victim = world.ConnectClient();
            ClearLogs(greifer, victim);

            for (ushort slot = 0; slot < 4096; slot++) {
                greifer.Claims.RequestClaim(slot);
            }

            world.Pump(8);

            Assert.Equal(ServerClaimRegistry.MaxSlots, world.Registry.Holders.Count);
            Assert.Equal(ServerClaimRegistry.MaxSlots, victim.Verdicts.Count);
        }

        /// <summary>
        /// A full map must not strand the peers already in it: re-stating a claim you hold is how a client
        /// recovers from a desync, and it names a slot the map already knows.
        /// </summary>
        [Fact]
        public void AFullRegistryStillAnswersAReclaimOfAHeldSlot() {
            var peers = new List<PeerHandle> { Alice, Bob };
            using var transport = new FakeNetTransport();
            using var server = new NetServer(transport, BuildConfig());
            var registry = new ServerClaimRegistry(server, () => peers);
            var request = new ClaimRequest(DriverLever);

            registry.HandleClaimRequest(in request, Alice);
            FillToCap(registry, Bob);

            Assert.Equal(ServerClaimRegistry.MaxSlots, registry.Holders.Count);

            registry.HandleClaimRequest(in request, Alice);

            Assert.Equal(Alice.Id, registry.Holder(DriverLever));
        }

        /// <summary>
        /// The game's own validator is the first gate: a train with four levers answers for four numbers,
        /// and a request naming a fifth costs the session nothing and is told nothing.
        /// </summary>
        [Fact]
        public void ASlotTheGameDoesNotUseIsRefusedSilently() {
            var peers = new List<PeerHandle> { Alice };
            using var transport = new FakeNetTransport();
            using var server = new NetServer(transport, BuildConfig());
            var registry = new ServerClaimRegistry(server, () => peers, (slot) => slot < 4);

            var verdicts = new List<ClaimVerdict>();
            registry.OnClaimChanged += (slot, holder) => verdicts.Add(new ClaimVerdict(slot, holder));

            var refused = new ClaimRequest(DriverLever);
            registry.HandleClaimRequest(in refused, Alice);

            Assert.False(registry.IsHeld(DriverLever));
            Assert.Empty(verdicts);

            var accepted = new ClaimRequest(3);
            registry.HandleClaimRequest(in accepted, Alice);

            Assert.Equal(Alice.Id, registry.Holder(3));
        }

        /// <summary>
        /// The validator gates what clients may ask for, not what the host may do. Seating a driver on
        /// match start is the host's own bookkeeping and goes through whatever number it likes.
        /// </summary>
        [Fact]
        public void TheValidatorDoesNotStandInTheHostsWay() {
            var peers = new List<PeerHandle> { Alice };
            using var transport = new FakeNetTransport();
            using var server = new NetServer(transport, BuildConfig());
            var registry = new ServerClaimRegistry(server, () => peers, (slot) => false);

            Assert.True(registry.TryClaim(DriverLever, Alice));
            Assert.Equal(Alice.Id, registry.Holder(DriverLever));
        }

        /// <summary>
        /// A keyframe is checked against the roster the way a request's sender is: a front desk that
        /// resolves the wrong session would otherwise hand an outsider this session's whole holder map.
        /// </summary>
        [Fact]
        public void AKeyframeToAPeerOutsideTheSessionSendsNothing() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient holder = world.ConnectClient();
            ClaimWireSpy outsider = world.ConnectSpy();

            world.Registry.TryClaim(DriverLever, holder.ServerSidePeer);
            world.Pump(2);

            world.SessionPeers.Remove(outsider.ServerSidePeer);
            outsider.ClearLog();

            world.Registry.SendKeyframeTo(outsider.ServerSidePeer);
            world.Pump(2);

            Assert.Empty(outsider.Received);
        }

        /// <summary>
        /// A transition and the join keyframe landing in the same tick must read as one event, whichever
        /// order the front desk emits them in: both ride ReliableOrdered and the client drops a verdict it
        /// already agrees with.
        /// </summary>
        [Fact]
        public void AKeyframeInterleavedWithATransitionReadsAsOneEvent() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient driver = world.ConnectClient();

            world.Registry.TryClaim(DriverLever, driver.ServerSidePeer);
            world.Pump(2);

            ClaimLoopbackClient joiner = world.ConnectClient();
            joiner.ClearLog();

            // The transition reaches the joiner before the keyframe restates it.
            world.Registry.TryClaim(BrakeLever, driver.ServerSidePeer);
            world.Registry.SendKeyframeTo(joiner.ServerSidePeer);
            world.Pump(4);

            Assert.Equal(2, joiner.Verdicts.Count);
            Assert.Equal(driver.ServerSidePeer.Id, joiner.Claims.Holder(DriverLever));
            Assert.Equal(driver.ServerSidePeer.Id, joiner.Claims.Holder(BrakeLever));
            Assert.Empty(joiner.Granted);
            Assert.Empty(joiner.Lost);
        }

        /// <summary>Takes the registry to its ceiling through the wire path, one slot at a time.</summary>
        private static void FillToCap(ServerClaimRegistry registry, PeerHandle peer) {
            for (ushort slot = 100; registry.Holders.Count < ServerClaimRegistry.MaxSlots; slot++) {
                var request = new ClaimRequest(slot);
                registry.HandleClaimRequest(in request, peer);
            }
        }

        private static void ClearLogs(params ClaimLoopbackClient[] clients) {
            for (int index = 0; index < clients.Length; index++) {
                clients[index].ClearLog();
            }
        }

        private static NetConfig BuildConfig() {
            return new NetConfig {
                GameProtocolName = "alpinelib-claim-edge",
                Port = 1,
                MaxPeers = 8,
                ServerTickRate = 30,
                SnapshotRate = 15,
                ClientSendRate = 30
            };
        }

        private static SessionConfigData BuildSessionConfig() {
            return new SessionConfigData {
                Profile = new SessionProfileData(),
                Lobby = new LobbyConfigData()
            };
        }
    }
}
