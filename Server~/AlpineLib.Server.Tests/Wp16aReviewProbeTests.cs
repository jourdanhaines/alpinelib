using System;
using System.Collections.Generic;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication.StateChannel;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Claims;
using AlpineLib.Netcode.Sessions.Claims.Messages;
using AlpineLib.Netcode.Transport;
using AlpineLib.Server.Sessions;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// WP16a review probes: what the per-session fault guard really isolates, what a departing peer's
    /// module fault does to the slots it was holding, and how the dirty publish's unsequenced delivery
    /// treats a retirement that arrives late.
    /// </summary>
    /// <remarks>
    /// Written to break the round rather than to demonstrate it. The two facts that pinned defects —
    /// a late retirement deleting a live subject, and a tightened validator refusing its own holder —
    /// now pin the fixed behaviour instead.
    /// </remarks>
    public sealed class Wp16aReviewProbeTests {
        private const ushort ChannelMessageId = MessageIdBudget.GameBandStart;

        /// <summary>
        /// The claim the guard is sold on: one session's module throwing every step must leave a second
        /// session on the same process stepping.
        /// </summary>
        [Fact]
        public void ASecondSessionKeepsSteppingWhileTheFirstOnesModuleThrowsEveryStep() {
            FaultyModuleFactory factory = new FaultyModuleFactory { ThrowsOnTickForSession = 1 };

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(factory);
            HarnessClient first = harness.AddClient("Driver");
            HarnessClient second = harness.AddClient("Guard");

            OpenSession(harness, first);
            OpenSession(harness, second);

            Assert.Equal(2, factory.Modules.Count);

            FaultyModule healthy = factory.Modules[1];
            int stepsBefore = healthy.TickCount;

            harness.Pump(20);

            Assert.False(harness.WasStopRequested);
            Assert.True(healthy.TickCount > stepsBefore, "The healthy session stopped stepping.");
            Assert.True(harness.PumpUntil(() => harness.CaptureDirectory().Sessions.Count == 1),
                "The faulted session was never retired, or it took the healthy one with it.");
        }

        /// <summary>
        /// A module that throws retiring a departure closes its session — and the departing peer's slots
        /// were freed before the module ran, so the fault cannot strand them.
        /// </summary>
        [Fact]
        public void AModuleThatThrowsRetiringADepartureStillFreesTheSlotsThatPeerHeld() {
            FaultyModuleFactory factory = new FaultyModuleFactory { ThrowsOnPeerLeft = true };

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(factory);
            HarnessClient owner = harness.AddClient("Driver");
            HarnessClient rider = harness.AddClient("Rider");

            OpenSession(harness, owner);
            JoinSession(harness, rider, owner);

            SessionEntry entry = factory.Modules[0].Entry;
            PeerHandle riderPeer = new PeerHandle(rider.LocalPeerId);

            ClaimRequest request = new ClaimRequest(3);
            harness.Query(() => {
                entry.Claims.HandleClaimRequest(in request, riderPeer);
                return true;
            });

            Assert.Equal(rider.LocalPeerId, harness.Query(() => entry.Claims.Holder(3)));

            harness.Complete(rider.Session.LeaveAsync());
            harness.PumpUntil(() => factory.Modules[0].PeerLeftCount > 0);

            Assert.False(harness.WasStopRequested);
            Assert.False(harness.Query(() => entry.Claims.IsHeld(3)));
        }

        /// <summary>
        /// The reason a faulted session gives its members. Pinned because it is <c>HostClosed</c> — the
        /// same value a host who walked out sends — while the round added <c>ServerFault</c> for exactly
        /// this case and used it only for a creation fault.
        /// </summary>
        [Fact]
        public void AFaultedSessionTellsItsMembersTheHostClosedIt() {
            FaultyModuleFactory factory = new FaultyModuleFactory { ThrowsOnTickForSession = 1 };

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(factory);
            HarnessClient owner = harness.AddClient("Driver");

            OpenSession(harness, owner);
            harness.PumpUntil(() => owner.SessionEndings.Count > 0);

            Assert.NotEmpty(owner.SessionEndings);
            Assert.Equal(SessionEndReason.HostClosed, owner.SessionEndings[0]);
            Assert.DoesNotContain(SessionEndReason.ServerFault, owner.SessionEndings);
        }

        /// <summary>
        /// The "you already hold it" arm is answered before the validator, so a rule that tightens under
        /// a current holder's feet cannot answer that holder with a refusal for a slot it still holds.
        /// </summary>
        [Fact]
        public void AValidatorThatTurnsAgainstTheHolderStillAgreesTheSlotIsTheirs() {
            bool isOpen = true;

            using var world = new ClaimLoopbackWorld(isClaimAllowed: (slot, requester, claims) => isOpen);
            ClaimLoopbackClient holder = world.ConnectClient();

            world.Registry.TryClaim(7, holder.ServerSidePeer);
            world.Pump(2);

            isOpen = false;
            holder.ClearLog();

            holder.Claims.RequestClaim(7);
            world.Pump(4);

            Assert.Equal(holder.ServerSidePeer.Id, world.Registry.Holder(7));
            Assert.DoesNotContain<ushort>(7, holder.Denied);
            Assert.True(holder.Claims.IsHeldLocally(7), "The holder was told it lost a slot it still holds.");
        }

        /// <summary>
        /// A dirty publish is unsequenced, so a retirement can arrive after a restatement the server
        /// made later. The record tick is what orders them: the stale retirement is dropped and the
        /// subject stays.
        /// </summary>
        [Fact]
        public void ALateRetirementLeavesASubjectARecentPublishHadAlreadyRestated() {
            using var client = new ProbeChannelClient();

            client.Deliver(101u, new StateChannelRecord<StateChannelTestState>(
                5, 101u, new StateChannelTestState(12f, 3f, 1)));

            Assert.True(client.Channel.TryGet(5, out _, out uint tick));
            Assert.Equal(101u, tick);

            client.Deliver(100u, StateChannelRecord<StateChannelTestState>.Retired(5, 100u));

            Assert.True(client.Channel.TryGet(5, out _, out uint heldTick));
            Assert.Equal(101u, heldTick);
            Assert.DoesNotContain<ushort>(5, client.Removals);
        }

        /// <summary>
        /// The other edge of the same rule: a retirement newer than the state held for its subject is
        /// still applied, so the server's ordinary remove path is untouched by the tick gate.
        /// </summary>
        [Fact]
        public void ARetirementNewerThanTheHeldStateStillDropsTheSubject() {
            using var client = new ProbeChannelClient();

            client.Deliver(100u, new StateChannelRecord<StateChannelTestState>(
                5, 100u, new StateChannelTestState(12f, 3f, 1)));

            client.Deliver(101u, StateChannelRecord<StateChannelTestState>.Retired(5, 101u));

            Assert.False(client.Channel.TryGet(5, out _, out _));
            Assert.Contains<ushort>(5, client.Removals);
        }

        private static void OpenSession(DedicatedServerHarness harness, HarnessClient client) {
            Assert.True(harness.Complete(client.Session.ConnectAsync(harness.Endpoint)).IsSuccess);
            harness.Complete(client.Session.CreateSessionAsync(string.Empty));
            harness.PumpUntil(() => client.HasLocalPeerId);
        }

        private static void JoinSession(DedicatedServerHarness harness, HarnessClient client, HarnessClient owner) {
            Assert.True(harness.Complete(client.Session.ConnectAsync(harness.Endpoint)).IsSuccess);
            harness.Complete(client.Session.JoinSessionAsync(owner.Session.JoinCode));
            harness.PumpUntil(() => client.HasLocalPeerId);
        }

        /// <summary>A module that can be told to throw out of one session's step or out of a departure.</summary>
        private sealed class FaultyModule : ISessionModule {
            private readonly FaultyModuleFactory _factory;
            private readonly int _ordinal;

            public FaultyModule(FaultyModuleFactory factory, SessionEntry entry, int ordinal) {
                _factory = factory;
                _ordinal = ordinal;
                Entry = entry;
            }

            /// <summary>The entry this module was built for.</summary>
            public SessionEntry Entry { get; }

            /// <summary>How many times the loop stepped this module.</summary>
            public int TickCount { get; private set; }

            /// <summary>How many departures reached this module, throwing ones included.</summary>
            public int PeerLeftCount { get; private set; }

            /// <inheritdoc />
            public void Tick(uint serverTick, float deltaSeconds) {
                TickCount++;

                if (_factory.ThrowsOnTickForSession != _ordinal) return;

                throw new InvalidOperationException("The game cannot step this session.");
            }

            /// <inheritdoc />
            public void OnPeerJoined(PeerHandle peer) {
            }

            /// <inheritdoc />
            public void OnPeerLeft(PeerHandle peer) {
                PeerLeftCount++;

                if (!_factory.ThrowsOnPeerLeft) return;

                throw new InvalidOperationException("The game cannot retire this departure.");
            }

            /// <inheritdoc />
            public void Dispose() {
            }
        }

        /// <summary>Builds <see cref="FaultyModule"/>s and decides which of them misbehaves.</summary>
        private sealed class FaultyModuleFactory : ISessionModuleFactory {
            /// <summary>Every module built, in creation order.</summary>
            public List<FaultyModule> Modules { get; } = new List<FaultyModule>();

            /// <summary>One-based ordinal of the session whose step throws, or zero for none.</summary>
            public int ThrowsOnTickForSession { get; set; }

            /// <summary>Makes every module throw out of its departure callback.</summary>
            public bool ThrowsOnPeerLeft { get; set; }

            /// <inheritdoc />
            public ISessionModule Create(SessionEntry entry) {
                FaultyModule module = new FaultyModule(this, entry, Modules.Count + 1);
                Modules.Add(module);
                return module;
            }

            /// <inheritdoc />
            public void RegisterHandlers(MessageRouter router, Func<PeerHandle, SessionEntry> resolveEntry) {
            }
        }

        /// <summary>A client channel fed hand-built envelopes in whatever order the probe wants.</summary>
        private sealed class ProbeChannelClient : IDisposable {
            private readonly FakeNetTransport _transport = new FakeNetTransport();
            private readonly NetClient _client;
            private readonly List<ushort> _removals = new List<ushort>();
            private readonly byte[] _buffer = new byte[NetBufferPool.DefaultBufferSize];

            public ProbeChannelClient() {
                _client = new NetClient(_transport, new NetConfig());
                Channel = new ClientStateChannel<StateChannelTestState>(_client, ChannelMessageId);
                Channel.Removed += RecordRemoval;
            }

            /// <summary>The channel under probe.</summary>
            public ClientStateChannel<StateChannelTestState> Channel { get; }

            /// <summary>Every subject the channel reported retired, in order.</summary>
            public IReadOnlyList<ushort> Removals => _removals;

            /// <summary>Hands the channel one envelope, as if it had just come off the wire.</summary>
            public void Deliver(uint serverTick, params StateChannelRecord<StateChannelTestState>[] records) {
                var envelope = new StateChannelEnvelope<StateChannelTestState>(
                    serverTick, new List<StateChannelRecord<StateChannelTestState>>(records));

                var writer = new NetWriter(_buffer);
                writer.WriteMessage(envelope);

                var reader = new NetReader(_buffer, 0, writer.Written);
                Assert.True(_client.Router.Dispatch(ChannelMessageId, ref reader, PeerHandle.None));
            }

            public void Dispose() {
                Channel.Dispose();
                _client.Dispose();
            }

            private void RecordRemoval(ushort id) {
                _removals.Add(id);
            }
        }
    }
}
