using System;
using System.Collections.Generic;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication.StateChannel;
using AlpineLib.Netcode.Transport;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Round-two review probes: the chunk packer at and past its byte boundary, retirement crossing the
    /// stale-drop and the resurrection paths, and what a partially-sent publish leaves behind.
    /// </summary>
    /// <remarks>
    /// Written to break the round-two fixes rather than to demonstrate them. Where a probe pins
    /// behaviour the reviewer considers wrong, the comment on it says so.
    /// </remarks>
    public sealed class StateChannelReview2Tests {
        private const ushort ChannelMessageId = MessageIdBudget.GameBandStart;

        [Fact]
        public void ARecordExactlyAtTheByteBudgetFillsOneDatagramToTheLastByte() {
            using var world = new BulkyWorld();

            // 2 id + 4 tick + 1 flags + 4 length + payload == the whole budget, to the byte.
            int payloadBytes = ServerStateChannel<BulkyState>.MaxRecordBytesPerEnvelope - 11;
            world.Channel.Set(1, new BulkyState(payloadBytes), 5u);

            world.Channel.BroadcastKeyframe(5u);

            Assert.Single(world.Transport.Sent);
            Assert.Equal(NetBufferPool.DefaultBufferSize, world.Transport.Sent[0].Length);
            Assert.Equal(1016, ServerStateChannel<BulkyState>.MaxRecordBytesPerEnvelope);
        }

        [Fact]
        public void ARecordOneByteOverTheBudgetIsRefusedWithTheAnswerableError() {
            using var world = new BulkyWorld();

            int payloadBytes = ServerStateChannel<BulkyState>.MaxRecordBytesPerEnvelope - 10;
            world.Channel.Set(1, new BulkyState(payloadBytes), 5u);

            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() => world.Channel.BroadcastKeyframe(5u));

            Assert.Contains("Splitting cannot help", error.Message);
            Assert.Empty(world.Transport.Sent);
        }

        /// <summary>
        /// The shape an oversized record actually takes — a state carrying a string or a list, so past
        /// the datagram by a lot rather than by a byte — is named rather than coming out as a raw
        /// writer overflow. The scratch buffer the measurement uses is several datagrams wide for this.
        /// </summary>
        [Fact]
        public void ARecordBiggerThanADatagramIsStillRefusedWithTheAnswerableError() {
            using var world = new BulkyWorld();

            world.Channel.Set(1, new BulkyState(NetBufferPool.DefaultBufferSize), 5u);

            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() => world.Channel.BroadcastKeyframe(5u));

            Assert.Contains("Subject 1", error.Message);
            Assert.Contains("Splitting cannot help", error.Message);
        }

        /// <summary>
        /// Past even the scratch buffer the exact size is unknowable, so the error says "more than" —
        /// but it still names the subject, which is the half a caller can act on.
        /// </summary>
        [Fact]
        public void ARecordPastTheWholeScratchBufferIsNamedWithALowerBoundOnItsSize() {
            using var world = new BulkyWorld();

            world.Channel.Set(1, new BulkyState(NetBufferPool.DefaultBufferSize * 32), 5u);

            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() => world.Channel.BroadcastKeyframe(5u));

            Assert.Contains("Subject 1", error.Message);
            Assert.Contains("more than", error.Message);
            Assert.Contains("Splitting cannot help", error.Message);
        }

        [Fact]
        public void ASubjectPastTheBudgetLeavesTheEarlierChunksAlreadyOnTheWire() {
            using var world = new BulkyWorld();

            // Nine 111-byte records fill a datagram, so thirty subjects take four chunks; the
            // oversized one sits last and is only reached while the final chunk is being packed.
            for (ushort id = 1; id <= 30; id++) {
                world.Channel.Set(id, new BulkyState(100), 5u);
            }

            world.Channel.Set(99, new BulkyState(ServerStateChannel<BulkyState>.MaxRecordBytesPerEnvelope), 5u);

            Assert.Throws<InvalidOperationException>(() => world.Channel.BroadcastDirty(5u));

            int sentBeforeTheThrow = world.Transport.Sent.Count;
            Assert.True(sentBeforeTheThrow > 0, "The publish threw before any chunk reached the transport.");

            // The dirty set survived the partial publish, so every subject is republished — including
            // the ones the peer already had, which it drops on the tick.
            world.Channel.Remove(99);
            world.Channel.BroadcastDirty(6u);
            Assert.True(world.Transport.Sent.Count >= sentBeforeTheThrow + 4);
        }

        [Fact]
        public void ARetirementWhoseTickEqualsTheHeldTickStillRemovesTheSubject() {
            using var world = new LoopbackWorld();
            ChannelClient client = world.ConnectClient();

            world.Channel.Set(7, Moving(10f), 100u);
            world.Pump(3);
            Assert.True(client.Channel.TryGet(7, out _, out uint held));
            Assert.Equal(100u, held);

            // The retire record is stamped with the broadcast tick; force it to the tick the client
            // already holds, which is the one value the stale-drop would refuse for a state record.
            world.Channel.Remove(7);
            world.Channel.BroadcastDirty(100u);
            world.Pump(1);

            Assert.False(client.Channel.TryGet(7, out _, out _));
            Assert.Equal(new ushort[] { 7 }, client.Removals);
            Assert.Empty(client.Channel.Ids);
        }

        [Fact]
        public void ARetirementForASubjectNeverHeldIsSilentAndTheRepeatIsSilentToo() {
            using var world = new LoopbackWorld();
            ChannelClient client = world.ConnectClient();

            world.Channel.Set(7, Moving(10f), 100u);
            world.Pump(3);

            world.Channel.Remove(7);
            world.Pump(3);
            Assert.Single(client.Removals);

            // The repeat rides every dirty broadcast until a keyframe carries it; the client has
            // already dropped the subject, so nothing more is raised. Only the 1 Hz floor retires the
            // repeat, so it takes a whole second of pumping to see the list drain.
            Assert.Equal(new ushort[] { 7 }, world.Channel.RetiringIds);
            world.Pump(35);
            Assert.Single(client.Removals);
            Assert.Empty(world.Channel.RetiringIds);
        }

        [Fact]
        public void ASubjectRemovedAndSetAgainBeforeAnyBroadcastIsNeverRetiredOnTheWire() {
            using var world = new LoopbackWorld();
            WireSpy spy = world.ConnectSpy();
            ChannelClient client = world.ConnectClient();

            world.Channel.Set(7, Moving(10f), 100u);
            world.Pump(3);

            world.Channel.Remove(7);
            world.Channel.Set(7, Moving(20f), 101u);
            Assert.Empty(world.Channel.RetiringIds);

            world.Pump(3);

            Assert.All(spy.AllRecords(), record => Assert.False(record.IsRetired));
            Assert.Empty(client.Removals);
            Assert.True(client.Channel.TryGet(7, out StateChannelTestState state, out uint tick));
            Assert.Equal(20f, state.Distance);
            Assert.Equal(101u, tick);
        }

        [Fact]
        public void ASubjectSetAgainAfterItsRetirementWentOutIsResurrectedInOrder() {
            using var world = new LoopbackWorld();
            ChannelClient client = world.ConnectClient();

            world.Channel.Set(7, Moving(10f), 100u);
            world.Pump(3);

            world.Channel.Remove(7);
            world.Pump(3);
            Assert.Single(client.Removals);

            world.Channel.Set(7, Moving(30f), 101u);
            world.Pump(3);

            Assert.True(client.Channel.TryGet(7, out StateChannelTestState state, out uint tick));
            Assert.Equal(30f, state.Distance);
            Assert.Equal(101u, tick);
            Assert.Single(client.Removals);
        }

        [Fact]
        public void ASubjectResurrectedAtATickAlreadyPublishedIsStillSeenBecauseTheClientDroppedIt() {
            using var world = new LoopbackWorld();
            ChannelClient client = world.ConnectClient();

            world.Channel.Set(7, Moving(10f), 100u);
            world.Pump(3);

            world.Channel.Remove(7);
            world.Pump(3);

            // The equal-tick drop cannot bite here: the client no longer holds the subject, so the
            // record takes the "first time seen" branch rather than the comparison.
            world.Channel.Set(7, Moving(55f), 100u);
            world.Pump(3);

            Assert.True(client.Channel.TryGet(7, out StateChannelTestState state, out uint tick));
            Assert.Equal(55f, state.Distance);
            Assert.Equal(100u, tick);
        }

        [Fact]
        public void ASubjectResurrectedBeforeTheClientHeardTheRetirementKeepsTheStaleDrop() {
            using var world = new LoopbackWorld();
            ChannelClient client = world.ConnectClient();

            world.Channel.Set(7, Moving(10f), 100u);
            world.Pump(3);

            // Remove and Set in the same frame: no retire record is ever built, so the client still
            // holds tick 100 and a resurrection stamped with it publishes nothing at all.
            world.Channel.Remove(7);
            world.Channel.Set(7, Moving(99f), 100u);
            world.Pump(3);

            Assert.True(client.Channel.TryGet(7, out StateChannelTestState state, out _));
            Assert.Equal(10f, state.Distance);
        }

        [Fact]
        public void AKeyframeSplitAcrossEnvelopesLetsAListenerSeeAPartialRosterForOneFrame() {
            using var world = new LoopbackWorld();
            ChannelClient client = world.ConnectClient();
            var rosterSizes = new List<int>();
            client.Channel.Updated += (id, state, tick) => rosterSizes.Add(client.Channel.Ids.Count);

            for (ushort id = 1; id <= 200; id++) {
                world.Channel.Set(id, Moving(id), 100u);
            }

            world.Pump(3);

            // Every subject arrives, but not in one envelope: a listener reading Ids inside Updated
            // sees the roster grow one record at a time and can be handed a partial one.
            Assert.Equal(200, client.Channel.Ids.Count);
            Assert.Equal(200, rosterSizes.Count);
            Assert.Equal(1, rosterSizes[0]);
            Assert.True(world.Channel.Count > 0);
        }

        /// <summary>
        /// A dirty publish that fits one datagram takes the same delivery class a chunked one must, so
        /// two publishes of different sizes are never on two channels that order neither against the
        /// other.
        /// </summary>
        [Fact]
        public void ASingleChunkDirtyPublishRidesTheSameUnsequencedClassAChunkedOneDoes() {
            using var world = new BulkyWorld();

            world.Channel.Set(1, new BulkyState(10), 5u);
            world.Channel.BroadcastDirty(5u);

            Assert.Equal(new[] { DeliveryClass.Unreliable }, world.Transport.Deliveries);
        }

        /// <summary>
        /// A dirty publish that chunks stays off the sequencer for every one of its datagrams: they
        /// share a publish tick, so a reorder inside the burst would have the sequencer discard the
        /// earlier chunks outright rather than merely deliver them late.
        /// </summary>
        [Fact]
        public void AChunkedDirtyPublishDropsTheSequencerForTheWholeBurst() {
            using var world = new BulkyWorld();

            for (ushort id = 1; id <= 30; id++) {
                world.Channel.Set(id, new BulkyState(100), 5u);
            }

            world.Channel.BroadcastDirty(5u);

            Assert.True(world.Transport.Deliveries.Count > 1, "The publish fitted one datagram after all.");
            Assert.All(world.Transport.Deliveries, (delivery) => Assert.Equal(DeliveryClass.Unreliable, delivery));
        }

        [Fact]
        public void EveryChunkOfOnePublishCarriesTheSamePublishTick() {
            using var world = new LoopbackWorld();
            WireSpy spy = world.ConnectSpy();

            for (ushort id = 1; id <= 200; id++) {
                world.Channel.Set(id, Moving(id), 100u);
            }

            world.Pump(3);

            Assert.True(spy.Envelopes.Count > 1);
            uint firstTick = spy.Envelopes[0].ServerTick;
            Assert.All(spy.Envelopes, envelope => Assert.Equal(firstTick, envelope.ServerTick));
        }

        [Fact]
        public void AParkedSubjectRestatedByEveryKeyframeNeverRaisesUpdatedTwice() {
            using var world = new LoopbackWorld();
            ChannelClient client = world.ConnectClient();

            world.Channel.Set(7, Moving(10f), 100u);
            world.Pump(150);

            Assert.Single(client.Updates);
            Assert.True(client.Channel.LastServerTick > 100u);
        }

        [Fact]
        public void ASubjectSetAgainAtTheSameTickWithANewPayloadIsPublishedAndThenDropped() {
            using var world = new LoopbackWorld();
            WireSpy spy = world.ConnectSpy();
            ChannelClient client = world.ConnectClient();

            world.Channel.Set(7, Moving(10f), 100u);
            world.Pump(3);

            world.Channel.Set(7, Moving(999f), 100u);
            world.Pump(3);

            // FINDING (documented, not a defect): the bandwidth is spent — the record goes out — and
            // the client throws it away. A publisher stamping a stale tick pays for silence.
            Assert.Equal(2, spy.Envelopes.Count);
            Assert.Equal(999f, spy.Envelopes[1].Records[0].State.Distance);
            Assert.True(client.Channel.TryGet(7, out StateChannelTestState state, out _));
            Assert.Equal(10f, state.Distance);
        }

        [Fact]
        public void APartialChunkFailureLeavesEveryDirtySubjectPendingAndTheRetirementInFlight() {
            using var world = new OfflineWorld();

            for (ushort id = 1; id <= 200; id++) {
                world.Channel.Set(id, Moving(id), 100u);
            }

            world.Channel.Set(500, Moving(1f), 100u);
            world.Channel.Remove(500);

            world.Transport.FailAfterSends = 1;
            Assert.Throws<InvalidOperationException>(() => world.Channel.BroadcastDirty(100u));
            Assert.Single(world.Transport.Sent);

            world.Transport.FailAfterSends = int.MaxValue;
            world.Channel.BroadcastDirty(101u);

            // Everything is republished, the already-sent chunk included, and the retirement is still
            // in flight rather than lost with the failed send.
            Assert.Equal(200, world.Transport.TotalRecordsSince(1) - 1);
            Assert.Equal(new ushort[] { 500 }, world.Channel.RetiringIds);
        }

        [Fact]
        public void AKeyframeThatFailsPartwayKeepsTheRetirementsForTheNextKeyframe() {
            using var world = new OfflineWorld();

            for (ushort id = 1; id <= 200; id++) {
                world.Channel.Set(id, Moving(id), 100u);
            }

            world.Channel.Set(500, Moving(1f), 100u);
            world.Channel.Remove(500);

            world.Transport.FailAfterSends = 1;
            Assert.Throws<InvalidOperationException>(() => world.Channel.BroadcastKeyframe(100u));

            Assert.Equal(new ushort[] { 500 }, world.Channel.RetiringIds);
        }

        [Fact]
        public void AHitchLongerThanTwoIntervalsIsDroppedRatherThanBanked() {
            using var world = new OfflineWorld();

            world.Channel.Set(1, Moving(1f), 1u);

            // The hitch crosses both cadences at once, so it costs one dirty broadcast and one
            // keyframe — a late send of each, not a queue of them.
            world.Channel.Tick(1u, 2f);
            int afterTheHitch = world.Transport.Sent.Count;
            Assert.Equal(2, afterTheHitch);

            for (int tick = 0; tick < 30; tick++) {
                world.Channel.Set(1, Moving(tick), (uint)(tick + 2));
                world.Channel.Tick((uint)(tick + 2), 1f / 30f);
            }

            // 30 ticks at 30 Hz is one second: 15 snapshots at the configured rate plus at most the
            // one keyframe the second brings round, and no burst of catch-up sends.
            Assert.InRange(world.Transport.Sent.Count - afterTheHitch, 15, 16);
        }

        [Fact]
        public void AHitchOfExactlyTwoIntervalsBanksOneWholeIntervalOfDebt() {
            using var world = new OfflineWorld();
            double interval = world.SnapshotInterval;

            world.Channel.Set(1, Moving(1f), 1u);
            world.Channel.Tick(1u, (float)(interval * 2.0));
            Assert.Single(world.Transport.Sent);

            // The clamp keeps a remainder of exactly one interval, so the very next pump fires again
            // however small its delta. Correct — two intervals of wall time owed two sends — but it is
            // the one input where the clamp does not clamp.
            world.Channel.Set(1, Moving(2f), 2u);
            world.Channel.Tick(2u, 0.0001f);
            Assert.Equal(2, world.Transport.Sent.Count);
        }

        [Fact]
        public void TheKeyframeFloorDriftsSlowBecauseItsAccumulatorIsResetRatherThanDrained() {
            using var world = new OfflineWorld();

            world.Channel.Set(1, Moving(1f), 1u);

            int firstKeyframeTick = -1;
            for (int tick = 0; tick < 40 && firstKeyframeTick < 0; tick++) {
                world.Transport.Sent.Clear();
                world.Channel.Tick((uint)tick, 1f / 30f);

                if (world.Transport.LastWasReliable) {
                    firstKeyframeTick = tick;
                }
            }

            // 30 ticks of 1/30 s is exactly one second, but float delta accumulation lands the floor
            // on tick 30 and then throws the overshoot away. Nitpick, not a defect.
            Assert.True(firstKeyframeTick >= 29, "The keyframe floor fired before a second had passed.");
        }

        [Fact]
        public void TheDecodeCapBranchInThePackerIsUnreachableForAnyRealPayload() {
            // The smallest possible record is 7 bytes (id, tick, flags) with an empty state, so at most
            // 145 fit a datagram — the 512-record branch in FillChunk can never be the binding limit.
            int smallestRecordBytes = 7;
            int maxRecordsPerDatagram = ServerStateChannel<EmptyState>.MaxRecordBytesPerEnvelope / smallestRecordBytes;

            Assert.True(maxRecordsPerDatagram < StateChannelEnvelope<EmptyState>.MaxRecordCount);
        }

        [Fact]
        public void TheIdOwnerScanMissesAGenericOwnerAndAStaticReadonlyId() {
            // Both are shapes nobody writes, but the scan's contract is "found by shape", so name what
            // the shape actually is: a non-generic static class with const ushort fields.
            Assert.EndsWith("MessageIds", typeof(ProbeMessageIds).Name, StringComparison.Ordinal);
            Assert.False(typeof(GenericProbeMessageIds<int>).Name.EndsWith("MessageIds", StringComparison.Ordinal));
        }

        private static StateChannelTestState Moving(float distance) {
            return new StateChannelTestState(distance, 1f, 0);
        }

        private static NetConfig BuildConfig(int port) {
            return new NetConfig {
                GameProtocolName = "statechannel-review2",
                Port = port,
                MaxPeers = 8,
                ServerTickRate = 30,
                SnapshotRate = 15,
                ClientSendRate = 30,
            };
        }

        /// <summary>Stands in for an id owner the scan is meant to find by shape.</summary>
        private static class ProbeMessageIds {
            public const ushort Probe = MessageIdBudget.GameBandStart;
        }

        /// <summary>The shape the scan silently misses: a generic type's runtime name is mangled.</summary>
        private static class GenericProbeMessageIds<T> {
            public const ushort Probe = MessageIdBudget.GameBandStart;
        }

        /// <summary>A payload the caller sizes, for probing the per-record ceiling exactly.</summary>
        private struct BulkyState : INetMessage {
            private int payloadBytes;

            public BulkyState(int payloadBytes) {
                this.payloadBytes = payloadBytes;
            }

            /// <inheritdoc />
            public void Serialize(ref NetWriter writer) {
                writer.WriteInt(payloadBytes);

                for (int byteIndex = 0; byteIndex < payloadBytes; byteIndex++) {
                    writer.WriteByte(0);
                }
            }

            /// <inheritdoc />
            public void Deserialize(ref NetReader reader) {
                payloadBytes = reader.ReadInt();

                for (int byteIndex = 0; byteIndex < payloadBytes; byteIndex++) {
                    reader.ReadByte();
                }
            }
        }

        /// <summary>A payload of no bytes at all, for the smallest record a channel can build.</summary>
        private struct EmptyState : INetMessage {
            /// <inheritdoc />
            public void Serialize(ref NetWriter writer) { }

            /// <inheritdoc />
            public void Deserialize(ref NetReader reader) { }
        }

        /// <summary>A server and a channel with no peer ever connecting, so a send can be inspected raw.</summary>
        private sealed class OfflineWorld : IDisposable {
            private readonly NetServer server;
            private readonly NetConfig config;

            public OfflineWorld() {
                config = BuildConfig(0);
                Transport = new RecordingTransport();
                server = new NetServer(Transport, config);
                Channel = new ServerStateChannel<StateChannelTestState>(
                    server, () => new[] { new PeerHandle(1) }, ChannelMessageId, config);
            }

            /// <summary>The channel under probe.</summary>
            public ServerStateChannel<StateChannelTestState> Channel { get; }

            /// <summary>Everything the channel handed the transport.</summary>
            public RecordingTransport Transport { get; }

            /// <summary>The cadence the channel was configured with.</summary>
            public double SnapshotInterval => config.SnapshotInterval;

            public void Dispose() {
                server.Dispose();
            }
        }

        /// <summary>The same rig over a payload the test sizes to the byte.</summary>
        private sealed class BulkyWorld : IDisposable {
            private readonly NetServer server;

            public BulkyWorld() {
                NetConfig config = BuildConfig(0);
                Transport = new RecordingTransport();
                server = new NetServer(Transport, config);
                Channel = new ServerStateChannel<BulkyState>(
                    server, () => new[] { new PeerHandle(1) }, ChannelMessageId, config);
            }

            /// <summary>The channel under probe.</summary>
            public ServerStateChannel<BulkyState> Channel { get; }

            /// <summary>Everything the channel handed the transport.</summary>
            public RecordingTransport Transport { get; }

            public void Dispose() {
                server.Dispose();
            }
        }

        /// <summary>Keeps every payload handed to it and can be told to fail partway through a publish.</summary>
        private sealed class RecordingTransport : INetTransport {
            /// <summary>Payload bytes, one entry per send, framing included.</summary>
            public List<byte[]> Sent { get; } = new List<byte[]>();

            /// <summary>Sends allowed before every later one throws.</summary>
            public int FailAfterSends { get; set; } = int.MaxValue;

            /// <summary>The delivery class of the most recent send.</summary>
            public bool LastWasReliable { get; private set; }

            /// <summary>The delivery class of every send, in order.</summary>
            public List<DeliveryClass> Deliveries { get; } = new List<DeliveryClass>();

            /// <summary>Never raised: nothing connects to a transport that only records.</summary>
            public event Action<PeerHandle> OnPeerConnected { add { } remove { } }

            /// <summary>Never raised.</summary>
            public event Action<PeerHandle, DisconnectReason> OnPeerDisconnected { add { } remove { } }

            /// <summary>Never raised.</summary>
            public event Action<PeerHandle, ArraySegment<byte>, DeliveryClass> OnData { add { } remove { } }

            /// <summary>Records across every payload sent from an index onwards.</summary>
            public int TotalRecordsSince(int firstIndex) {
                int total = 0;

                for (int sentIndex = firstIndex; sentIndex < Sent.Count; sentIndex++) {
                    total += Decode(Sent[sentIndex]).Records.Count;
                }

                return total;
            }

            public void StartServer(int port, int maxPeers, string protocolKey) { }

            public void StartClient(string protocolKey) { }

            public void Connect(NetEndpoint endpoint) { }

            public void Disconnect(PeerHandle peer) { }

            public void Send(PeerHandle peer, ReadOnlySpan<byte> payload, DeliveryClass delivery) {
                if (Sent.Count >= FailAfterSends) {
                    throw new InvalidOperationException("The transport refused the datagram.");
                }

                LastWasReliable = delivery == DeliveryClass.ReliableOrdered;
                Deliveries.Add(delivery);
                Sent.Add(payload.ToArray());
            }

            public void Stop() { }

            public void Poll() { }

            public int GetPingMs(PeerHandle peer) {
                return 0;
            }

            public void Dispose() { }

            private static StateChannelEnvelope<StateChannelTestState> Decode(byte[] payload) {
                var reader = new NetReader(payload, NetEnvelope.HeaderSize, payload.Length - NetEnvelope.HeaderSize);
                StateChannelEnvelope<StateChannelTestState> envelope = default;
                envelope.Deserialize(ref reader);
                return envelope;
            }
        }

        /// <summary>A server with one state channel on it, plus every peer watching that channel.</summary>
        private sealed class LoopbackWorld : IDisposable {
            private static int nextPort = 46500;

            private readonly int port;
            private readonly FakeNetTransport serverTransport = new FakeNetTransport();
            private readonly NetServer server;
            private readonly List<ChannelClient> clients = new List<ChannelClient>();
            private readonly List<WireSpy> spies = new List<WireSpy>();

            public LoopbackWorld() {
                port = System.Threading.Interlocked.Increment(ref nextPort);
                NetConfig config = BuildConfig(port);

                server = new NetServer(serverTransport, config);
                Channel = new ServerStateChannel<StateChannelTestState>(
                    server, () => server.Peers, ChannelMessageId, config);
                server.Start();
            }

            /// <summary>The channel under test.</summary>
            public ServerStateChannel<StateChannelTestState> Channel { get; }

            /// <summary>Dials a client that builds a channel from the wire.</summary>
            public ChannelClient ConnectClient() {
                var client = new ChannelClient(BuildConfig(port));
                clients.Add(client);
                Attach(client);
                return client;
            }

            /// <summary>Dials a peer that keeps the raw envelopes instead of folding them.</summary>
            public WireSpy ConnectSpy() {
                var spy = new WireSpy(BuildConfig(port));
                spies.Add(spy);
                Attach(spy);
                return spy;
            }

            /// <summary>Advances the server, the channel and every attached peer by whole ticks.</summary>
            public void Pump(int ticks) {
                for (int tick = 0; tick < ticks; tick++) {
                    server.Update(1f / 30f);
                    Channel.Tick(server.Tick, 1f / 30f);

                    for (int clientIndex = 0; clientIndex < clients.Count; clientIndex++) {
                        clients[clientIndex].Update();
                    }

                    for (int spyIndex = 0; spyIndex < spies.Count; spyIndex++) {
                        spies[spyIndex].Update();
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

            private void Attach(ILoopbackPeer peer) {
                peer.Connect(port);

                for (int attempt = 0; attempt < 64 && !peer.IsReady; attempt++) {
                    Pump(1);
                }

                Assert.True(peer.IsReady, "The review loopback peer never finished connecting.");
            }
        }

        /// <summary>What the world needs of a peer to dial it and pump it.</summary>
        private interface ILoopbackPeer : IDisposable {
            /// <summary>True once the link is up.</summary>
            bool IsReady { get; }

            void Connect(int port);

            void Update();
        }

        /// <summary>A client that builds a channel from the wire and records what it raised.</summary>
        private sealed class ChannelClient : ILoopbackPeer {
            private readonly FakeNetTransport transport = new FakeNetTransport();
            private readonly NetClient client;
            private readonly List<uint> updates = new List<uint>();
            private readonly List<ushort> removals = new List<ushort>();

            public ChannelClient(NetConfig config) {
                client = new NetClient(transport, config);
                Channel = new ClientStateChannel<StateChannelTestState>(client, ChannelMessageId);
                Channel.Updated += RecordUpdate;
                Channel.Removed += RecordRemoval;
            }

            /// <summary>The channel built from this client's half of the wire.</summary>
            public ClientStateChannel<StateChannelTestState> Channel { get; }

            /// <summary>The tick of every accepted record, in order.</summary>
            public IReadOnlyList<uint> Updates => updates;

            /// <summary>Every subject the channel reported retired, in order.</summary>
            public IReadOnlyList<ushort> Removals => removals;

            /// <inheritdoc />
            public bool IsReady => client.IsConnected;

            /// <inheritdoc />
            public void Connect(int port) {
                client.Connect(NetEndpoint.Direct("127.0.0.1", port));
            }

            /// <inheritdoc />
            public void Update() {
                client.Update(1f / 30f);
            }

            public void Dispose() {
                Channel.Dispose();
                client.Dispose();
            }

            private void RecordUpdate(ushort id, StateChannelTestState state, uint tick) {
                updates.Add(tick);
            }

            private void RecordRemoval(ushort id) {
                removals.Add(id);
            }
        }

        /// <summary>A peer that keeps the raw envelopes rather than folding them into a channel.</summary>
        private sealed class WireSpy : ILoopbackPeer {
            private readonly FakeNetTransport transport = new FakeNetTransport();
            private readonly NetClient client;
            private readonly List<StateChannelEnvelope<StateChannelTestState>> envelopes =
                new List<StateChannelEnvelope<StateChannelTestState>>();

            public WireSpy(NetConfig config) {
                client = new NetClient(transport, config);
                client.Router.Register<StateChannelEnvelope<StateChannelTestState>>(ChannelMessageId, RecordEnvelope);
            }

            /// <summary>Every envelope this peer received, in arrival order.</summary>
            public IReadOnlyList<StateChannelEnvelope<StateChannelTestState>> Envelopes => envelopes;

            /// <inheritdoc />
            public bool IsReady => client.IsConnected;

            /// <summary>Every record across every envelope, flattened.</summary>
            public IReadOnlyList<StateChannelRecord<StateChannelTestState>> AllRecords() {
                var all = new List<StateChannelRecord<StateChannelTestState>>();

                for (int envelopeIndex = 0; envelopeIndex < envelopes.Count; envelopeIndex++) {
                    all.AddRange(envelopes[envelopeIndex].Records);
                }

                return all;
            }

            /// <inheritdoc />
            public void Connect(int port) {
                client.Connect(NetEndpoint.Direct("127.0.0.1", port));
            }

            /// <inheritdoc />
            public void Update() {
                client.Update(1f / 30f);
            }

            public void Dispose() {
                client.Dispose();
            }

            private void RecordEnvelope(in StateChannelEnvelope<StateChannelTestState> message, PeerHandle sender) {
                envelopes.Add(message);
            }
        }
    }
}
