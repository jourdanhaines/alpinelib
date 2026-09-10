using System;
using System.Collections.Generic;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication.StateChannel;
using AlpineLib.Netcode.Transport;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// WP16a round-three review probes: the server-tick contract on <c>ServerStateChannel.Set</c>, the
    /// tick a retirement is stamped with, and the resurrection ordering the contract is there to buy.
    /// </summary>
    /// <remarks>
    /// Written to break round three's remedy A. Six of the eight facts pin what the round claims; two
    /// pin what it does not cover — the unchecked first stamp, and a server tick that advances by more
    /// than one between two pumps, which <c>NetServer.AdvanceTick</c> is free to do.
    /// </remarks>
    public sealed class Wp16aReviewRound3ProbeTests {
        private const ushort ChannelMessageId = MessageIdBudget.GameBandStart;

        /// <summary>A state older than the last publish is refused where the game stamps it.</summary>
        [Fact]
        public void AStateStampedBelowTheLastPublishedTickIsRefused() {
            using var server = new ProbeServerChannel();

            server.Channel.Tick(100u, 0f);
            server.Channel.Set(5, Moving(10f), 100u);
            server.Channel.BroadcastDirty(100u);

            ArgumentException refusal =
                Assert.Throws<ArgumentException>(() => server.Channel.Set(5, Moving(20f), 99u));
            Assert.Equal("tick", refusal.ParamName);
        }

        /// <summary>A state more than one tick past the last drive is refused.</summary>
        [Fact]
        public void AStateStampedMoreThanOneTickAheadIsRefused() {
            using var server = new ProbeServerChannel();

            server.Channel.Tick(100u, 0f);

            ArgumentException refusal =
                Assert.Throws<ArgumentException>(() => server.Channel.Set(5, Moving(10f), 102u));
            Assert.Equal("tick", refusal.ParamName);
        }

        /// <summary>
        /// One tick past the last drive is the shipped shape — the game computes the state for tick N
        /// and hands it over before pumping the channel for N — and it publishes.
        /// </summary>
        [Fact]
        public void AStateStampedOneTickAheadOfTheLastDriveIsAccepted() {
            using var server = new ProbeServerChannel();

            server.Channel.Tick(100u, 0f);
            server.Channel.Set(5, Moving(10f), 101u);
            server.Channel.BroadcastDirty(101u);
            int publish = server.TakePublishIndex();

            Assert.Equal(101u, server.RecordTick(publish, 5));

            // The same tick again, and the one after it, stay legal: neither is below the last publish.
            server.Channel.Set(5, Moving(11f), 101u);
            server.Channel.Tick(101u, 0f);
            server.Channel.Set(5, Moving(12f), 102u);
        }

        /// <summary>
        /// A retirement carries the tick of the publish that sends it, and every repeat carries its own
        /// — no floor raised from the subject's last state tick.
        /// </summary>
        [Fact]
        public void ARetirementAndItsRepeatsCarryThePublishTick() {
            using var server = new ProbeServerChannel();

            server.Channel.Tick(200u, 0f);
            server.Channel.Set(5, Moving(10f), 200u);
            server.Channel.BroadcastDirty(200u);
            server.TakePublishIndex();

            server.Channel.Remove(5);
            server.Channel.BroadcastDirty(201u);
            int firstRetirement = server.TakePublishIndex();
            server.Channel.BroadcastDirty(202u);
            int repeat = server.TakePublishIndex();

            Assert.Equal(201u, server.RecordTick(firstRetirement, 5));
            Assert.Equal(202u, server.RecordTick(repeat, 5));
        }

        /// <summary>
        /// The ordering the contract exists for: state at N, the retirement published at N+1 and the
        /// resurrection at N+2 arrive in every one of the six possible orders, and the subject is alive
        /// carrying the resurrected payload at the end of each.
        /// </summary>
        [Fact]
        public void TheResurrectionSurvivesEveryDeliveryOrderOfTheThreePublishes() {
            using var server = new ProbeServerChannel();

            server.Channel.Tick(300u, 0f);
            server.Channel.Set(5, Moving(10f), 300u);
            server.Channel.BroadcastDirty(300u);
            int statePublish = server.TakePublishIndex();

            server.Channel.Remove(5);
            server.Channel.BroadcastDirty(301u);
            int retirementPublish = server.TakePublishIndex();

            server.Channel.Tick(301u, 0f);
            server.Channel.Set(5, Moving(20f), 302u);
            server.Channel.BroadcastDirty(302u);
            int resurrectionPublish = server.TakePublishIndex();

            int[] publishes = { statePublish, retirementPublish, resurrectionPublish };

            foreach (int[] order in Permutations(publishes)) {
                using var client = new ProbeChannelClient();

                for (int index = 0; index < order.Length; index++) {
                    server.Replay(client, order[index]);
                }

                string shape = string.Join(",", order);
                Assert.True(client.Channel.TryGet(5, out StateChannelTestState state, out uint heldTick),
                    "Subject 5 was deleted by order " + shape);
                Assert.Equal(20f, state.Distance);
                Assert.Equal(302u, heldTick);
            }
        }

        /// <summary>
        /// The gap round three documents rather than closes: nothing is checked before the channel has
        /// been driven or published once, so a first stamp from any clock at all reaches the wire — and
        /// the ghost it leaves on a client is permanent, not repaired by the next keyframe.
        /// </summary>
        /// <remarks>
        /// Judged acceptable because both shipped games create the channel and pump it from the same
        /// module tick. Pinned so the exemption is a fact rather than an assumption, and so the cost of
        /// falling into it is recorded accurately: a keyframe is records like any other publish, with no
        /// flag telling the client to reset, so the stale-tick drop applies to it too.
        /// </remarks>
        [Fact]
        public void TheVeryFirstStampIsTakenOnTrustAndTheGhostItLeavesIsPermanent() {
            using var server = new ProbeServerChannel();

            server.Channel.Set(5, Moving(10f), 900_000u);
            server.Channel.BroadcastDirty(1u);
            int aheadOfClockPublish = server.TakePublishIndex();

            Assert.Equal(900_000u, server.RecordTick(aheadOfClockPublish, 5));

            // The publish tick, not the wild stamp, is the floor from here — so the server carries on
            // stamping states the client will never accept.
            server.Channel.Set(5, Moving(20f), 2u);
            server.Channel.BroadcastDirty(2u);
            int restatementPublish = server.TakePublishIndex();

            server.Channel.Remove(5);
            server.Channel.BroadcastKeyframe(3u);
            int keyframePublish = server.TakePublishIndex();

            Assert.Equal(3u, server.RecordTick(keyframePublish, 5));

            using var client = new ProbeChannelClient();
            server.Replay(client, aheadOfClockPublish);
            server.Replay(client, restatementPublish);
            server.Replay(client, keyframePublish);

            Assert.True(client.Channel.TryGet(5, out StateChannelTestState state, out uint heldTick),
                "The keyframe retirement reached the ghost after all.");
            Assert.Equal(10f, state.Distance);
            Assert.Equal(900_000u, heldTick);
            Assert.DoesNotContain<ushort>(5, client.Removals);
        }

        /// <summary>
        /// A subject retired and set again inside one publish never reaches the wire as a retirement, so
        /// the equal-tick edge the client's strict comparison covers is not reachable from a single pump.
        /// </summary>
        [Fact]
        public void ASubjectRemovedAndSetAgainBeforeThePublishIsNeverRetiredOnTheWire() {
            using var server = new ProbeServerChannel();

            server.Channel.Tick(400u, 0f);
            server.Channel.Set(5, Moving(10f), 400u);
            server.Channel.BroadcastDirty(400u);
            server.TakePublishIndex();

            server.Channel.Remove(5);
            server.Channel.Set(5, Moving(20f), 401u);
            server.Channel.BroadcastDirty(401u);
            int publish = server.TakePublishIndex();

            Assert.Empty(server.Channel.RetiringIds);
            Assert.Equal(401u, server.RecordTick(publish, 5));

            using var client = new ProbeChannelClient();
            server.Replay(client, publish);
            Assert.True(client.Channel.TryGet(5, out StateChannelTestState state, out _));
            Assert.Equal(20f, state.Distance);
            Assert.DoesNotContain<ushort>(5, client.Removals);
        }

        /// <summary>
        /// The equal-tick collision the strict comparison was chosen for is still reachable, but only
        /// through two publishes at one tick: a retirement and the resurrection that followed it then
        /// share a tick, and the reordered retirement wins.
        /// </summary>
        /// <remarks>
        /// Only <c>Set</c> is gated, not the publish, and the pump publishes at most once per tick — so
        /// nothing a game drives through <c>Tick</c> produces this. Pinned because the exclusion is a
        /// property of the driver rather than of the channel.
        /// </remarks>
        [Fact]
        public void TwoPublishesAtOneTickPutARetirementAndItsResurrectionOnTheSameTick() {
            using var server = new ProbeServerChannel();

            server.Channel.Tick(500u, 0f);
            server.Channel.Set(5, Moving(10f), 500u);
            server.Channel.BroadcastDirty(500u);
            int statePublish = server.TakePublishIndex();

            server.Channel.Remove(5);
            server.Channel.BroadcastDirty(500u);
            int retirementPublish = server.TakePublishIndex();

            server.Channel.Set(5, Moving(20f), 500u);
            server.Channel.BroadcastDirty(500u);
            int resurrectionPublish = server.TakePublishIndex();

            Assert.Equal(500u, server.RecordTick(retirementPublish, 5));
            Assert.Equal(500u, server.RecordTick(resurrectionPublish, 5));

            using var client = new ProbeChannelClient();
            server.Replay(client, statePublish);
            server.Replay(client, resurrectionPublish);
            server.Replay(client, retirementPublish);

            Assert.False(client.Channel.TryGet(5, out StateChannelTestState _, out uint _));
            Assert.Contains<ushort>(5, client.Removals);
        }

        /// <summary>
        /// <c>NetServer</c> advances its tick by as many intervals as one update's delta covers, so a
        /// hitched frame moves the counter by more than one between two pumps of a channel. The state
        /// the game computes for that tick is accepted, because the guard reads the live counter rather
        /// than only the tick it was last driven with.
        /// </summary>
        /// <remarks>
        /// Unreachable in the dedicated loop, which steps a fixed delta equal to the tick interval, and
        /// no listen host ticks a module today. It is a property of the library's own clock rather than
        /// of any game, which is why it is pinned here.
        /// </remarks>
        [Fact]
        public void AServerTickThatAdvancedByMoreThanOneStillAcceptsTheStateComputedForIt() {
            using var probe = new ProbeServerChannel();
            probe.Server.Start();

            probe.Server.Update(1f / 30f);
            uint firstTick = probe.Server.Tick;
            probe.Channel.Tick(firstTick, 1f / 30f);

            // One update whose delta covers four intervals: the counter jumps, as AdvanceTick's loop
            // is written to do.
            probe.Server.Update(4f / 30f);
            uint jumpedTick = probe.Server.Tick;
            Assert.True(jumpedTick - firstTick > 1u, "The server tick did not jump; AdvanceTick changed.");

            probe.Channel.Set(5, Moving(10f), jumpedTick);
            probe.Channel.BroadcastDirty(jumpedTick);

            Assert.Equal(jumpedTick, probe.RecordTick(probe.TakePublishIndex(), 5));

            // Two past the caught-up counter is still refused: the live read widens the ceiling to the
            // server's tick, not beyond it.
            ArgumentException refusal =
                Assert.Throws<ArgumentException>(() => probe.Channel.Set(5, Moving(20f), jumpedTick + 2u));
            Assert.Equal("tick", refusal.ParamName);
        }

        private static StateChannelTestState Moving(float distance) {
            return new StateChannelTestState(distance, 1f, 0);
        }

        /// <summary>Every ordering of three captured publishes, so a probe can replay all of them.</summary>
        private static IEnumerable<int[]> Permutations(int[] values) {
            for (int first = 0; first < values.Length; first++) {
                for (int second = 0; second < values.Length; second++) {
                    if (second == first) {
                        continue;
                    }

                    for (int third = 0; third < values.Length; third++) {
                        if (third == first || third == second) {
                            continue;
                        }

                        yield return new[] { values[first], values[second], values[third] };
                    }
                }
            }
        }

        /// <summary>A server channel whose datagrams are kept so a probe can replay them out of order.</summary>
        private sealed class ProbeServerChannel : IDisposable {
            private int _taken;

            public ProbeServerChannel() {
                NetConfig config = new NetConfig {
                    GameProtocolName = "statechannel-review-r3",
                    Port = 0,
                    MaxPeers = 8,
                    ServerTickRate = 30,
                    SnapshotRate = 15,
                    ClientSendRate = 30,
                };

                Transport = new CapturingTransport();
                Server = new NetServer(Transport, config);
                Channel = new ServerStateChannel<StateChannelTestState>(
                    Server, () => new[] { new PeerHandle(1) }, ChannelMessageId, config);
            }

            /// <summary>The server the channel publishes through, and the owner of the tick counter.</summary>
            public NetServer Server { get; }

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
                Server.Dispose();
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
    }
}
