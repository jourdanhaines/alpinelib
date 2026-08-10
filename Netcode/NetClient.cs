using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Messages;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Timing;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode {
    /// <summary>
    /// The player's end of a connection: one transport, one message router, one clock estimate and one
    /// work inbox, wrapped so nothing above has to know there is a socket underneath.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A client has exactly one counterpart, so every send here is implicitly addressed to the server
    /// and every receive implicitly comes from it. Handlers are still passed a
    /// <see cref="PeerHandle"/> for signature parity with the server side, and it is always the handle
    /// of the server link.
    /// </para>
    /// <para>
    /// The facade owns the clock. It registers <see cref="CoreMessageIds.ClockSync"/> itself and folds
    /// each observation into <see cref="Clock"/> together with the transport's current round-trip time,
    /// so interpolation and prediction upstream get a usable timeline without anyone else wiring it.
    /// </para>
    /// </remarks>
    public sealed class NetClient : IDisposable {
        private readonly INetTransport transport;
        private readonly NetConfig config;
        private readonly MessageRouter router = new MessageRouter();
        private readonly TickInbox inbox = new TickInbox();
        private readonly NetBufferPool bufferPool = new NetBufferPool();
        private readonly Dictionary<ushort, RawMessageHandler> rawHandlers = new Dictionary<ushort, RawMessageHandler>();
        private readonly NetClock clock;

        private PeerHandle serverPeer = PeerHandle.None;
        private ConnectionState state = ConnectionState.Disconnected;
        private DisconnectReason? noticedReason;
        private uint lastObservedTick;
        private bool hasObservedTick;
        private bool disposed;

        public NetClient(INetTransport transport, NetConfig config) {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.config = config ?? throw new ArgumentNullException(nameof(config));

            clock = new NetClock(config);

            transport.OnPeerConnected += HandlePeerConnected;
            transport.OnPeerDisconnected += HandlePeerDisconnected;
            transport.OnData += HandleData;

            router.Register<ClockSync>(CoreMessageIds.ClockSync, HandleClockSync);
            router.Register<DisconnectNotice>(CoreMessageIds.DisconnectNotice, HandleDisconnectNotice);
        }

        /// <summary>The link came up. Auth and session attach happen after this, not before.</summary>
        public event Action OnConnected;

        /// <summary>
        /// The link ended. The reason is the server's own if it sent a <see cref="DisconnectNotice"/>
        /// first, otherwise whatever the transport observed.
        /// </summary>
        public event Action<DisconnectReason> OnDisconnected;

        /// <summary>Text the server attached to its disconnect notice, when there was any.</summary>
        public event Action<string> OnDisconnectMessage;

        /// <summary>
        /// The server sent something that could not be decoded — a version skew that slipped past the
        /// connect key, or a bug. Reported rather than thrown so a frame is never lost to it.
        /// </summary>
        public event Action<NetProtocolException> OnMalformedMessage;

        /// <summary>Where every layer above registers its typed message handlers.</summary>
        public MessageRouter Router => router;

        /// <summary>Marshals asynchronous results onto the frame thread.</summary>
        public TickInbox Inbox => inbox;

        /// <summary>The running estimate of the server's simulation clock.</summary>
        public NetClock Clock => clock;

        /// <summary>Where this client sits in its connection lifecycle.</summary>
        public ConnectionState State => state;

        /// <summary>True only in <see cref="ConnectionState.Connected"/>.</summary>
        public bool IsConnected => state == ConnectionState.Connected;

        /// <summary>Handle of the server link, or <see cref="PeerHandle.None"/> while disconnected.</summary>
        public PeerHandle ServerPeer => serverPeer;

        /// <summary>Round-trip time to the server in milliseconds, or negative while disconnected.</summary>
        public int PingMs => transport.GetPingMs(serverPeer);

        /// <summary>
        /// Dials the server. Completion — or failure — arrives during a later <see cref="Update"/> as
        /// <see cref="OnConnected"/> or <see cref="OnDisconnected"/>; nothing here blocks.
        /// </summary>
        public void Connect(NetEndpoint endpoint) {
            ThrowIfDisposed();
            if (!endpoint.IsValid) {
                throw new ArgumentException("Cannot connect to an incomplete endpoint.", nameof(endpoint));
            }

            if (state != ConnectionState.Disconnected) {
                throw new InvalidOperationException($"NetClient cannot connect while {state}.");
            }

            // Release any socket left over from a previous session before opening a new one: a rejoin
            // runs through this same path, and the transport refuses to start twice.
            transport.Stop();
            clock.Reset();
            noticedReason = null;
            lastObservedTick = 0u;
            hasObservedTick = false;

            transport.StartClient(config.BuildConnectKey());
            transport.Connect(endpoint);
            state = ConnectionState.Connecting;
        }

        /// <summary>
        /// One pump of the connection: run queued work, deliver everything received, then free-run the
        /// clock forward by this frame. Call once per frame.
        /// </summary>
        public void Update(float deltaSeconds) {
            if (state == ConnectionState.Disconnected) {
                return;
            }

            inbox.Drain();
            transport.Poll();
            clock.Advance(deltaSeconds);
        }

        /// <summary>Sends one typed message to the server.</summary>
        public void Send<TMessage>(ushort messageId, in TMessage message, DeliveryClass delivery)
            where TMessage : struct, INetMessage {
            byte[] buffer = bufferPool.Rent();
            try {
                int written = NetEnvelope.Frame(buffer, messageId, in message);
                transport.Send(serverPeer, new ReadOnlySpan<byte>(buffer, 0, written), delivery);
            }
            finally {
                bufferPool.Return(buffer);
            }
        }

        /// <summary>
        /// Sends a body this layer does not decode under an envelope id — the chat pipe. Framing matches
        /// a typed send exactly, so the server splits the id off and routes the rest by registration.
        /// </summary>
        public void SendRaw(ushort envelopeId, ReadOnlySpan<byte> payload, DeliveryClass delivery) {
            byte[] buffer = bufferPool.Rent();
            try {
                int written = NetEnvelope.FrameRaw(buffer, envelopeId, payload);
                transport.Send(serverPeer, new ReadOnlySpan<byte>(buffer, 0, written), delivery);
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

        /// <summary>
        /// Closes the link gracefully. The state settles to
        /// <see cref="ConnectionState.Disconnected"/> when the transport confirms it during a poll.
        /// </summary>
        public void Disconnect() {
            if (state == ConnectionState.Disconnected) {
                return;
            }

            if (!serverPeer.IsValid) {
                // The dial never completed, so there is nothing to close politely and no disconnect
                // event will ever arrive for it. Tear the socket down and settle the state here.
                transport.Stop();
                FinishDisconnect(DisconnectReason.Graceful);
                return;
            }

            state = ConnectionState.Disconnecting;
            transport.Disconnect(serverPeer);
        }

        /// <inheritdoc />
        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            state = ConnectionState.Disconnected;
            serverPeer = PeerHandle.None;

            transport.OnPeerConnected -= HandlePeerConnected;
            transport.OnPeerDisconnected -= HandlePeerDisconnected;
            transport.OnData -= HandleData;

            router.Clear();
            rawHandlers.Clear();
            inbox.Clear();
            transport.Dispose();
        }

        private void HandlePeerConnected(PeerHandle peer) {
            serverPeer = peer;
            state = ConnectionState.Connected;
            OnConnected?.Invoke();
        }

        private void HandlePeerDisconnected(PeerHandle peer, DisconnectReason reason) {
            if (state == ConnectionState.Disconnected) {
                return;
            }

            FinishDisconnect(reason);
        }

        private void FinishDisconnect(DisconnectReason reason) {
            DisconnectReason reported = noticedReason ?? reason;
            noticedReason = null;
            serverPeer = PeerHandle.None;
            state = ConnectionState.Disconnected;
            clock.Reset();
            lastObservedTick = 0u;
            hasObservedTick = false;
            OnDisconnected?.Invoke(reported);
        }

        private void HandleData(PeerHandle sender, ArraySegment<byte> payload, DeliveryClass delivery) {
            try {
                NetEnvelope.Deliver(payload, sender, router, rawHandlers);
            }
            catch (NetProtocolException error) {
                OnMalformedMessage?.Invoke(error);
            }
        }

        private void HandleClockSync(in ClockSync message, PeerHandle sender) {
            // The reliable seed sent at connect and the unreliable periodic syncs ride different
            // delivery classes, so nothing guarantees they arrive in the order they were sent. A stamp
            // older than one already folded in carries no information and would drag the estimate
            // backwards, so it is dropped rather than smoothed.
            if (hasObservedTick && message.ServerTick <= lastObservedTick) {
                return;
            }

            lastObservedTick = message.ServerTick;
            hasObservedTick = true;
            clock.OnServerTickObserved(message.ServerTick, transport.GetPingMs(serverPeer));
        }

        private void HandleDisconnectNotice(in DisconnectNotice message, PeerHandle sender) {
            // Remember the reason: the transport-level drop that follows would otherwise arrive as a
            // plain graceful close, indistinguishable from the player quitting on their own.
            noticedReason = message.Reason;
            if (!string.IsNullOrEmpty(message.Message)) {
                OnDisconnectMessage?.Invoke(message.Message);
            }
        }

        private void ThrowIfDisposed() {
            if (disposed) {
                throw new ObjectDisposedException(nameof(NetClient));
            }
        }
    }
}
