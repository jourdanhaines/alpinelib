using System;
using System.Collections.Generic;
using System.Linq;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Transport;
using AlpineLib.Server.Configuration;
using AlpineLib.Server.Hosting;
using AlpineLib.Server.Sessions;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// A dedicated server asks the game's module factory which prefab each arrival is spawned as, and
    /// a factory that does not answer leaves the session config's prefab in force.
    /// </summary>
    public sealed class SessionPawnPrefabResolverTests {
        private const ushort ConfiguredPrefabId = 5;
        private const ushort ResolvedPrefabId = 11;

        [Fact]
        public void TheFactorysResolverChoosesTheArrivalsPrefab() {
            var factory = new PrefabModuleFactory(overridesPrefab: true);

            NetEntity pawn = SpawnOnePawn(factory, out SessionEntry entry);

            Assert.Equal(ResolvedPrefabId, pawn.PrefabId);
            Assert.Equal(new[] { ConfiguredPrefabId }, factory.DefaultsSeen);
            Assert.Equal(ConfiguredPrefabId, entry.Spawner.PrefabId);
        }

        [Fact]
        public void AFactoryThatDoesNotResolveKeepsTheConfiguredPrefab() {
            var factory = new PrefabModuleFactory(overridesPrefab: false);

            NetEntity pawn = SpawnOnePawn(factory, out SessionEntry _);

            Assert.Equal(ConfiguredPrefabId, pawn.PrefabId);
        }

        private static NetEntity SpawnOnePawn(PrefabModuleFactory factory, out SessionEntry entry) {
            ServerConfigBundle config = DedicatedServerHarness.BuildConfig(
                new SpawnSettingsDocument { PawnPrefabId = ConfiguredPrefabId });

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(config, new ServerRuntimeOptions(), factory);
            HarnessClient player = harness.AddClient("Driver");

            Assert.True(harness.Complete(player.Session.ConnectAsync(harness.Endpoint)).IsSuccess);
            harness.Complete(player.Session.CreateSessionAsync(string.Empty));
            Assert.True(harness.PumpUntil(() => player.HasLocalPeerId), "The player was never seated.");

            SessionEntry sessionEntry = factory.Entries[0];
            entry = sessionEntry;

            return harness.Query(() => sessionEntry.Replication.Entities.Entities.Single());
        }

        /// <summary>A module that does nothing, so the test is only about the factory's prefab answer.</summary>
        private sealed class IdleModule : ISessionModule {
            /// <inheritdoc />
            public void Tick(uint serverTick, float deltaSeconds) {
            }

            /// <inheritdoc />
            public void OnPeerJoined(PeerHandle peer) {
            }

            /// <inheritdoc />
            public void OnPeerLeft(PeerHandle peer) {
            }

            /// <inheritdoc />
            public void Dispose() {
            }
        }

        /// <summary>Records the default it is offered and, when told to, answers with its own prefab.</summary>
        private sealed class PrefabModuleFactory : ISessionModuleFactory {
            private readonly bool _overridesPrefab;

            public PrefabModuleFactory(bool overridesPrefab) {
                _overridesPrefab = overridesPrefab;
            }

            /// <summary>Every entry this factory built a module for, in creation order.</summary>
            public List<SessionEntry> Entries { get; } = new List<SessionEntry>();

            /// <summary>The default prefab the library offered on each arrival.</summary>
            public List<ushort> DefaultsSeen { get; } = new List<ushort>();

            /// <inheritdoc />
            public ISessionModule Create(SessionEntry entry) {
                Entries.Add(entry);
                return new IdleModule();
            }

            /// <inheritdoc />
            public void RegisterHandlers(MessageRouter router, Func<PeerHandle, SessionEntry> resolveEntry) {
            }

            /// <inheritdoc />
            public ushort ResolvePawnPrefab(SessionMember member, ushort defaultPrefabId) {
                if (!_overridesPrefab) {
                    return ((ISessionModuleFactory)new NonResolvingFactory()).ResolvePawnPrefab(member, defaultPrefabId);
                }

                DefaultsSeen.Add(defaultPrefabId);
                return ResolvedPrefabId;
            }
        }

        /// <summary>A factory that leaves the prefab hook to its default implementation.</summary>
        private sealed class NonResolvingFactory : ISessionModuleFactory {
            /// <inheritdoc />
            public ISessionModule Create(SessionEntry entry) {
                return new IdleModule();
            }

            /// <inheritdoc />
            public void RegisterHandlers(MessageRouter router, Func<PeerHandle, SessionEntry> resolveEntry) {
            }
        }
    }
}
