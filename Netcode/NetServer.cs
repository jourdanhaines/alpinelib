using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Messages;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Timing;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode {
    /// <summary>
    /// The authoritative end of a connection: a transport, a message router, a tick counter and a work
    /// inbox bundled into the one object the session and replication layers talk to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The facade owns framing and nothing else. Every payload it sends is a two-byte message id
    /// followed by the message body, and every payload it receives is split back apart the same way —
    /// which is why the chat pipe can ride the same socket as gameplay without either side knowing
    /// anything about the other's codec (see <see cref="SendRaw"/>).
    /// </para>
    /// <para>
    /// <b>Threading.</b> Everything here happens on whichever thread calls <see cref="Update"/> — the
    /// Unity main thread on a listen host, the fixed-step game-loop thread on the dedicated server.
    /// Transport events fire synchronously inside the poll, so handlers run on that same thread; work
    /// arriving from anywhere else must go through <see cref="Inbox"/>.
    /// </para>
    /// <para>
    /// The facade is deliberately session-agnostic. It knows about connections, not about players,
    /// rosters or entities: a <c>SessionHost</c> layers those on top and broadcasts through
    /// <see cref="SendToMany{TMessage}"/> with its own member list, so one server can host many
    /// sessions over one socket.
    /// </para>
    /// </remarks>
    public sealed class NetServer : IDisposable {
        /// <summary>How often the authoritative tick is broadcast to every peer.</summary>
        private const double ClockSyncIntervalSeconds = 1.0;

        /// <summary>
        /// Ceiling on ticks advanced by a single update. A stalled process — a breakpoint, a paused
        /// editor, a long GC — must not be paid back as a burst of simulation that stalls it further.
        /// </summary>
        private const int MaxCatchUpTicks = 32;

        /// <summary>Grace given to a kick notice to reach the wire before the connection is closed.</summary>
        private const double KickGraceSeconds = 0.1;

        private readonly INetTransport transport;
        private readonly NetConfig config;
        private readonly MessageRouter router = new MessageRouter();
        private readonly TickInbox inbox = new TickInbox();
        private readonly NetBufferPool bufferPool = new NetBufferPool();
        private readonly Dictionary<ushort, RawMessageHandler> rawHandlers = new Dictionary<ushort, RawMessageHandler>();
        private readonly List<PeerHandle> peers = new List<PeerHandle>();
        private readonly List<PendingKick> pendingKicks = new List<PendingKick>();

        private double tickAccumulatorSeconds;
        private double clockSyncAccumulatorSeconds;
        private uint tick;
        private bool isRunning;
        private bool disposed;

        public NetServer(INetTransport transport, NetConfig config) {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.config = config ?? throw new ArgumentNullException(nameof(config));

            transport.OnPeerConnected += HandlePeerConnected;
            transport.OnPeerDisconnected += HandlePeerDisconnected;
            transport.OnData += HandleData;
        }

        /// <summary>A peer finished connecting. It is authenticated by nothing yet — that is the session's job.</summary>
        public event Action<PeerHandle> OnPeerConnected;

        /// <summary>A peer's link ended, for whatever reason the transport reports.</summary>
        public event Action<PeerHandle, DisconnectReason> OnPeerDisconnected;

        /// <summary>
        /// A peer sent something that could not be decoded. Reported rather than thrown: a malformed
        /// datagram is a peer-scoped fault, and one bad client must never take the server's loop down.
        /// </summary>
        public event Action<PeerHandle, NetProtocolException> OnMalformedMessage;

        /// <summary>Where every layer above registers its typed message handlers.</summary>
        public MessageRouter Router => router;

        /// <summary>Marshals asynchronous results — auth validation, backend calls — onto the tick thread.</summary>
        public TickInbox Inbox => inbox;

        /// <summary>The authoritative tick counter, advanced at <c>NetConfig.ServerTickRate</c>.</summary>
        public uint Tick => tick;

        /// <summary>Simulation time in seconds, derived from the tick counter.</summary>
        public double ServerSeconds => tick * (double)config.ServerTickInterval;

        /// <summary>Every currently connected peer. The list is reused; do not retain it across updates.</summary>
        public IReadOnlyList<PeerHandle> Peers => peers;

        /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
        public bool IsRunning => isRunning;

        /// <summary>Binds the socket and begins accepting peers whose connect key matches this build.</summary>
        public void Start() {
            ThrowIfDisposed();
            if (isRunning) {
                throw new InvalidOperationException("NetServer is already running.");
            }

            transport.StartServer(config.Port, config.MaxPeers, config.BuildConnectKey());
            tick = 0;
            tickAccumulatorSeconds = 0.0;
            clockSyncAccumulatorSeconds = 0.0;
            isRunning = true;
        }

        /// <summary>
        /// One pump of the connection: run queued work, deliver everything received, advance the tick
        /// counter and keep clients' clocks anchored. Call once per frame or per fixed step.
        /// </summary>
        /// <remarks>
        /// The inbox is drained before the poll on purpose. Work posted from a background thread —
        /// an auth verdict, most often — lands before the messages that may depend on it, so a client
        /// never has a message rejected by state that was already resolved a moment earlier.
        /// </remarks>
        public void Update(float deltaSeconds) {
            if (!isRunning) {
                return;
            }

            inbox.Drain();
            transport.Poll();
            CompletePendingKicks(deltaSeconds);
            AdvanceTick(deltaSeconds);
            BroadcastClockSync(deltaSeconds);
        }

        /// <summary>Sends one typed message to one peer.</summary>
        public void Send<TMessage>(PeerHandle peer, ushort messageId, in TMessage message, DeliveryClass delivery)
            where TMessage : struct, INetMessage {
            byte[] buffer = bufferPool.Rent();
            try {
                int written = NetEnvelope.Frame(buffer, messageId, in message);
                transport.Send(peer, new ReadOnlySpan<byte>(buffer, 0, written), delivery);
            }
            finally {
                bufferPool.Return(buffer);
            }
        }

        /// <summary>
        /// Sends one typed message to several peers, serializing it exactly once. This is how every
        /// session-scoped broadcast goes out: the caller supplies the membership, the facade supplies
        /// the wire.
        /// </summary>
        public void SendToMany<TMessage>(IReadOnlyList<PeerHandle> targets, ushort messageId, in TMessage message, DeliveryClass delivery)
            where TMessage : struct, INetMessage {
            if (targets == null || targets.Count == 0) {
                return;
            }

            byte[] buffer = bufferPool.Rent();
            try {
                int written = NetEnvelope.Frame(buffer, messageId, in message);
                SendFramed(targets, buffer, written, delivery);
            }
            finally {
                bufferPool.Return(buffer);
            }
        }

        /// <summary>
        /// Sends a body the netcode layer does not decode under an envelope id — the chat pipe. The
        /// payload is framed identically to a typed message, so the receiving facade splits the id off
        /// and hands the rest to whoever registered for it.
        /// </summary>
        public void SendRaw(PeerHandle peer, ushort envelopeId, ReadOnlySpan<byte> payload, DeliveryClass delivery) {
            byte[] buffer = bufferPool.Rent();
            try {
                int written = NetEnvelope.FrameRaw(buffer, envelopeId, payload);
                transport.Send(peer, new ReadOnlySpan<byte>(buffer, 0, written), delivery);
            }
            finally {
                bufferPool.Return(buffer);
            }
        }

        /// <summary>Claims an envelope id for a handler that decodes its own body. Throws on a double claim.</summary>
        public void RegisterRaw(ushort envelopeId, RawMessageHandler handler) {
            if (handler == null) {
                throw new ArgumentNullException(nameof(handler));
            }

            if (rawHandlers.ContainsKey(envelopeId)) {
                throw new InvalidOperationException($"Envelope id {envelopeId} is already registered.");
            }

            rawHandlers.Add(envelopeId, handler);
        }

        /// <summary>Releases an envelope id claimed by <see cref="RegisterRaw"/>.</summary>
        public void UnregisterRaw(ushort envelopeId) {
            rawHandlers.Remove(envelopeId);
        }

        /// <summary>Tells a peer why it is being dropped, then drops it.</summary>
        public void Kick(PeerHandle peer, DisconnectReason reason) {
            Kick(peer, reason, string.Empty);
        }

        /// <summary>Kicks a peer with an explanation for the player.</summary>
        /// <remarks>
        /// The close is deferred by <see cref="KickGraceSeconds"/> rather than done here. Closing a
        /// connection discards whatever is still queued on it, so a notice sent and then immediately
        /// followed by a disconnect would usually die in the outbound queue — leaving the client with a
        /// bare "connection lost" and no idea it was kicked, which is the exact failure the notice
        /// exists to prevent.
        /// </remarks>
        public void Kick(PeerHandle peer, DisconnectReason reason, string message) {
            var notice = new DisconnectNotice(reason, message);
            Send(peer, CoreMessageIds.DisconnectNotice, in notice, DeliveryClass.ReliableOrdered);
            pendingKicks.Add(new PendingKick(peer, KickGraceSeconds));
        }

        /// <summary>Closes every connection and releases the socket. The server may be started again.</summary>
        public void Stop() {
            if (!isRunning) {
                return;
            }

            isRunning = false;
            transport.Stop();
            peers.Clear();
            pendingKicks.Clear();
            inbox.Clear();
        }

        /// <summary>Round-trip time to a peer in milliseconds, or negative when the peer is unknown.</summary>
        public int GetPingMs(PeerHandle peer) {
            return transport.GetPingMs(peer);
        }

        /// <inheritdoc />
        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            Stop();

            transport.OnPeerConnected -= HandlePeerConnected;
            transport.OnPeerDisconnected -= HandlePeerDisconnected;
            transport.OnData -= HandleData;

            router.Clear();
            rawHandlers.Clear();
            transport.Dispose();
        }

        private void AdvanceTick(float deltaSeconds) {
            if (deltaSeconds <= 0f) {
                return;
            }

            double interval = config.ServerTickInterval;
            double ceiling = interval * MaxCatchUpTicks;
            tickAccumulatorSeconds = Math.Min(tickAccumulatorSeconds + deltaSeconds, ceiling);

            while (tickAccumulatorSeconds >= interval) {
                tickAccumulatorSeconds -= interval;
                tick++;
            }
        }

        private void BroadcastClockSync(float deltaSeconds) {
            clockSyncAccumulatorSeconds += deltaSeconds;
            if (clockSyncAccumulatorSeconds < ClockSyncIntervalSeconds) {
                return;
            }

            clockSyncAccumulatorSeconds = 0.0;
            if (peers.Count == 0) {
                return;
            }

            var sync = new ClockSync(tick);
            SendToMany(peers, CoreMessageIds.ClockSync, in sync, DeliveryClass.UnreliableSequenced);
        }

        private void SendFramed(IReadOnlyList<PeerHandle> targets, byte[] buffer, int written, DeliveryClass delivery) {
            var framed = new ReadOnlySpan<byte>(buffer, 0, written);
            for (int index = 0; index < targets.Count; index++) {
                transport.Send(targets[index], framed, delivery);
            }
        }

        private void HandlePeerConnected(PeerHandle peer) {
            peers.Add(peer);

            // Seed the newcomer's clock rather than making it wait out the next broadcast: a client that
            // cannot place the server's timeline cannot interpolate anything it receives, and a whole
            // second of that is a second of a visibly broken join. The seed goes reliably because
            // there is no second chance at it — the periodic syncs that follow can afford to be lossy.
            var sync = new ClockSync(tick);
            Send(peer, CoreMessageIds.ClockSync, in sync, DeliveryClass.ReliableOrdered);

            OnPeerConnected?.Invoke(peer);
        }

        private void HandlePeerDisconnected(PeerHandle peer, DisconnectReason reason) {
            peers.Remove(peer);
            pendingKicks.RemoveAll(kick => kick.Peer == peer);
            OnPeerDisconnected?.Invoke(peer, reason);
        }

        private void CompletePendingKicks(float deltaSeconds) {
            for (int index = pendingKicks.Count - 1; index >= 0; index--) {
                PendingKick kick = pendingKicks[index].Aged(deltaSeconds);
                if (kick.HasGraceLeft) {
                    pendingKicks[index] = kick;
                    continue;
                }

                pendingKicks.RemoveAt(index);
                transport.Disconnect(kick.Peer);
            }
        }

        private void HandleData(PeerHandle sender, ArraySegment<byte> payload, DeliveryClass delivery) {
            try {
                NetEnvelope.Deliver(payload, sender, router, rawHandlers);
            }
            catch (NetProtocolException error) {
                OnMalformedMessage?.Invoke(sender, error);
            }
        }

        private void ThrowIfDisposed() {
            if (disposed) {
                throw new ObjectDisposedException(nameof(NetServer));
            }
        }

        /// <summary>A peer that has been told why it is going and is waiting out its send grace.</summary>
        private readonly struct PendingKick {
            private readonly double remainingSeconds;

            public PendingKick(PeerHandle peer, double remainingSeconds) {
                Peer = peer;
                this.remainingSeconds = remainingSeconds;
            }

            /// <summary>The peer to close once the grace runs out.</summary>
            public PeerHandle Peer { get; }

            /// <summary>False once the notice has had its time and the connection should go.</summary>
            public bool HasGraceLeft => remainingSeconds > 0.0;

            /// <summary>Returns the same kick with one frame's worth of grace spent.</summary>
            public PendingKick Aged(double deltaSeconds) {
                return new PendingKick(Peer, remainingSeconds - deltaSeconds);
            }
        }
    }
}
