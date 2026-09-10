using System;
using System.Collections.Generic;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication.StateChannel;
using AlpineLib.Netcode.Transport;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The state channel's failure modes: a publish too big for one datagram, a send that throws with
    /// state still pending, a client channel reaching for an id the library speaks, a cadence
    /// accumulator asked to swallow a frame hitch, and a channel outliving the connection it was filled
    /// from.
    /// </summary>
    /// <remarks>
    /// These began as review probes pinning the behaviour as it stood; the round-2 fixes landed and each
    /// now asserts the behaviour that was wanted instead. Their value is that they were written by
    /// someone trying to break the channel rather than to demonstrate it.
    /// </remarks>
    public sealed class StateChannelAdversarialTests {
        private const ushort ChannelMessageId = MessageIdBudget.GameBandStart;

        // 2 id + 4 tick + 1 flags + 9 payload; the envelope adds 4 tick + 2 count and the frame a 2-byte id.
        private const int RecordBytes = 16;
        private const int EnvelopeOverheadBytes = 8;

        [Fact]
        public void APublishTooBigForOneDatagramIsSplitAcrossEnvelopesRatherThanThrowing() {
            using var world = new OfflineWorld();

            int subjectsPerEnvelope = (NetBufferPool.DefaultBufferSize - EnvelopeOverheadBytes) / RecordBytes;

            for (ushort id = 0; id <= subjectsPerEnvelope * 2; id++) {
                world.Channel.Set(id, new StateChannelTestState(id, 1f, 0), 10u);
            }

            world.Channel.BroadcastKeyframe(10u);

            Assert.Equal(3, world.Transport.Sent.Count);
            Assert.All(
                world.Transport.Sent,
                payload => Assert.True(payload.Length <= NetBufferPool.DefaultBufferSize));
            Assert.Equal(world.Channel.Count, world.Transport.TotalRecordsSent());
        }

        [Fact]
        public void ASendThatThrowsLeavesTheDirtySetPendingForTheNextBroadcast() {
            using var world = new OfflineWorld();

            world.Channel.Set(1, new StateChannelTestState(1f, 1f, 0), 10u);
            world.Channel.Set(2, new StateChannelTestState(2f, 1f, 0), 10u);

            world.Transport.FailSends = true;
            Assert.Throws<InvalidOperationException>(() => world.Channel.BroadcastDirty(10u));

            // Nothing reached a peer, so the subjects are still pending rather than silently published.
            world.Transport.FailSends = false;
            world.Channel.BroadcastDirty(11u);

            Assert.Single(world.Transport.Sent);
            Assert.Equal(2, world.Transport.DecodeLast().Records.Count);
        }

        [Fact]
        public void AChannelWithMoreSubjectsThanTheDecodeCapStillPublishesEveryOneOfThem() {
            using var world = new OfflineWorld();

            int subjects = StateChannelEnvelope<StateChannelTestState>.MaxRecordCount + 100;

            for (ushort id = 0; id < subjects; id++) {
                world.Channel.Set(id, new StateChannelTestState(id, 1f, 0), 10u);
            }

            world.Channel.BroadcastKeyframe(10u);

            // The 512 cap bounds one envelope's decode, not the channel: chunking is what makes the
            // subject count a bandwidth question rather than a hard ceiling.
            Assert.Equal(subjects, world.Transport.TotalRecordsSent());
            Assert.All(
                world.Transport.Sent,
                payload => Assert.True(payload.Length <= NetBufferPool.DefaultBufferSize));
        }

        [Fact]
        public void ASingleRecordTooBigForAnEmptyDatagramIsRefusedWithAnAnswerableError() {
            using var world = new BulkyWorld();

            world.Channel.Set(1, new BulkyTestState(BulkyWorld.OversizedPayloadBytes), 1u);

            InvalidOperationException error =
                Assert.Throws<InvalidOperationException>(() => world.Channel.BroadcastKeyframe(1u));

            Assert.Contains("Splitting cannot help", error.Message);
        }

        [Fact]
        public void AnEnvelopePastTheCapIsRefusedByTheSenderRatherThanByEveryReceiver() {
            var records = new List<StateChannelRecord<StateChannelTestState>>();

            for (int index = 0; index <= StateChannelEnvelope<StateChannelTestState>.MaxRecordCount; index++) {
                records.Add(new StateChannelRecord<StateChannelTestState>(
                    (ushort)index, 1u, new StateChannelTestState(index, 0f, 0)));
            }

            var envelope = new StateChannelEnvelope<StateChannelTestState>(1u, records);
            var buffer = new byte[64 * 1024];

            Assert.Throws<NetProtocolException>(() => EncodeEnvelope(in envelope, buffer));
        }

        [Fact]
        public void ATruncatedEnvelopeIsRefusedByTheReaderRatherThanReadingPastTheDatagram() {
            var records = new List<StateChannelRecord<StateChannelTestState>> {
                new StateChannelRecord<StateChannelTestState>(1, 5u, new StateChannelTestState(1f, 2f, 3)),
                new StateChannelRecord<StateChannelTestState>(2, 6u, new StateChannelTestState(4f, 5f, 6)),
            };

            var envelope = new StateChannelEnvelope<StateChannelTestState>(9u, records);
            var buffer = new byte[256];
            int written = EncodeEnvelope(in envelope, buffer);

            for (int length = 1; length < written; length++) {
                Assert.Throws<NetProtocolException>(() => DecodeEnvelope(buffer, length));
            }
        }

        [Fact]
        public void AStateThatThrowsSomethingOtherThanAProtocolErrorEscapesTheClientPump() {
            using var world = new LoopbackPair();
            var channel = new ClientStateChannel<ThrowingTestState>(world.Client, ChannelMessageId);

            try {
                var envelope = new StateChannelEnvelope<ThrowingTestState>(
                    1u,
                    new List<StateChannelRecord<ThrowingTestState>> {
                        new StateChannelRecord<ThrowingTestState>(1, 1u, new ThrowingTestState()),
                    });

                world.Server.SendToMany(world.Server.Peers, ChannelMessageId, in envelope, DeliveryClass.ReliableOrdered);

                // NetClient.HandleData catches only NetProtocolException, and widening that is a
                // netcode-wide decision rather than this channel's. Both halves of the channel document
                // the contract instead: a game's TState codec must not throw anything else.
                Assert.Throws<InvalidOperationException>(() => world.Client.Update(1f / 30f));
            }
            finally {
                channel.Dispose();
            }
        }

        [Fact]
        public void AClientChannelIsRefusedAnIdTheServerSideGuardWouldHaveRefused() {
            using var world = new LoopbackPair();

            // The router refuses only the second registration, so a channel bound before
            // ClientReplication attaches would otherwise win the snapshot id and leave the library's own
            // registration as the one that fails.
            Assert.Throws<ArgumentException>(() => new ClientStateChannel<StateChannelTestState>(
                world.Client, AlpineLib.Netcode.Replication.Messages.ReplicationMessageIds.Snapshot));

            Assert.False(world.Client.Router.IsRegistered(
                AlpineLib.Netcode.Replication.Messages.ReplicationMessageIds.Snapshot));
        }

        [Fact]
        public void DisposingFromInsideAnUpdatedHandlerStopsTheRestOfTheEnvelope() {
            using var world = new LoopbackPair();
            var channel = new ClientStateChannel<StateChannelTestState>(world.Client, ChannelMessageId);
            var seen = new List<ushort>();

            void OnUpdated(ushort id, StateChannelTestState state, uint tick) {
                seen.Add(id);
                channel.Dispose();
            }

            channel.Updated += OnUpdated;

            var envelope = new StateChannelEnvelope<StateChannelTestState>(
                1u,
                new List<StateChannelRecord<StateChannelTestState>> {
                    new StateChannelRecord<StateChannelTestState>(1, 1u, new StateChannelTestState(1f, 0f, 0)),
                    new StateChannelRecord<StateChannelTestState>(2, 1u, new StateChannelTestState(2f, 0f, 0)),
                    new StateChannelRecord<StateChannelTestState>(3, 1u, new StateChannelTestState(3f, 0f, 0)),
                });

            world.Server.SendToMany(world.Server.Peers, ChannelMessageId, in envelope, DeliveryClass.ReliableOrdered);
            world.Client.Update(1f / 30f);

            Assert.Equal(new ushort[] { 1 }, seen);
            Assert.False(world.Client.Router.IsRegistered(ChannelMessageId));
        }

        [Fact]
        public void AKeyframeRestatingTheSameTickRaisesUpdatedOnceAndNeverAgain() {
            using var world = new LoopbackPair();
            var channel = new ClientStateChannel<StateChannelTestState>(world.Client, ChannelMessageId);
            int updates = 0;
            channel.Updated += (id, state, tick) => updates++;

            try {
                for (int repeat = 0; repeat < 3; repeat++) {
                    PublishOne(world, (uint)(100 + repeat), 50u, 7f);
                    world.Client.Update(1f / 30f);
                }

                // A state is its subject and its tick together, so a keyframe restating a parked subject
                // is a duplicate rather than an event an edge-triggered listener has to filter out.
                Assert.Equal(1, updates);
            }
            finally {
                channel.Dispose();
            }
        }

        [Fact]
        public void AChannelClearedForANewConnectionAcceptsTheLowerTicksOfTheNewServer() {
            using var world = new LoopbackPair();
            var channel = new ClientStateChannel<StateChannelTestState>(world.Client, ChannelMessageId);

            try {
                PublishOne(world, 4000u, 4000u, 100f);
                world.Client.Update(1f / 30f);

                // The listen host went back to the lobby and stood a fresh NetServer up, so its tick
                // counter restarted near zero.
                channel.Clear();

                Assert.Empty(channel.Ids);
                Assert.Equal(0u, channel.LastServerTick);

                PublishOne(world, 5u, 5u, 7f);
                world.Client.Update(1f / 30f);

                Assert.True(channel.TryGet(1, out StateChannelTestState held, out uint heldTick));
                Assert.Equal(7f, held.Distance);
                Assert.Equal(5u, heldTick);
                Assert.Equal(5u, channel.LastServerTick);
            }
            finally {
                channel.Dispose();
            }
        }

        [Fact]
        public void AChannelCarriedAcrossASessionWithoutBeingClearedIsDeafToTheLowerTicks() {
            using var world = new LoopbackPair();
            var channel = new ClientStateChannel<StateChannelTestState>(world.Client, ChannelMessageId);

            try {
                PublishOne(world, 4000u, 4000u, 100f);
                world.Client.Update(1f / 30f);

                PublishOne(world, 5u, 5u, 7f);
                world.Client.Update(1f / 30f);

                // Documented, and the reason Clear exists: the tick floor is only meaningful within one
                // server's lifetime, so a channel carried across connections must be emptied between them.
                Assert.True(channel.TryGet(1, out StateChannelTestState held, out uint heldTick));
                Assert.Equal(100f, held.Distance);
                Assert.Equal(4000u, heldTick);
            }
            finally {
                channel.Dispose();
            }
        }

        [Fact]
        public void ASecondClientChannelOnTheSameIdIsRefusedByTheRouterRatherThanStealingIt() {
            using var world = new LoopbackPair();
            var first = new ClientStateChannel<StateChannelTestState>(world.Client, ChannelMessageId);

            try {
                Assert.Throws<InvalidOperationException>(
                    () => new ClientStateChannel<StateChannelTestState>(world.Client, ChannelMessageId));

                Assert.True(world.Client.Router.IsRegistered(ChannelMessageId));
            }
            finally {
                first.Dispose();
            }
        }

        [Fact]
        public void AFrameHitchCostsOneLateSendRatherThanABurstAtDoubleTheConfiguredRate() {
            using var world = new OfflineWorld();

            world.Channel.Set(1, new StateChannelTestState(1f, 1f, 0), 1u);
            world.Channel.Tick(1u, 2f);
            world.Transport.Sent.Clear();

            int broadcasts = 0;

            for (int tick = 0; tick < 30; tick++) {
                world.Channel.Set(1, new StateChannelTestState(tick, 1f, 0), (uint)(2 + tick));
                world.Transport.Sent.Clear();
                world.Channel.Tick((uint)(2 + tick), 1f / 30f);

                if (world.Transport.Sent.Count > 0) {
                    broadcasts++;
                }
            }

            // 15 Hz snapshot against a 30 Hz tick. The hitch's debt was dropped rather than banked, so
            // the channel is back on cadence at once instead of sending at 30 Hz until it drains.
            Assert.Equal(15, broadcasts);
        }

        [Fact]
        public void AKeyframeClearsTheDirtySetSoTheNextSnapshotDoesNotResendWhatWentOutReliably() {
            using var world = new OfflineWorld();

            world.Channel.Set(1, new StateChannelTestState(1f, 1f, 0), 1u);
            world.Channel.BroadcastKeyframe(1u);
            world.Transport.Sent.Clear();

            world.Channel.BroadcastDirty(2u);

            Assert.Empty(world.Transport.Sent);
        }

        [Fact]
        public void AKeyframeSentBeforeTheFirstTickIsStampedWithTickZero() {
            using var world = new OfflineWorld();

            world.Channel.Set(1, new StateChannelTestState(1f, 1f, 0), 900u);
            world.Channel.SendKeyframeTo(new PeerHandle(1));

            Assert.Single(world.Transport.Sent);

            StateChannelEnvelope<StateChannelTestState> envelope = world.Transport.DecodeLast();

            // The record carries the real tick, but the envelope's own tick is whatever the last Tick()
            // call left behind — zero on a channel that has never been pumped.
            Assert.Equal(0u, envelope.ServerTick);
            Assert.Equal(900u, envelope.Records[0].Tick);
        }

        /// <summary>Publishes a single-record envelope for subject 1 straight down the wire.</summary>
        private static void PublishOne(LoopbackPair world, uint serverTick, uint recordTick, float distance) {
            var envelope = new StateChannelEnvelope<StateChannelTestState>(
                serverTick,
                new List<StateChannelRecord<StateChannelTestState>> {
                    new StateChannelRecord<StateChannelTestState>(1, recordTick, new StateChannelTestState(distance, 0f, 0)),
                });

            world.Server.SendToMany(world.Server.Peers, ChannelMessageId, in envelope, DeliveryClass.ReliableOrdered);
        }

        /// <summary>A ref-struct writer cannot be touched from a lambda, so the throwing call lives here.</summary>
        private static int EncodeEnvelope(in StateChannelEnvelope<StateChannelTestState> envelope, byte[] buffer) {
            var writer = new NetWriter(buffer);
            envelope.Serialize(ref writer);
            return writer.Written;
        }

        private static void DecodeEnvelope(byte[] buffer, int length) {
            var reader = new NetReader(buffer, 0, length);
            StateChannelEnvelope<StateChannelTestState> envelope = default;
            envelope.Deserialize(ref reader);
        }

        private static NetConfig BuildOfflineConfig() {
            return new NetConfig {
                GameProtocolName = "statechannel-review",
                Port = 0,
                MaxPeers = 8,
                ServerTickRate = 30,
                SnapshotRate = 15,
                ClientSendRate = 30,
            };
        }

        /// <summary>A server and a channel with no peer ever connecting, so a send can be inspected raw.</summary>
        private sealed class OfflineWorld : IDisposable {
            private readonly NetServer server;
            private readonly ServerStateChannel<StateChannelTestState> channel;

            public OfflineWorld() {
                NetConfig config = BuildOfflineConfig();

                Transport = new RecordingTransport();
                server = new NetServer(Transport, config);
                channel = new ServerStateChannel<StateChannelTestState>(
                    server, () => new[] { new PeerHandle(1) }, ChannelMessageId, config);
            }

            /// <summary>The channel under probe.</summary>
            public ServerStateChannel<StateChannelTestState> Channel => channel;

            /// <summary>Everything the channel handed the transport.</summary>
            public RecordingTransport Transport { get; }

            public void Dispose() {
                server.Dispose();
            }
        }

        /// <summary>The same rig over a payload big enough that one record cannot fit a datagram.</summary>
        private sealed class BulkyWorld : IDisposable {
            private readonly NetServer server;
            private readonly ServerStateChannel<BulkyTestState> channel;

            public BulkyWorld() {
                NetConfig config = BuildOfflineConfig();

                Transport = new RecordingTransport();
                server = new NetServer(Transport, config);
                channel = new ServerStateChannel<BulkyTestState>(
                    server, () => new[] { new PeerHandle(1) }, ChannelMessageId, config);
            }

            /// <summary>Payload bytes that push a record just past an envelope's room but not past the buffer.</summary>
            public static int OversizedPayloadBytes =>
                ServerStateChannel<BulkyTestState>.MaxRecordBytesPerEnvelope - 10;

            /// <summary>The channel under probe.</summary>
            public ServerStateChannel<BulkyTestState> Channel => channel;

            /// <summary>Everything the channel handed the transport.</summary>
            public RecordingTransport Transport { get; }

            public void Dispose() {
                server.Dispose();
            }
        }

        /// <summary>Keeps every payload handed to it, so a test can decode what actually went out.</summary>
        private sealed class RecordingTransport : INetTransport {
            /// <summary>Payload bytes, one entry per send, framing included.</summary>
            public List<byte[]> Sent { get; } = new List<byte[]>();

            /// <summary>While true every send throws, standing in for a transport refusing a datagram.</summary>
            public bool FailSends { get; set; }

            /// <summary>Never raised: nothing ever connects to a transport that only records.</summary>
            public event Action<PeerHandle> OnPeerConnected { add { } remove { } }

            /// <summary>Never raised.</summary>
            public event Action<PeerHandle, DisconnectReason> OnPeerDisconnected { add { } remove { } }

            /// <summary>Never raised.</summary>
            public event Action<PeerHandle, ArraySegment<byte>, DeliveryClass> OnData { add { } remove { } }

            /// <summary>Decodes the body of the last payload sent as a state channel envelope.</summary>
            public StateChannelEnvelope<StateChannelTestState> DecodeLast() {
                return Decode(Sent[Sent.Count - 1]);
            }

            /// <summary>Records across every payload sent, which is what chunking spreads them over.</summary>
            public int TotalRecordsSent() {
                int total = 0;

                for (int sentIndex = 0; sentIndex < Sent.Count; sentIndex++) {
                    total += Decode(Sent[sentIndex]).Records.Count;
                }

                return total;
            }

            public void StartServer(int port, int maxPeers, string protocolKey) { }

            public void StartClient(string protocolKey) { }

            public void Connect(NetEndpoint endpoint) { }

            public void Disconnect(PeerHandle peer) { }

            public void Send(PeerHandle peer, ReadOnlySpan<byte> payload, DeliveryClass delivery) {
                if (FailSends) {
                    throw new InvalidOperationException("The transport refused the datagram.");
                }

                Sent.Add(payload.ToArray());
            }

            public void Stop() { }

            public void Poll() { }

            public int GetPingMs(PeerHandle peer) {
                return 0;
            }

            public void Dispose() {
            }

            private static StateChannelEnvelope<StateChannelTestState> Decode(byte[] payload) {
                var reader = new NetReader(payload, NetEnvelope.HeaderSize, payload.Length - NetEnvelope.HeaderSize);
                StateChannelEnvelope<StateChannelTestState> envelope = default;
                envelope.Deserialize(ref reader);
                return envelope;
            }
        }

        /// <summary>A connected server and client over the in-memory transport, with nothing else on them.</summary>
        private sealed class LoopbackPair : IDisposable {
            private static int nextPort = 45500;

            private readonly FakeNetTransport serverTransport = new FakeNetTransport();
            private readonly FakeNetTransport clientTransport = new FakeNetTransport();

            public LoopbackPair() {
                int port = System.Threading.Interlocked.Increment(ref nextPort);

                var config = new NetConfig {
                    GameProtocolName = "statechannel-review",
                    Port = port,
                    MaxPeers = 8,
                    ServerTickRate = 30,
                    SnapshotRate = 15,
                    ClientSendRate = 30,
                };

                Server = new NetServer(serverTransport, config);
                Server.Start();

                Client = new NetClient(clientTransport, config);
                Client.Connect(NetEndpoint.Direct("127.0.0.1", port));

                for (int attempt = 0; attempt < 32 && !Client.IsConnected; attempt++) {
                    Server.Update(1f / 30f);
                    Client.Update(1f / 30f);
                }

                Assert.True(Client.IsConnected, "The review loopback pair never connected.");
            }

            /// <summary>The server half.</summary>
            public NetServer Server { get; }

            /// <summary>The client half.</summary>
            public NetClient Client { get; }

            public void Dispose() {
                Client.Dispose();
                Server.Dispose();
            }
        }

        /// <summary>A game payload whose decoder fails the way a game's own bug would.</summary>
        private struct ThrowingTestState : INetMessage {
            /// <inheritdoc />
            public void Serialize(ref NetWriter writer) {
                writer.WriteInt(0);
            }

            /// <inheritdoc />
            public void Deserialize(ref NetReader reader) {
                reader.ReadInt();
                throw new InvalidOperationException("The game's own decoder failed.");
            }
        }

        /// <summary>A game payload sized by the caller, for probing the per-record ceiling.</summary>
        private struct BulkyTestState : INetMessage {
            private int payloadBytes;

            public BulkyTestState(int payloadBytes) {
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
    }
}
