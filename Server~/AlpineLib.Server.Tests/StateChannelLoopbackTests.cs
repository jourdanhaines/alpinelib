using System;
using System.Collections.Generic;
using System.Threading;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication.StateChannel;
using AlpineLib.Netcode.Transport;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// A server state channel and its clients driven against each other over an in-memory transport, one
    /// fixed tick at a time. These are the tests the codec cannot stand in for: a cadence that never
    /// fires, a keyframe that arrives out of order and undoes a newer state, a handler left bound after
    /// teardown.
    /// </summary>
    public sealed class StateChannelLoopbackTests {
        private const ushort ChannelMessageId = MessageIdBudget.GameBandStart;
        private const float TickInterval = 1f / 30f;

        private static int nextPort = 44000;

        [Fact]
        public void AChannelRefusesAMessageIdTheLibraryAlreadySpeaks() {
            using var world = new LoopbackWorld(AllocatePort());

            Assert.Throws<ArgumentException>(() => world.BuildChannel(MessageIdBudget.ReplicationBandStart));
        }

        [Fact]
        public void OnlyTheSubjectsThatChangedRideTheSnapshotCadence() {
            using var world = new LoopbackWorld(AllocatePort());
            WireSpyClient spy = world.ConnectSpy();

            world.Channel.Set(1, Moving(10f), 100u);
            world.Channel.Set(2, Moving(20f), 100u);
            world.Pump(3);

            Assert.Single(spy.Envelopes);
            Assert.Equal(2, spy.Envelopes[0].Records.Count);

            world.Channel.Set(2, Moving(25f), 101u);
            world.Pump(3);

            Assert.Equal(2, spy.Envelopes.Count);
            Assert.Single(spy.Envelopes[1].Records);
            Assert.Equal(2, spy.Envelopes[1].Records[0].Id);
            Assert.Equal(25f, spy.Envelopes[1].Records[0].State.Distance);
        }

        [Fact]
        public void ASilentChannelSendsNothingUntilTheKeyframeFloorComesRound() {
            using var world = new LoopbackWorld(AllocatePort());
            WireSpyClient spy = world.ConnectSpy();

            world.Channel.Set(1, Moving(10f), 100u);
            world.Channel.Set(2, Moving(20f), 100u);
            world.Pump(3);

            Assert.Single(spy.Envelopes);

            world.Pump(40);

            Assert.True(spy.Envelopes.Count >= 2, "The 1 Hz keyframe floor never went out.");

            for (int envelopeIndex = 1; envelopeIndex < spy.Envelopes.Count; envelopeIndex++) {
                Assert.Equal(2, spy.Envelopes[envelopeIndex].Records.Count);
            }
        }

        [Fact]
        public void ALateJoinerIsCaughtUpByAKeyframeAddressedToItAlone() {
            using var world = new LoopbackWorld(AllocatePort());
            ChannelClient early = world.ConnectClient();

            world.Channel.Set(1, Moving(10f), 100u);
            world.Channel.Set(2, Moving(20f), 100u);
            world.Pump(3);

            ChannelClient late = world.ConnectClient();
            int earlyUpdatesBefore = early.Updates.Count;

            Assert.Empty(late.Updates);

            world.Channel.SendKeyframeTo(late.ServerSidePeer);
            world.Pump(1);

            Assert.Equal(2, late.Updates.Count);
            Assert.Equal(earlyUpdatesBefore, early.Updates.Count);

            Assert.True(late.Channel.TryGet(1, out StateChannelTestState state, out uint tick));
            Assert.Equal(10f, state.Distance);
            Assert.Equal(100u, tick);
            Assert.Equal(new ushort[] { 1, 2 }, late.Channel.Ids);
            Assert.True(late.Channel.LastServerTick > 0u);
        }

        [Fact]
        public void ARecordOlderThanTheOneHeldIsDropped() {
            using var world = new LoopbackWorld(AllocatePort());
            ChannelClient client = world.ConnectClient();

            world.Channel.Set(1, Moving(50f), 200u);
            world.Pump(3);

            Assert.True(client.Channel.TryGet(1, out StateChannelTestState current, out uint currentTick));
            Assert.Equal(50f, current.Distance);
            Assert.Equal(200u, currentTick);

            world.Channel.Set(1, Moving(5f), 100u);
            world.Pump(3);

            Assert.True(client.Channel.TryGet(1, out StateChannelTestState held, out uint heldTick));
            Assert.Equal(50f, held.Distance);
            Assert.Equal(200u, heldTick);
            Assert.Single(client.Updates);
        }

        [Fact]
        public void AnUpdateCarriesTheSubjectTheStateAndTheTickItWasTrueAt() {
            using var world = new LoopbackWorld(AllocatePort());
            ChannelClient client = world.ConnectClient();

            world.Channel.Set(9, new StateChannelTestState(123.5f, -4.25f, -2), 777u);
            world.Pump(3);

            Assert.Single(client.Updates);
            Assert.Equal(9, client.Updates[0].Id);
            Assert.Equal(777u, client.Updates[0].Tick);
            Assert.Equal(123.5f, client.Updates[0].State.Distance);
            Assert.Equal(-4.25f, client.Updates[0].State.Velocity);
            Assert.Equal(-2, client.Updates[0].State.Notch);
        }

        [Fact]
        public void DisposingAClientChannelGivesTheRouterIdBack() {
            using var world = new LoopbackWorld(AllocatePort());
            ChannelClient client = world.ConnectClient();

            Assert.True(client.IsChannelRegistered);

            client.Channel.Dispose();

            Assert.False(client.IsChannelRegistered);

            // A teardown followed by a new session must be able to claim the id again; the router throws
            // on a double registration, so this is what proves Dispose really released it.
            client.RebindChannel();
            Assert.True(client.IsChannelRegistered);
        }

        [Fact]
        public void RemovingASubjectDropsItFromTheServerSideSet() {
            using var world = new LoopbackWorld(AllocatePort());

            world.Channel.Set(1, Moving(10f), 100u);
            world.Channel.Set(2, Moving(20f), 100u);

            Assert.Equal(2, world.Channel.Count);
            Assert.True(world.Channel.Remove(1));
            Assert.False(world.Channel.Remove(1));
            Assert.Equal(1, world.Channel.Count);
            Assert.False(world.Channel.TryGet(1, out StateChannelTestState _));
            Assert.True(world.Channel.TryGet(2, out StateChannelTestState survivor));
            Assert.Equal(20f, survivor.Distance);
            Assert.Equal(new ushort[] { 2 }, world.Channel.Ids);
        }

        private static StateChannelTestState Moving(float distance) {
            return new StateChannelTestState(distance, 5f, 1);
        }

        private static int AllocatePort() {
            return Interlocked.Increment(ref nextPort);
        }

        private static NetConfig BuildConfig(int port) {
            return new NetConfig {
                GameProtocolName = "statechannel-test",
                Port = port,
                MaxPeers = 8,
                ServerTickRate = 30,
                SnapshotRate = 15,
                ClientSendRate = 30,
            };
        }

        /// <summary>A server with one state channel on it, plus every peer watching that channel.</summary>
        private sealed class LoopbackWorld : IDisposable {
            private readonly int port;
            private readonly FakeNetTransport serverTransport = new FakeNetTransport();
            private readonly NetServer server;
            private readonly ServerStateChannel<StateChannelTestState> channel;
            private readonly List<ChannelClient> clients = new List<ChannelClient>();
            private readonly List<WireSpyClient> spies = new List<WireSpyClient>();

            public LoopbackWorld(int port) {
                this.port = port;
                NetConfig config = BuildConfig(port);

                server = new NetServer(serverTransport, config);
                channel = new ServerStateChannel<StateChannelTestState>(server, () => server.Peers, ChannelMessageId, config);
                server.Start();
            }

            /// <summary>The channel under test.</summary>
            public ServerStateChannel<StateChannelTestState> Channel => channel;

            /// <summary>Builds another channel on this server, for the constructor's own guards.</summary>
            public ServerStateChannel<StateChannelTestState> BuildChannel(ushort messageId) {
                return new ServerStateChannel<StateChannelTestState>(
                    server,
                    () => server.Peers,
                    messageId,
                    BuildConfig(port));
            }

            /// <summary>Dials a client that builds a channel from the wire.</summary>
            public ChannelClient ConnectClient() {
                var client = new ChannelClient(BuildConfig(port));
                clients.Add(client);
                Attach(client);
                return client;
            }

            /// <summary>
            /// Dials a peer that reads the envelopes straight off the wire instead of building a channel
            /// from them, so a test can assert on what the server actually sent.
            /// </summary>
            public WireSpyClient ConnectSpy() {
                var spy = new WireSpyClient(BuildConfig(port));
                spies.Add(spy);
                Attach(spy);
                return spy;
            }

            /// <summary>Advances the server, the channel and every attached peer by whole ticks.</summary>
            public void Pump(int ticks) {
                for (int tick = 0; tick < ticks; tick++) {
                    server.Update(TickInterval);
                    channel.Tick(server.Tick, TickInterval);

                    for (int clientIndex = 0; clientIndex < clients.Count; clientIndex++) {
                        clients[clientIndex].Update(TickInterval);
                    }

                    for (int spyIndex = 0; spyIndex < spies.Count; spyIndex++) {
                        spies[spyIndex].Update(TickInterval);
                    }
                }
            }

            public void Dispose() {
                for (int clientIndex = 0; clientIndex < clients.Count; clientIndex++) {
                    clients[clientIndex].Dispose();
                }

                for (int spyIndex = 0; spyIndex < spies.Count; spyIndex++) {
                    spies[spyIndex].Dispose();
                }

                server.Dispose();
            }

            /// <summary>Dials a peer and pumps until the session sees it, then hands it its own handle.</summary>
            private void Attach(ILoopbackPeer peer) {
                peer.Connect(port);

                for (int attempt = 0; attempt < 32 && !peer.IsReady; attempt++) {
                    Pump(1);
                }

                Assert.True(peer.IsReady, "The loopback peer never finished connecting.");
                peer.BindPeerId(server.Peers[server.Peers.Count - 1]);
            }
        }

        /// <summary>What <see cref="LoopbackWorld"/> needs of a peer to dial it and pump it.</summary>
        private interface ILoopbackPeer : IDisposable {
            /// <summary>True once the link is up.</summary>
            bool IsReady { get; }

            void Connect(int port);

            /// <summary>Remembers which peer the server thinks this is, so a test can address it by hand.</summary>
            void BindPeerId(PeerHandle peer);

            void Update(float deltaSeconds);
        }

        /// <summary>A client that builds a <see cref="ClientStateChannel{TState}"/> from the wire.</summary>
        private sealed class ChannelClient : ILoopbackPeer {
            private readonly FakeNetTransport transport = new FakeNetTransport();
            private readonly NetClient client;
            private readonly List<ChannelUpdate> updates = new List<ChannelUpdate>();

            private ClientStateChannel<StateChannelTestState> channel;

            public ChannelClient(NetConfig config) {
                client = new NetClient(transport, config);
                BindChannel();
            }

            /// <summary>The channel built from this client's half of the wire.</summary>
            public ClientStateChannel<StateChannelTestState> Channel => channel;

            /// <summary>Every <c>Updated</c> the channel raised, in order.</summary>
            public IReadOnlyList<ChannelUpdate> Updates => updates;

            /// <summary>The handle the server addresses this peer by.</summary>
            public PeerHandle ServerSidePeer { get; private set; } = PeerHandle.None;

            /// <inheritdoc />
            public bool IsReady => client.IsConnected;

            /// <summary>True while the channel owns its id on this client's router.</summary>
            public bool IsChannelRegistered => client.Router.IsRegistered(ChannelMessageId);

            /// <inheritdoc />
            public void Connect(int port) {
                client.Connect(NetEndpoint.Direct("127.0.0.1", port));
            }

            /// <inheritdoc />
            public void BindPeerId(PeerHandle peer) {
                ServerSidePeer = peer;
            }

            /// <summary>Builds a fresh channel on the same id, as a new session on this client would.</summary>
            public void RebindChannel() {
                BindChannel();
            }

            /// <inheritdoc />
            public void Update(float deltaSeconds) {
                client.Update(deltaSeconds);
            }

            public void Dispose() {
                channel.Dispose();
                client.Dispose();
            }

            private void BindChannel() {
                channel = new ClientStateChannel<StateChannelTestState>(client, ChannelMessageId);
                channel.Updated += RecordUpdate;
            }

            private void RecordUpdate(ushort id, StateChannelTestState state, uint tick) {
                updates.Add(new ChannelUpdate(id, state, tick));
            }
        }

        /// <summary>A peer that keeps the raw envelopes rather than folding them into a channel.</summary>
        private sealed class WireSpyClient : ILoopbackPeer {
            private readonly FakeNetTransport transport = new FakeNetTransport();
            private readonly NetClient client;
            private readonly List<StateChannelEnvelope<StateChannelTestState>> envelopes =
                new List<StateChannelEnvelope<StateChannelTestState>>();

            public WireSpyClient(NetConfig config) {
                client = new NetClient(transport, config);
                client.Router.Register<StateChannelEnvelope<StateChannelTestState>>(ChannelMessageId, RecordEnvelope);
            }

            /// <summary>Every envelope this peer received, in arrival order.</summary>
            public IReadOnlyList<StateChannelEnvelope<StateChannelTestState>> Envelopes => envelopes;

            /// <summary>The handle the server addresses this peer by.</summary>
            public PeerHandle ServerSidePeer { get; private set; } = PeerHandle.None;

            /// <inheritdoc />
            public bool IsReady => client.IsConnected;

            /// <inheritdoc />
            public void Connect(int port) {
                client.Connect(NetEndpoint.Direct("127.0.0.1", port));
            }

            /// <inheritdoc />
            public void BindPeerId(PeerHandle peer) {
                ServerSidePeer = peer;
            }

            /// <inheritdoc />
            public void Update(float deltaSeconds) {
                client.Update(deltaSeconds);
            }

            public void Dispose() {
                client.Dispose();
            }

            private void RecordEnvelope(in StateChannelEnvelope<StateChannelTestState> message, PeerHandle sender) {
                envelopes.Add(message);
            }
        }

        /// <summary>One raised <c>Updated</c>, kept so a test can assert on its arguments.</summary>
        private readonly struct ChannelUpdate {
            public ChannelUpdate(ushort id, StateChannelTestState state, uint tick) {
                Id = id;
                State = state;
                Tick = tick;
            }

            /// <summary>The subject that moved.</summary>
            public ushort Id { get; }

            /// <summary>Its new state.</summary>
            public StateChannelTestState State { get; }

            /// <summary>The tick that state was true at.</summary>
            public uint Tick { get; }
        }
    }
}
