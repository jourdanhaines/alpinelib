using System;
using System.Collections.Generic;
using System.Threading;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Appearance;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication.StateChannel;
using AlpineLib.Netcode.Transport;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The appearance host driven against real clients over an in-memory transport: what it accepts,
    /// when an accepted outfit reaches the wire, and what a departed or recycled peer id leaves behind.
    /// </summary>
    public sealed class ServerAppearanceHostTests {
        private const ushort OutfitMessageId = MessageIdBudget.GameBandStart;
        private const float TickInterval = 1f / 30f;

        private static int nextPort = 49500;

        [Fact]
        public void AMessageIdTheLibraryAlreadySpeaksIsRefused() {
            using var world = new AppearanceWorld(AllocatePort());

            Assert.Throws<ArgumentException>(() => world.BuildHost(MessageIdBudget.ReplicationBandStart));
        }

        [Fact]
        public void AnAcceptedRequestReachesTheChannelOnTheNextTickStampedWithIt() {
            using var world = new AppearanceWorld(AllocatePort());
            AppearanceClient member = world.ConnectMember(AppearanceTestCatalog.ModelA);
            AppearanceOutfit outfit = HatOutfit(1);
            var changes = new List<(ushort Key, uint Tick)>();
            world.Host.OutfitChanged += (key, tick) => changes.Add((key, tick));

            Assert.True(world.Host.HandleRequest(member.ServerSidePeer, new AppearanceOutfitMessage(outfit)));
            Assert.False(world.Host.TryGetOutfit(member.Key, out _));
            Assert.False(world.Host.Channel.TryGet(member.Key, out _));

            world.Pump(1);
            uint adoptedTick = world.ServerTick;

            Assert.True(world.Host.TryGetOutfit(member.Key, out AppearanceOutfit held));
            Assert.Equal(outfit, held);
            Assert.Equal(new[] { (member.Key, adoptedTick) }, changes);

            world.Pump(4);

            Assert.True(member.Channel.TryGet(member.Key, out AppearanceOutfitMessage seen, out uint seenTick));
            Assert.Equal(outfit, seen.Outfit);
            Assert.Equal(adoptedTick, seenTick);
        }

        [Fact]
        public void ARefusedRequestRaisesTheEventAndChangesNothing() {
            using var world = new AppearanceWorld(AllocatePort());
            AppearanceClient member = world.ConnectMember(AppearanceTestCatalog.ModelA);
            var refusals = new List<string>();
            world.Host.RequestRefused += (sender, outfit, reason) => refusals.Add(reason);

            // Model A's hat slot holds two variants; asking for a third is outside the catalog.
            AppearanceOutfit outOfRange = HatOutfit(2);

            Assert.False(world.Host.HandleRequest(member.ServerSidePeer, new AppearanceOutfitMessage(outOfRange)));
            world.Pump(4);

            Assert.Single(refusals);
            Assert.False(string.IsNullOrEmpty(refusals[0]));
            Assert.False(world.Host.TryGetOutfit(member.Key, out _));
            Assert.Equal(0, world.Host.Channel.Count);
            Assert.Empty(member.Updates);
        }

        [Fact]
        public void TheLastRequestOfATickIsTheOneAdopted() {
            using var world = new AppearanceWorld(AllocatePort());
            AppearanceClient member = world.ConnectMember(AppearanceTestCatalog.ModelA);
            var changes = new List<ushort>();
            world.Host.OutfitChanged += (key, tick) => changes.Add(key);

            Assert.True(world.Host.HandleRequest(member.ServerSidePeer, new AppearanceOutfitMessage(HatOutfit(0))));
            Assert.True(world.Host.HandleRequest(member.ServerSidePeer, new AppearanceOutfitMessage(HatOutfit(1))));
            world.Pump(4);

            Assert.True(world.Host.TryGetOutfit(member.Key, out AppearanceOutfit held));
            Assert.Equal(HatOutfit(1), held);
            Assert.Single(changes);
            Assert.True(member.Channel.TryGet(member.Key, out AppearanceOutfitMessage seen, out uint _));
            Assert.Equal(HatOutfit(1), seen.Outfit);
        }

        [Fact]
        public void RestatingTheSameOutfitRaisesNoChange() {
            using var world = new AppearanceWorld(AllocatePort());
            AppearanceClient member = world.ConnectMember(AppearanceTestCatalog.ModelA);
            var changes = new List<ushort>();
            world.Host.OutfitChanged += (key, tick) => changes.Add(key);

            world.Host.HandleRequest(member.ServerSidePeer, new AppearanceOutfitMessage(HatOutfit(1)));
            world.Pump(1);
            world.Host.HandleRequest(member.ServerSidePeer, new AppearanceOutfitMessage(HatOutfit(1)));
            world.Pump(1);

            Assert.Single(changes);
        }

        [Fact]
        public void AJoinerIsCaughtUpByAKeyframeCarryingEveryOutfit() {
            using var world = new AppearanceWorld(AllocatePort());
            AppearanceClient first = world.ConnectMember(AppearanceTestCatalog.ModelA);
            AppearanceClient second = world.ConnectMember(AppearanceTestCatalog.ModelB);

            world.Host.HandleRequest(first.ServerSidePeer, new AppearanceOutfitMessage(HatOutfit(1)));
            world.Host.HandleRequest(second.ServerSidePeer, new AppearanceOutfitMessage(ModelBHatOutfit(2)));
            world.Pump(4);

            AppearanceClient late = world.ConnectMember(AppearanceTestCatalog.ModelA);
            Assert.Empty(late.Updates);

            world.Host.SendKeyframeTo(late.ServerSidePeer);
            world.Pump(1);

            Assert.True(late.Channel.TryGet(first.Key, out AppearanceOutfitMessage firstSeen, out uint _));
            Assert.Equal(HatOutfit(1), firstSeen.Outfit);
            Assert.True(late.Channel.TryGet(second.Key, out AppearanceOutfitMessage secondSeen, out uint _));
            Assert.Equal(ModelBHatOutfit(2), secondSeen.Outfit);
        }

        [Fact]
        public void ForgettingAPeerDropsItsOutfitAndItsQueuedRequest() {
            using var world = new AppearanceWorld(AllocatePort());
            AppearanceClient watcher = world.ConnectMember(AppearanceTestCatalog.ModelA);
            AppearanceClient leaver = world.ConnectMember(AppearanceTestCatalog.ModelA);

            world.Host.HandleRequest(leaver.ServerSidePeer, new AppearanceOutfitMessage(HatOutfit(0)));
            world.Pump(4);
            Assert.True(watcher.Channel.TryGet(leaver.Key, out AppearanceOutfitMessage _, out uint _));

            world.Host.HandleRequest(leaver.ServerSidePeer, new AppearanceOutfitMessage(HatOutfit(1)));
            world.Host.ForgetPeer(leaver.ServerSidePeer);
            world.Pump(4);

            Assert.False(world.Host.TryGetOutfit(leaver.Key, out _));
            Assert.False(world.Host.Channel.TryGet(leaver.Key, out _));
            Assert.Contains(leaver.Key, watcher.Removals);
            Assert.False(watcher.Channel.TryGet(leaver.Key, out AppearanceOutfitMessage _, out uint _));
        }

        /// <summary>
        /// The transport hands a freed peer id to the next arrival. Its first outfit must be stamped past
        /// the old holder's, or a client still holding that tick would drop it as a restatement.
        /// </summary>
        [Fact]
        public void ARecycledKeyPublishesUnderANewerTickAndIsSeen() {
            using var world = new AppearanceWorld(AllocatePort());
            AppearanceClient watcher = world.ConnectMember(AppearanceTestCatalog.ModelA);
            AppearanceClient holder = world.ConnectMember(AppearanceTestCatalog.ModelA);

            world.Host.HandleRequest(holder.ServerSidePeer, new AppearanceOutfitMessage(HatOutfit(0)));
            world.Pump(1);
            uint firstTick = world.ServerTick;

            // Forgotten and reused inside the same server tick: the queued request must wait for the next.
            world.Host.ForgetPeer(holder.ServerSidePeer);
            world.Host.HandleRequest(holder.ServerSidePeer, new AppearanceOutfitMessage(HatOutfit(1)));
            world.Host.Tick(firstTick, 0f);

            Assert.False(world.Host.TryGetOutfit(holder.Key, out _));

            world.Pump(4);

            Assert.True(world.Host.TryGetOutfit(holder.Key, out AppearanceOutfit held));
            Assert.Equal(HatOutfit(1), held);
            Assert.True(watcher.Channel.TryGet(holder.Key, out AppearanceOutfitMessage seen, out uint seenTick));
            Assert.Equal(HatOutfit(1), seen.Outfit);
            Assert.True(seenTick > firstTick);
        }

        [Fact]
        public void AnOutfitForAnotherModelIsRefused() {
            using var world = new AppearanceWorld(AllocatePort());
            AppearanceClient member = world.ConnectMember(AppearanceTestCatalog.ModelA);
            var refusals = new List<PeerHandle>();
            world.Host.RequestRefused += (sender, outfit, reason) => refusals.Add(sender);

            // Valid for model B in isolation, but this member spawned as model A.
            Assert.False(world.Host.HandleRequest(member.ServerSidePeer, new AppearanceOutfitMessage(ModelBHatOutfit(0))));
            world.Pump(2);

            Assert.Equal(new[] { member.ServerSidePeer }, refusals);
            Assert.Equal(0, world.Host.Channel.Count);
        }

        [Fact]
        public void APeerThatIsNotAMemberIsRefused() {
            using var world = new AppearanceWorld(AllocatePort());
            AppearanceClient stranger = world.ConnectMember(0);
            var reasons = new List<string>();
            world.Host.RequestRefused += (sender, outfit, reason) => reasons.Add(reason);

            Assert.False(world.Host.HandleRequest(stranger.ServerSidePeer, new AppearanceOutfitMessage(HatOutfit(0))));
            Assert.False(world.Host.HandleRequest(PeerHandle.None, new AppearanceOutfitMessage(HatOutfit(0))));
            world.Pump(2);

            Assert.Equal(2, reasons.Count);
            Assert.Equal(0, world.Host.Channel.Count);
        }

        private static AppearanceOutfit HatOutfit(byte variant) {
            return AppearanceOutfit.Create(AppearanceTestCatalog.ModelA, new[] {
                new AppearanceSlotPick(AppearanceTestCatalog.HatForA, variant),
                AppearanceSlotPick.Empty,
                AppearanceSlotPick.Empty,
                AppearanceSlotPick.Empty,
            });
        }

        private static AppearanceOutfit ModelBHatOutfit(byte variant) {
            return AppearanceOutfit.Create(AppearanceTestCatalog.ModelB, new[] {
                new AppearanceSlotPick(AppearanceTestCatalog.HatForB, variant),
                AppearanceSlotPick.Empty,
            });
        }

        private static int AllocatePort() {
            return Interlocked.Increment(ref nextPort);
        }

        private static NetConfig BuildConfig(int port) {
            return new NetConfig {
                GameProtocolName = "appearance-test",
                Port = port,
                MaxPeers = 8,
                ServerTickRate = 30,
                SnapshotRate = 15,
                ClientSendRate = 30,
            };
        }

        /// <summary>A server with one appearance host on it, plus every member watching its channel.</summary>
        private sealed class AppearanceWorld : IDisposable {
            private readonly int _port;
            private readonly FakeNetTransport _serverTransport = new FakeNetTransport();
            private readonly NetServer _server;
            private readonly Dictionary<int, ushort> _modelByPeer = new Dictionary<int, ushort>();
            private readonly List<AppearanceClient> _clients = new List<AppearanceClient>();

            public AppearanceWorld(int port) {
                _port = port;
                _server = new NetServer(_serverTransport, BuildConfig(port));
                Host = BuildHost(OutfitMessageId);
                _server.Start();
            }

            /// <summary>The host under test.</summary>
            public ServerAppearanceHost Host { get; }

            /// <summary>The tick the server is on.</summary>
            public uint ServerTick => _server.Tick;

            public ServerAppearanceHost BuildHost(ushort messageId) {
                return new ServerAppearanceHost(
                    _server,
                    () => _server.Peers,
                    BuildConfig(_port),
                    messageId,
                    AppearanceTestCatalog.Build(),
                    ExpectedModelOf);
            }

            /// <summary>Dials a client and records the model it spawned as; 0 leaves it outside the session.</summary>
            public AppearanceClient ConnectMember(ushort modelId) {
                var client = new AppearanceClient(BuildConfig(_port));
                _clients.Add(client);
                client.Connect(_port);

                for (int attempt = 0; attempt < 32 && !client.IsReady; attempt++) {
                    Pump(1);
                }

                Assert.True(client.IsReady, "The appearance-test client never finished connecting.");
                client.BindPeer(_server.Peers[_server.Peers.Count - 1]);
                _modelByPeer[client.ServerSidePeer.Id] = modelId;
                return client;
            }

            /// <summary>Advances the server, the host and every client by whole ticks.</summary>
            public void Pump(int ticks) {
                for (int tick = 0; tick < ticks; tick++) {
                    _server.Update(TickInterval);
                    Host.Tick(_server.Tick, TickInterval);

                    for (int clientIndex = 0; clientIndex < _clients.Count; clientIndex++) {
                        _clients[clientIndex].Update(TickInterval);
                    }
                }
            }

            public void Dispose() {
                for (int clientIndex = 0; clientIndex < _clients.Count; clientIndex++) {
                    _clients[clientIndex].Dispose();
                }

                _server.Dispose();
            }

            private ushort ExpectedModelOf(PeerHandle peer) {
                return _modelByPeer.TryGetValue(peer.Id, out ushort modelId) ? modelId : (ushort)0;
            }
        }

        /// <summary>A client that builds the outfit channel from the wire.</summary>
        private sealed class AppearanceClient : IDisposable {
            private readonly FakeNetTransport _transport = new FakeNetTransport();
            private readonly NetClient _client;
            private readonly List<ushort> _updates = new List<ushort>();
            private readonly List<ushort> _removals = new List<ushort>();

            public AppearanceClient(NetConfig config) {
                _client = new NetClient(_transport, config);
                Channel = new ClientStateChannel<AppearanceOutfitMessage>(_client, OutfitMessageId);
                Channel.Updated += RecordUpdate;
                Channel.Removed += RecordRemoval;
            }

            /// <summary>The channel built from this client's half of the wire.</summary>
            public ClientStateChannel<AppearanceOutfitMessage> Channel { get; }

            /// <summary>Subject key of every update the channel raised, in order.</summary>
            public IReadOnlyList<ushort> Updates => _updates;

            /// <summary>Every subject the channel reported retired, in order.</summary>
            public IReadOnlyList<ushort> Removals => _removals;

            /// <summary>The handle the server addresses this client by.</summary>
            public PeerHandle ServerSidePeer { get; private set; } = PeerHandle.None;

            /// <summary>The subject key this client's outfit is published under.</summary>
            public ushort Key => AppearanceSubjectKey.FromPeerId(ServerSidePeer.Id);

            /// <summary>True once the link is up.</summary>
            public bool IsReady => _client.IsConnected;

            public void Connect(int port) {
                _client.Connect(NetEndpoint.Direct("127.0.0.1", port));
            }

            public void BindPeer(PeerHandle peer) {
                ServerSidePeer = peer;
            }

            public void Update(float deltaSeconds) {
                _client.Update(deltaSeconds);
            }

            public void Dispose() {
                Channel.Dispose();
                _client.Dispose();
            }

            private void RecordUpdate(ushort key, AppearanceOutfitMessage state, uint tick) {
                _updates.Add(key);
            }

            private void RecordRemoval(ushort key) {
                _removals.Add(key);
            }
        }
    }
}
