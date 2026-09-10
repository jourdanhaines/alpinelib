using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Claims;
using AlpineLib.Netcode.Sessions.Claims.Messages;
using AlpineLib.Netcode.Transport;
using AlpineLib.Server.Sessions;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The three things the library now promises a game's server module: it is told when its entry is
    /// finished, a throw out of it costs one session rather than the process, and its authority rule
    /// over claim slots is installed before the registry answers anybody.
    /// </summary>
    public sealed class SessionModuleContractTests {
        /// <summary>
        /// <c>Attached</c> runs after the entry's constructor has finished assembling it, and before the
        /// loop has stepped the session even once.
        /// </summary>
        [Fact]
        public void AModuleIsToldItsEntryIsFinishedBeforeTheFirstTick() {
            ContractModuleFactory factory = new ContractModuleFactory();

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(factory);
            HarnessClient player = harness.AddClient("Driver");

            OpenSession(harness, player);

            ContractModule module = factory.Modules[0];

            Assert.True(module.WasAttached);
            Assert.Equal(0, module.TicksBeforeAttach);
            Assert.NotNull(module.EntryAtAttach);
            Assert.NotNull(module.EntryAtAttach.Claims);
            Assert.NotNull(module.EntryAtAttach.Replication);
            Assert.NotNull(module.EntryAtAttach.Module);
        }

        /// <summary>
        /// A module that throws seating an arrival closes its own session and leaves the box — and every
        /// other session on it — running.
        /// </summary>
        [Fact]
        public void AModuleThatThrowsSeatingAnArrivalClosesOnlyItsOwnSession() {
            ContractModuleFactory factory = new ContractModuleFactory { ThrowsOnPeerJoined = true };

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(factory);
            HarnessClient owner = harness.AddClient("Driver");

            OpenSession(harness, owner);

            Assert.False(harness.WasStopRequested);
            Assert.True(harness.PumpUntil(() => harness.CaptureDirectory().Sessions.Count == 0),
                "The session the module faulted in was never retired.");
        }

        /// <summary>A module that throws mid-step is the same case: the loop keeps running.</summary>
        [Fact]
        public void AModuleThatThrowsMidStepDoesNotStopTheProcess() {
            ContractModuleFactory factory = new ContractModuleFactory { ThrowsOnTick = true };

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(factory);
            HarnessClient owner = harness.AddClient("Driver");

            OpenSession(harness, owner);
            harness.Pump(10);

            Assert.False(harness.WasStopRequested);
        }

        /// <summary>
        /// The validator the factory supplies is in force from the registry's first message, so a slot
        /// this game does not answer for is never granted.
        /// </summary>
        [Fact]
        public void TheFactorysClaimValidatorIsInForceBeforeTheFirstRequest() {
            ContractModuleFactory factory = new ContractModuleFactory { RefusesSlotsPast = 4 };

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(factory);
            HarnessClient player = harness.AddClient("Driver");

            OpenSession(harness, player);

            ServerClaimRegistry claims = factory.Modules[0].EntryAtAttach.Claims;
            PeerHandle peer = new PeerHandle(player.LocalPeerId);

            ClaimRequest pastTheEnd = new ClaimRequest(9);
            claims.HandleClaimRequest(in pastTheEnd, peer);
            Assert.False(claims.IsHeld(9));

            ClaimRequest answeredFor = new ClaimRequest(2);
            claims.HandleClaimRequest(in answeredFor, peer);
            Assert.Equal(player.LocalPeerId, claims.Holder(2));
        }

        private static void OpenSession(DedicatedServerHarness harness, HarnessClient client) {
            Assert.True(harness.Complete(client.Session.ConnectAsync(harness.Endpoint)).IsSuccess);
            harness.Complete(client.Session.CreateSessionAsync(string.Empty));
            harness.PumpUntil(() => client.HasLocalPeerId);
        }

        /// <summary>A module that records the new hooks and can be told to throw out of each callback.</summary>
        private sealed class ContractModule : ISessionModule {
            private readonly ContractModuleFactory _factory;
            private readonly SessionEntry _entry;

            public ContractModule(ContractModuleFactory factory, SessionEntry entry) {
                _factory = factory;
                _entry = entry;
            }

            /// <summary>True once <see cref="Attached"/> has run.</summary>
            public bool WasAttached { get; private set; }

            /// <summary>How many steps the loop had taken by the time the module was attached.</summary>
            public int TicksBeforeAttach { get; private set; } = -1;

            /// <summary>The entry as it read at the moment of attachment.</summary>
            public SessionEntry EntryAtAttach { get; private set; }

            /// <summary>How many times the loop stepped this module.</summary>
            public int TickCount { get; private set; }

            /// <inheritdoc />
            public void Attached() {
                WasAttached = true;
                TicksBeforeAttach = TickCount;
                EntryAtAttach = _entry;
            }

            /// <inheritdoc />
            public void Tick(uint serverTick, float deltaSeconds) {
                TickCount++;

                if (!_factory.ThrowsOnTick) return;

                throw new InvalidOperationException("The game cannot step this session.");
            }

            /// <inheritdoc />
            public void OnPeerJoined(PeerHandle peer) {
                if (!_factory.ThrowsOnPeerJoined) return;

                throw new InvalidOperationException("The game cannot seat this arrival.");
            }

            /// <inheritdoc />
            public void OnPeerLeft(PeerHandle peer) {
            }

            /// <inheritdoc />
            public void Dispose() {
            }
        }

        /// <summary>Builds <see cref="ContractModule"/>s and supplies the claim rule under test.</summary>
        private sealed class ContractModuleFactory : ISessionModuleFactory {
            /// <summary>Every module this factory built, in creation order.</summary>
            public List<ContractModule> Modules { get; } = new List<ContractModule>();

            /// <summary>Makes the module throw out of its step.</summary>
            public bool ThrowsOnTick { get; set; }

            /// <summary>Makes the module throw out of its arrival callback.</summary>
            public bool ThrowsOnPeerJoined { get; set; }

            /// <summary>Slot numbers at or past this are refused, or zero to answer for every slot.</summary>
            public int RefusesSlotsPast { get; set; }

            /// <inheritdoc />
            public ISessionModule Create(SessionEntry entry) {
                ContractModule module = new ContractModule(this, entry);
                Modules.Add(module);
                return module;
            }

            /// <inheritdoc />
            public void RegisterHandlers(MessageRouter router, Func<PeerHandle, SessionEntry> resolveEntry) {
            }

            /// <inheritdoc />
            public Func<ushort, PeerHandle, ServerClaimRegistry, bool> BuildClaimValidator(SessionHost host) {
                if (RefusesSlotsPast <= 0) return null;

                return (slot, requester, claims) => slot < RefusesSlotsPast;
            }
        }
    }
}
