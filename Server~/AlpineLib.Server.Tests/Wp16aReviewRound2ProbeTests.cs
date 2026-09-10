using System;
using System.Collections.Generic;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication.StateChannel;
using AlpineLib.Netcode.Transport;
using AlpineLib.Server.Hosting;
using AlpineLib.Server.Sessions;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// WP16a round-two review probes: a state channel stamped from a clock that is not the server's,
    /// the strict comparison at the held tick, and the fault guard re-entered from a close.
    /// </summary>
    /// <remarks>
    /// Written to break the round-two fixes. Four of them found a live defect and are kept inverted, so
    /// they now pin the round-three contract that closed it: every tick a state channel handles is a
    /// server tick, and <c>Set</c> refuses anything that cannot be one.
    /// </remarks>
    public sealed class Wp16aReviewRound2ProbeTests {
        private const ushort ChannelMessageId = MessageIdBudget.GameBandStart;

        /// <summary>
        /// The resurrection that used to lose to the retirement it overtook never reaches the wire: a
        /// state stamped from a clock behind the server's is refused where the game sets it, not
        /// discovered on a client that has already deleted the subject.
        /// </summary>
        [Fact]
        public void AResurrectionStampedFromAGameClockIsRefusedRatherThanPublished() {
            using var server = new ProbeServerChannel();

            server.Channel.Set(5, Moving(10f), 1000u);
            server.Channel.BroadcastDirty(1000u);
            server.TakePublishIndex();

            server.Channel.Remove(5);
            server.Channel.BroadcastDirty(1001u);
            int retirementPublish = server.TakePublishIndex();

            Assert.Equal(1001u, server.RecordTick(retirementPublish, 5));

            ArgumentException refusal =
                Assert.Throws<ArgumentException>(() => server.Channel.Set(5, Moving(20f), 101u));
            Assert.Equal("tick", refusal.ParamName);
        }

        /// <summary>
        /// The other skew, where the retirement floor used to fire: a retirement now carries the tick of
        /// the publish that sends it, and a state stamped ahead of the server's counter is refused.
        /// </summary>
        [Fact]
        public void ARetirementCarriesThePublishTickAndAnAheadOfClockStateIsRefused() {
            using var server = new ProbeServerChannel();

            server.Channel.Set(5, Moving(10f), 10u);
            server.Channel.BroadcastDirty(10u);
            server.TakePublishIndex();

            server.Channel.Remove(5);
            server.Channel.BroadcastDirty(11u);
            int retirementPublish = server.TakePublishIndex();

            // No floor any more: the retirement is the publish's own tick, comparable with every state.
            Assert.Equal(11u, server.RecordTick(retirementPublish, 5));

            ArgumentException refusal =
                Assert.Throws<ArgumentException>(() => server.Channel.Set(5, Moving(20f), 200u));
            Assert.Equal("tick", refusal.ParamName);
        }

        /// <summary>
        /// A retirement is repeated on every dirty publish until a keyframe, and under the server-tick
        /// contract every repeat is older than the resurrection that followed it — so a repeat arriving
        /// late is a no-op rather than another chance to delete a live subject.
        /// </summary>
        [Fact]
        public void EveryRepeatOfARetirementLosesToTheResurrectionThatFollowedIt() {
            using var server = new ProbeServerChannel();

            server.Channel.Set(5, Moving(10f), 100u);
            server.Channel.BroadcastDirty(100u);
            int firstPublish = server.TakePublishIndex();

            server.Channel.Remove(5);
            server.Channel.BroadcastDirty(101u);
            server.TakePublishIndex();
            server.Channel.BroadcastDirty(102u);
            int repeatPublish = server.TakePublishIndex();

            Assert.Equal(102u, server.RecordTick(repeatPublish, 5));

            server.Channel.Set(5, Moving(20f), 103u);
            server.Channel.BroadcastDirty(103u);
            int resurrectionPublish = server.TakePublishIndex();

            using var client = new ProbeChannelClient();
            server.Replay(client, firstPublish);
            server.Replay(client, resurrectionPublish);
            server.Replay(client, repeatPublish);

            Assert.True(client.Channel.TryGet(5, out StateChannelTestState state, out _));
            Assert.Equal(20f, state.Distance);
            Assert.DoesNotContain<ushort>(5, client.Removals);
        }

        /// <summary>
        /// The shipped game's shape, for contrast: All Aboard stamps <c>Set</c> with the same server
        /// tick the publish is stamped with, so the retirement is always older than the resurrection
        /// and the round-two gate does what it says.
        /// </summary>
        [Fact]
        public void AGameStampingSetWithTheServerTickIsOrderedCorrectly() {
            using var server = new ProbeServerChannel();

            server.Channel.Set(5, Moving(10f), 100u);
            server.Channel.BroadcastDirty(100u);
            int firstPublish = server.TakePublishIndex();

            server.Channel.Remove(5);
            server.Channel.BroadcastDirty(101u);
            int retirementPublish = server.TakePublishIndex();

            server.Channel.Set(5, Moving(20f), 102u);
            server.Channel.BroadcastDirty(102u);
            int resurrectionPublish = server.TakePublishIndex();

            using var client = new ProbeChannelClient();
            server.Replay(client, firstPublish);
            server.Replay(client, resurrectionPublish);
            server.Replay(client, retirementPublish);

            Assert.True(client.Channel.TryGet(5, out StateChannelTestState state, out _));
            Assert.Equal(20f, state.Distance);
        }

        /// <summary>
        /// Every dirty publish rides one delivery class whatever its size, which is the half of finding
        /// one that did land.
        /// </summary>
        [Fact]
        public void EveryDirtyPublishRidesPlainUnreliableWhateverItsSize() {
            using var server = new ProbeServerChannel();

            server.Channel.Set(1, Moving(1f), 5u);
            server.Channel.BroadcastDirty(5u);

            for (ushort id = 2; id < 400; id++) {
                server.Channel.Set(id, Moving(id), 6u);
            }

            server.Channel.BroadcastDirty(6u);

            Assert.True(server.Transport.Deliveries.Count > 2, "The bulk publish never chunked.");
            Assert.All(server.Transport.Deliveries, delivery => Assert.Equal(DeliveryClass.Unreliable, delivery));
        }

        /// <summary>
        /// A module that throws out of every departure is re-entered while its own fault is closing the
        /// session. The close is idempotent, so the second fault is a log line rather than a recursion.
        /// </summary>
        [Fact]
        public void AModuleThrowingOnEveryDepartureDuringItsOwnCloseDoesNotRecurse() {
            var factory = new AlwaysThrowingDepartureFactory();

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(factory);
            HarnessClient owner = harness.AddClient("Driver");
            HarnessClient rider = harness.AddClient("Rider");
            HarnessClient second = harness.AddClient("Guard");

            Assert.True(harness.Complete(owner.Session.ConnectAsync(harness.Endpoint)).IsSuccess);
            harness.Complete(owner.Session.CreateSessionAsync(string.Empty));
            harness.PumpUntil(() => owner.HasLocalPeerId);

            Assert.True(harness.Complete(rider.Session.ConnectAsync(harness.Endpoint)).IsSuccess);
            harness.Complete(rider.Session.JoinSessionAsync(owner.Session.JoinCode));
            harness.PumpUntil(() => rider.HasLocalPeerId);

            Assert.True(harness.Complete(second.Session.ConnectAsync(harness.Endpoint)).IsSuccess);
            harness.Complete(second.Session.JoinSessionAsync(owner.Session.JoinCode));
            harness.PumpUntil(() => second.HasLocalPeerId);

            harness.Complete(rider.Session.LeaveAsync());
            harness.PumpUntil(() => factory.Modules[0].PeerLeftCount > 0);

            harness.Complete(second.Session.LeaveAsync());
            harness.Pump(30);

            Assert.False(harness.WasStopRequested);
            Assert.True(factory.Modules[0].PeerLeftCount >= 1);
        }

        /// <summary>
        /// The no-content-root arm: a relative path is handed back untouched rather than anchored on the
        /// working directory, and a rooted one is still normalised.
        /// </summary>
        [Fact]
        public void ResolvePathLeavesARelativePathAloneAndStillNormalisesARootedOne() {
            Assert.Equal("config/session.json", ServerRuntimeOptions.ResolvePath(string.Empty, "config/session.json"));
            Assert.Equal("config/session.json", ServerRuntimeOptions.ResolvePath("   ", "config/session.json"));
            Assert.Equal("./config/session.json", ServerRuntimeOptions.ResolvePath(null, "./config/session.json"));
            Assert.Equal(System.IO.Path.GetFullPath("/opt/server/session.json"),
                ServerRuntimeOptions.ResolvePath(string.Empty, "/opt/server/session.json"));
        }

        /// <summary>
        /// The no-content-root arm hands back the operator's own text unchecked, traversal segments and
        /// all, and leaves a path the platform refuses to be discovered when something opens it. The
        /// remark scopes its normalise-or-throw promise to the arms that keep it.
        /// </summary>
        [Fact]
        public void TheNoContentRootArmHandsBackTheOperatorsTextUnchecked() {
            Assert.Equal("config/../../etc/session.json",
                ServerRuntimeOptions.ResolvePath(string.Empty, "config/../../etc/session.json"));

            string malformed = "config/\0/session.json";
            Assert.Equal(malformed, ServerRuntimeOptions.ResolvePath(string.Empty, malformed));
            Assert.ThrowsAny<ArgumentException>(() => ServerRuntimeOptions.ResolvePath("/opt/server", malformed));
        }

        private static StateChannelTestState Moving(float distance) {
            return new StateChannelTestState(distance, 1f, 0);
        }

        /// <summary>A server channel whose datagrams are kept so a probe can replay them out of order.</summary>
        private sealed class ProbeServerChannel : IDisposable {
            private readonly NetServer _server;
            private int _taken;

            public ProbeServerChannel() {
                NetConfig config = new NetConfig {
                    GameProtocolName = "statechannel-review-r2",
                    Port = 0,
                    MaxPeers = 8,
                    ServerTickRate = 30,
                    SnapshotRate = 15,
                    ClientSendRate = 30,
                };

                Transport = new CapturingTransport();
                _server = new NetServer(Transport, config);
                Channel = new ServerStateChannel<StateChannelTestState>(
                    _server, () => new[] { new PeerHandle(1) }, ChannelMessageId, config);
            }

            /// <summary>The channel under probe.</summary>
            public ServerStateChannel<StateChannelTestState> Channel { get; }

            /// <summary>Everything the channel handed the transport.</summary>
            public CapturingTransport Transport { get; }

            /// <summary>The index of the datagram the publish just made, asserting it made exactly one.</summary>
            public int TakePublishIndex() {
                Assert.Equal(_taken + 1, Transport.Sent.Count);
                _taken = Transport.Sent.Count;
                return _taken - 1;
            }

            /// <summary>The tick a subject's record carries in one captured datagram.</summary>
            public uint RecordTick(int publishIndex, ushort id) {
                StateChannelEnvelope<StateChannelTestState> envelope = Decode(Transport.Sent[publishIndex]);

                for (int recordIndex = 0; recordIndex < envelope.Records.Count; recordIndex++) {
                    if (envelope.Records[recordIndex].Id == id) {
                        return envelope.Records[recordIndex].Tick;
                    }
                }

                throw new InvalidOperationException($"Publish {publishIndex} carries no record for subject {id}.");
            }

            /// <summary>Hands one captured datagram to a client, whenever the probe wants it to arrive.</summary>
            public void Replay(ProbeChannelClient client, int publishIndex) {
                client.Deliver(Transport.Sent[publishIndex]);
            }

            public void Dispose() {
                _server.Dispose();
            }

            private static StateChannelEnvelope<StateChannelTestState> Decode(byte[] payload) {
                var reader = new NetReader(payload, NetEnvelope.HeaderSize, payload.Length - NetEnvelope.HeaderSize);
                StateChannelEnvelope<StateChannelTestState> envelope = default;
                envelope.Deserialize(ref reader);
                return envelope;
            }
        }

        /// <summary>Keeps every payload handed to it, with the delivery class each went out on.</summary>
        private sealed class CapturingTransport : INetTransport {
            /// <summary>Payload bytes, one entry per send, framing included.</summary>
            public List<byte[]> Sent { get; } = new List<byte[]>();

            /// <summary>The delivery class of every send, in order.</summary>
            public List<DeliveryClass> Deliveries { get; } = new List<DeliveryClass>();

            /// <summary>Never raised: nothing connects to a transport that only records.</summary>
            public event Action<PeerHandle> OnPeerConnected { add { } remove { } }

            /// <summary>Never raised.</summary>
            public event Action<PeerHandle, DisconnectReason> OnPeerDisconnected { add { } remove { } }

            /// <summary>Never raised.</summary>
            public event Action<PeerHandle, ArraySegment<byte>, DeliveryClass> OnData { add { } remove { } }

            public void StartServer(int port, int maxPeers, string protocolKey) { }

            public void StartClient(string protocolKey) { }

            public void Connect(NetEndpoint endpoint) { }

            public void Disconnect(PeerHandle peer) { }

            public void Send(PeerHandle peer, ReadOnlySpan<byte> payload, DeliveryClass delivery) {
                Deliveries.Add(delivery);
                Sent.Add(payload.ToArray());
            }

            public void Stop() { }

            public void Poll() { }

            public int GetPingMs(PeerHandle peer) {
                return 0;
            }

            public void Dispose() { }
        }

        /// <summary>A client channel fed captured datagrams in whatever order the probe wants.</summary>
        private sealed class ProbeChannelClient : IDisposable {
            private readonly FakeNetTransport _transport = new FakeNetTransport();
            private readonly NetClient _client;
            private readonly List<ushort> _removals = new List<ushort>();

            public ProbeChannelClient() {
                _client = new NetClient(_transport, new NetConfig());
                Channel = new ClientStateChannel<StateChannelTestState>(_client, ChannelMessageId);
                Channel.Removed += RecordRemoval;
            }

            /// <summary>The channel under probe.</summary>
            public ClientStateChannel<StateChannelTestState> Channel { get; }

            /// <summary>Every subject the channel reported retired, in order.</summary>
            public IReadOnlyList<ushort> Removals => _removals;

            /// <summary>Folds one captured datagram in, as if it had just come off the wire.</summary>
            public void Deliver(byte[] payload) {
                var reader = new NetReader(payload, NetEnvelope.HeaderSize, payload.Length - NetEnvelope.HeaderSize);
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

        /// <summary>A module whose every departure callback throws, so the fault guard is re-entered.</summary>
        private sealed class AlwaysThrowingDepartureModule : ISessionModule {
            /// <summary>How many departures reached this module, throwing ones included.</summary>
            public int PeerLeftCount { get; private set; }

            /// <inheritdoc />
            public void Tick(uint serverTick, float deltaSeconds) {
            }

            /// <inheritdoc />
            public void OnPeerJoined(PeerHandle peer) {
            }

            /// <inheritdoc />
            public void OnPeerLeft(PeerHandle peer) {
                PeerLeftCount++;
                throw new InvalidOperationException("The game cannot retire this departure.");
            }

            /// <inheritdoc />
            public void Dispose() {
            }
        }

        /// <summary>Builds <see cref="AlwaysThrowingDepartureModule"/>s and keeps them for the probe.</summary>
        private sealed class AlwaysThrowingDepartureFactory : ISessionModuleFactory {
            /// <summary>Every module built, in creation order.</summary>
            public List<AlwaysThrowingDepartureModule> Modules { get; } =
                new List<AlwaysThrowingDepartureModule>();

            /// <inheritdoc />
            public ISessionModule Create(SessionEntry entry) {
                var module = new AlwaysThrowingDepartureModule();
                Modules.Add(module);
                return module;
            }

            /// <inheritdoc />
            public void RegisterHandlers(MessageRouter router, Func<PeerHandle, SessionEntry> resolveEntry) {
            }
        }
    }
}
