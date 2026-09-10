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
    /// Second-round adversarial probes: the corners the round-one fixes opened rather than closed.
    /// Re-entrancy through the new two-pass departure, an identity swapped from inside the events it
    /// raises, a game validator that misbehaves, the cap at its exact edge, and the graceful-leave
    /// ordering the front desk now depends on.
    /// </summary>
    public sealed class ClaimRound2ProbeTests {
        private static readonly PeerHandle Alice = new PeerHandle(1);
        private static readonly PeerHandle Bob = new PeerHandle(2);

        /// <summary>A listener re-seating the very slot whose freeing it is hearing about keeps it.</summary>
        [Fact]
        public void ReseatingTheSameSlotFromItsOwnFreeVerdictSticks() {
            var peers = new List<PeerHandle> { Alice, Bob };
            using var transport = new FakeNetTransport();
            using var server = new NetServer(transport, BuildConfig());
            var registry = new ServerClaimRegistry(server, () => peers);

            registry.TryClaim(10, Alice);
            registry.TryClaim(11, Alice);

            var verdicts = new List<ClaimVerdict>();
            bool tried = false;
            registry.OnClaimChanged += (slot, holder) => {
                verdicts.Add(new ClaimVerdict(slot, holder));

                if (slot != 10 || holder != ServerClaimRegistry.FreeHolderPeerId || tried) {
                    return;
                }

                tried = true;
                registry.TryClaim(10, Bob);
            };

            registry.ReleaseAllHeldBy(Alice);

            Assert.Equal(Bob.Id, registry.Holder(10));
            Assert.Equal(ServerClaimRegistry.FreeHolderPeerId, registry.Holder(11));
            Assert.Equal(
                new[] { "Claim(slot=10, holder=-1)", "Claim(slot=10, holder=2)", "Claim(slot=11, holder=-1)" },
                Describe(verdicts));
        }

        /// <summary>
        /// A listener that re-seats a not-yet-published slot and then hands it straight back has already
        /// announced the freeing itself, so the departure loop stays quiet about it.
        /// </summary>
        [Fact]
        public void AReseatHandedBackDuringADepartureIsFreedOnce() {
            var peers = new List<PeerHandle> { Alice, Bob };
            using var transport = new FakeNetTransport();
            using var server = new NetServer(transport, BuildConfig());
            var registry = new ServerClaimRegistry(server, () => peers);

            registry.TryClaim(10, Alice);
            registry.TryClaim(11, Alice);

            var verdicts = new List<ClaimVerdict>();
            bool tried = false;
            registry.OnClaimChanged += (slot, holder) => {
                verdicts.Add(new ClaimVerdict(slot, holder));

                if (slot != 10 || holder != ServerClaimRegistry.FreeHolderPeerId || tried) {
                    return;
                }

                tried = true;
                registry.TryClaim(11, Bob);
                registry.Release(11, Bob);
            };

            registry.ReleaseAllHeldBy(Alice);

            Assert.Equal(
                new[] {
                    "Claim(slot=10, holder=-1)",
                    "Claim(slot=11, holder=2)",
                    "Claim(slot=11, holder=-1)"
                },
                Describe(verdicts));
        }

        /// <summary>Freeing a peer's slots again from inside its own departure finds nothing left.</summary>
        [Fact]
        public void ReleaseAllHeldByReentersItselfWithoutRepeating() {
            var peers = new List<PeerHandle> { Alice, Bob };
            using var transport = new FakeNetTransport();
            using var server = new NetServer(transport, BuildConfig());
            var registry = new ServerClaimRegistry(server, () => peers);

            registry.TryClaim(10, Alice);
            registry.TryClaim(11, Alice);

            var verdicts = new List<ClaimVerdict>();
            registry.OnClaimChanged += (slot, holder) => {
                verdicts.Add(new ClaimVerdict(slot, holder));
                registry.ReleaseAllHeldBy(Alice);
            };

            registry.ReleaseAllHeldBy(Alice);

            Assert.Equal(2, verdicts.Count);
        }

        /// <summary>
        /// A client changing identity again from inside the loss that identity raised: the outer call's
        /// snapshot is stale by the time it is walked, and every slot in it is re-checked against the
        /// map before its event goes out, so only the grant the current identity really has is raised.
        /// </summary>
        [Fact]
        public void ChangingIdentityFromInsideALossOnlyGrantsWhatTheNewIdentityHolds() {
            using var world = new ClaimLoopbackWorld();
            ClaimLoopbackClient first = world.ConnectClient();
            ClaimLoopbackClient second = world.ConnectClient();

            ClientClaims view = first.Claims;
            int firstId = first.ServerSidePeer.Id;
            int secondId = second.ServerSidePeer.Id;

            world.Registry.TryClaim(5, first.ServerSidePeer);
            world.Registry.TryClaim(7, second.ServerSidePeer);
            world.Pump(4);

            var granted = new List<ushort>();
            bool swapped = false;
            view.OnClaimLost += (slot) => {
                if (swapped) {
                    return;
                }

                swapped = true;
                view.LocalPeerId = firstId;
            };
            view.OnClaimGranted += (slot) => granted.Add(slot);

            view.LocalPeerId = secondId;

            Assert.Equal(firstId, view.LocalPeerId);
            Assert.DoesNotContain<ushort>(7, granted);
            Assert.False(view.IsHeldLocally(7));

            // The nested adopt's own grant is honest and still goes out: slot 5 really is the first
            // identity's, which is the identity the view ends on.
            Assert.Contains<ushort>(5, granted);
            Assert.True(view.IsHeldLocally(5));
        }

        /// <summary>A game validator that throws takes the exception out through the router.</summary>
        [Fact]
        public void AValidatorThatThrowsEscapesThroughTheRouter() {
            var peers = new List<PeerHandle> { Alice };
            using var transport = new FakeNetTransport();
            using var server = new NetServer(transport, BuildConfig());
            var registry = new ServerClaimRegistry(
                server,
                () => peers,
                (slot, requester, claims) => throw new InvalidOperationException("bad slot table"));

            var request = new ClaimRequest(4);

            Assert.Throws<InvalidOperationException>(() => registry.HandleClaimRequest(in request, Alice));
        }

        /// <summary>The cap admits the last slot under it and refuses the one that would pass it.</summary>
        [Fact]
        public void TheCapAdmitsTheLastSlotAndRefusesTheNext() {
            var peers = new List<PeerHandle> { Alice };
            using var transport = new FakeNetTransport();
            using var server = new NetServer(transport, BuildConfig());
            var registry = new ServerClaimRegistry(server, () => peers);

            for (ushort slot = 0; slot < ServerClaimRegistry.MaxSlots - 1; slot++) {
                var filler = new ClaimRequest(slot);
                registry.HandleClaimRequest(in filler, Alice);
            }

            Assert.Equal(ServerClaimRegistry.MaxSlots - 1, registry.Holders.Count);

            var last = new ClaimRequest((ushort)(ServerClaimRegistry.MaxSlots - 1));
            registry.HandleClaimRequest(in last, Alice);

            Assert.Equal(ServerClaimRegistry.MaxSlots, registry.Holders.Count);

            var overflow = new ClaimRequest(ServerClaimRegistry.MaxSlots);
            registry.HandleClaimRequest(in overflow, Alice);

            Assert.Equal(ServerClaimRegistry.MaxSlots, registry.Holders.Count);
            Assert.False(registry.IsHeld(ServerClaimRegistry.MaxSlots));
        }

        /// <summary>
        /// The cap bounds the map, not the traffic: a peer that releases and re-claims produces a
        /// reliable broadcast per request for as long as it keeps asking.
        /// </summary>
        [Fact]
        public void TheCapDoesNotBoundHowManyVerdictsOneClientCanProvoke() {
            var peers = new List<PeerHandle> { Alice, Bob };
            using var transport = new FakeNetTransport();
            using var server = new NetServer(transport, BuildConfig());
            var registry = new ServerClaimRegistry(server, () => peers);

            int verdicts = 0;
            registry.OnClaimChanged += (slot, holder) => verdicts++;

            var request = new ClaimRequest(4);
            var release = new ClaimRelease(4);

            for (int round = 0; round < 5000; round++) {
                registry.HandleClaimRequest(in request, Alice);
                registry.HandleClaimRelease(in release, Alice);
            }

            Assert.Equal(10000, verdicts);
            Assert.Empty(registry.Holders);
        }

        /// <summary>
        /// A host seating peers itself is not capped, and a client can still re-state a claim it holds
        /// once the host has taken the map past the ceiling.
        /// </summary>
        [Fact]
        public void TheHostsOwnSeatingIsNotCappedAndDoesNotStrandItsHolders() {
            var peers = new List<PeerHandle> { Alice, Bob };
            using var transport = new FakeNetTransport();
            using var server = new NetServer(transport, BuildConfig());
            var registry = new ServerClaimRegistry(server, () => peers);

            for (int slot = 0; slot <= ServerClaimRegistry.MaxSlots; slot++) {
                Assert.True(registry.TryClaim((ushort)slot, Alice));
            }

            Assert.Equal(ServerClaimRegistry.MaxSlots + 1, registry.Holders.Count);

            var reclaim = new ClaimRequest(0);
            registry.HandleClaimRequest(in reclaim, Alice);

            Assert.Equal(Alice.Id, registry.Holder(0));
        }

        /// <summary>
        /// The order the front desk now uses on a graceful leave: free the slots while the leaver is
        /// still on the roster, then tell the host. The leaver hears its own slots go free, and the
        /// departure announcement that follows carries no peer id to free them by.
        /// </summary>
        [Fact]
        public void AGracefulLeaveFreesSlotsWhileTheLeaverIsStillOnTheRoster() {
            using var transport = new FakeNetTransport();
            using var server = new NetServer(transport, BuildConfig());
            var host = new SessionHost("s", "CODE", BuildSessionConfig(), server);
            host.Open();

            var registry = new ServerClaimRegistry(server, () => host.ConnectedPeers);
            host.AttachPeer(Alice, new PlayerIdentity(new PlayerId(Guid.NewGuid()), "Alice", AuthMethod.Anonymous));
            host.AttachPeer(Bob, new PlayerIdentity(new PlayerId(Guid.NewGuid()), "Bob", AuthMethod.Anonymous));

            registry.TryClaim(4, Alice);

            var audience = new List<int>();
            registry.OnClaimChanged += (slot, holder) => {
                for (int peerIndex = 0; peerIndex < registry.Peers.Count; peerIndex++) {
                    audience.Add(registry.Peers[peerIndex].Id);
                }
            };

            // Exactly what ListenServerFrontDesk.HandleLeaveNotice does, in its order.
            registry.ReleaseAllHeldBy(Alice);
            host.HandleLeaveNotice(Alice);

            Assert.Equal(new[] { Alice.Id, Bob.Id }, audience);
            Assert.False(registry.IsHeld(4));
            Assert.DoesNotContain(Alice, host.ConnectedPeers);
        }

        /// <summary>
        /// The wrong order still frees the slot but nobody who mattered hears it: proof the front desk's
        /// ordering is load-bearing rather than incidental.
        /// </summary>
        [Fact]
        public void FreeingAfterTheHostHasRetiredTheLeaverNeverReachesIt() {
            using var transport = new FakeNetTransport();
            using var server = new NetServer(transport, BuildConfig());
            var host = new SessionHost("s", "CODE", BuildSessionConfig(), server);
            host.Open();

            var registry = new ServerClaimRegistry(server, () => host.ConnectedPeers);
            host.AttachPeer(Alice, new PlayerIdentity(new PlayerId(Guid.NewGuid()), "Alice", AuthMethod.Anonymous));
            host.AttachPeer(Bob, new PlayerIdentity(new PlayerId(Guid.NewGuid()), "Bob", AuthMethod.Anonymous));

            registry.TryClaim(4, Alice);

            var audience = new List<int>();
            registry.OnClaimChanged += (slot, holder) => {
                for (int peerIndex = 0; peerIndex < registry.Peers.Count; peerIndex++) {
                    audience.Add(registry.Peers[peerIndex].Id);
                }
            };

            host.HandleLeaveNotice(Alice);
            registry.ReleaseAllHeldBy(Alice);

            Assert.Equal(new[] { Bob.Id }, audience);
        }

        /// <summary>
        /// A member rejoining is back on the roster before the session asks for its keyframe, so the new
        /// membership check on <c>SendKeyframeTo</c> cannot silence the path it guards.
        /// </summary>
        [Fact]
        public void AKeyframeRequestForARejoinerPassesTheMembershipCheck() {
            using var transport = new FakeNetTransport();
            using var server = new NetServer(transport, BuildConfig());
            var host = new SessionHost("s", "CODE", BuildSessionConfig(), server);
            host.Open();

            var registry = new ServerClaimRegistry(server, () => host.ConnectedPeers);
            var identity = new PlayerIdentity(new PlayerId(Guid.NewGuid()), "Alice", AuthMethod.Anonymous);

            var onRoster = new List<bool>();
            host.OnMemberNeedsKeyframe += (member) =>
                onRoster.Add(IsOnRoster(host.ConnectedPeers, member.PeerId));

            host.AttachPeer(Alice, identity);
            host.DetachPeer(Alice, LeaveReason.TransportLost);
            host.AttachPeer(Bob, identity);

            Assert.Equal(new[] { true, true }, onRoster);
            Assert.True(registry.TryClaim(4, Bob));
        }

        private static bool IsOnRoster(IReadOnlyList<PeerHandle> peers, int peerId) {
            for (int peerIndex = 0; peerIndex < peers.Count; peerIndex++) {
                if (peers[peerIndex].Id == peerId) {
                    return true;
                }
            }

            return false;
        }

        private static string[] Describe(List<ClaimVerdict> verdicts) {
            var lines = new string[verdicts.Count];

            for (int index = 0; index < verdicts.Count; index++) {
                lines[index] = verdicts[index].ToString();
            }

            return lines;
        }

        private static NetConfig BuildConfig() {
            return new NetConfig {
                GameProtocolName = "alpinelib-claim-round2",
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
