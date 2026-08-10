using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Chat.Wire;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Chat {
    /// <summary>
    /// The built-in chat provider: chat frames ride the game's own connection, and the game server rules
    /// on every line.
    /// </summary>
    /// <remarks>
    /// <b>Threading.</b> Every event this raises fires synchronously inside the transport pump — the
    /// Unity main thread, or the server game loop — because the frames arrive there and nothing in this
    /// class moves them anywhere else. There is no dispatcher in the stack and none is needed. The
    /// <see cref="Task"/> results it hands back complete with continuations posted rather than run
    /// inline, so a caller's <c>await</c> resumes on its own synchronisation context instead of hijacking
    /// the pump.
    /// <para>
    /// <b>No reconnect resync.</b> A dropped connection fails every in-flight request immediately and
    /// clears what the provider remembers. It does not queue sends to replay later, and it does not
    /// reconcile what it missed while it was away: the session layer reconnects, the room is joined
    /// again, and the history push that comes with joining is the resync. A queue would deliver lines
    /// minutes after they stopped meaning anything, which in a chat window is worse than losing them.
    /// </para>
    /// </remarks>
    public sealed class LiteNetChatProvider : IChatProvider {
        private readonly IChatTransport _transport;
        private readonly ChatSettings _settings;
        private readonly ChatDeliveryLedger _ledger;
        private readonly byte[] _sendBuffer = ChatWireCodec.CreateBuffer();

        private readonly Dictionary<uint, PendingRequest<ChatSendResult>> _pendingSends =
            new Dictionary<uint, PendingRequest<ChatSendResult>>();

        private readonly Dictionary<uint, PendingRequest<IReadOnlyList<ChatMessage>>> _pendingFetches =
            new Dictionary<uint, PendingRequest<IReadOnlyList<ChatMessage>>>();

        private ChatIdentity _identity;
        private ChatProviderState _state = ChatProviderState.Disconnected;
        private PendingRequest<bool> _pendingConnect;
        private uint _nextNonce;
        private uint _nextRequestId;
        private bool _isSubscribed;
        private bool _disposed;

        /// <summary>Creates a provider over an already-built chat pipe.</summary>
        public LiteNetChatProvider(IChatTransport transport, ChatSettings settings) {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _ledger = new ChatDeliveryLedger(_settings.ClientViewBufferSize);
        }

        /// <inheritdoc />
        public event Action<ChatProviderState> StateChanged;

        /// <inheritdoc />
        public event Action<ChatMessage> MessageReceived;

        /// <inheritdoc />
        public event Action<ChatChannelEvent> ChannelChanged;

        /// <inheritdoc />
        public ChatProviderState State => _state;

        /// <inheritdoc />
        public ChatProviderCapabilities Capabilities =>
            ChatProviderCapabilities.History | ChatProviderCapabilities.Presence;

        /// <summary>The identity the provider last connected as.</summary>
        public ChatIdentity Identity => _identity;

        /// <summary>The room channel this provider is following.</summary>
        public ChatChannelId RoomChannel => _identity.RoomChannel;

        /// <inheritdoc />
        public Task ConnectAsync(ChatIdentity identity, CancellationToken cancellationToken) {
            ThrowIfDisposed();
            _identity = identity;
            Subscribe();

            if (_transport.IsConnected) {
                SetState(ChatProviderState.Connected);
                return Task.CompletedTask;
            }

            SetState(ChatProviderState.Connecting);
            _pendingConnect?.TryCancel();
            _pendingConnect = new PendingRequest<bool>(cancellationToken, CancelPendingConnect);
            return _pendingConnect.Task;
        }

        /// <inheritdoc />
        public Task DisconnectAsync() {
            if (_disposed) {
                return Task.CompletedTask;
            }

            FailEverythingInFlight();
            _ledger.Clear();
            SetState(ChatProviderState.Disconnected);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<ChatSendResult> SendAsync(
            ChatChannelId channel,
            string text,
            CancellationToken cancellationToken) {
            ThrowIfDisposed();

            if (_state != ChatProviderState.Connected || !_transport.IsConnected) {
                // Fail fast rather than queueing. A line the player typed while the connection was down
                // must not surface in the room several seconds later, out of context.
                return Task.FromResult(ChatSendResult.Rejected(ChatSendStatus.NotConnected));
            }

            uint nonce = NextNonce();
            var pending = new PendingRequest<ChatSendResult>(cancellationToken, () => CancelPendingSend(nonce));
            _pendingSends[nonce] = pending;

            var request = new ChatSendRequest(nonce, channel, text);
            SendFrame(ChatWireCodec.Write(_sendBuffer, in request));

            return pending.Task;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<ChatMessage>> FetchHistoryAsync(
            ChatChannelId channel,
            ulong beforeMessageId,
            int count,
            CancellationToken cancellationToken) {
            ThrowIfDisposed();

            if (_state != ChatProviderState.Connected || !_transport.IsConnected) {
                return Task.FromResult<IReadOnlyList<ChatMessage>>(Array.Empty<ChatMessage>());
            }

            uint requestId = NextRequestId();
            var pending = new PendingRequest<IReadOnlyList<ChatMessage>>(
                cancellationToken,
                () => CancelPendingFetch(requestId));

            _pendingFetches[requestId] = pending;

            var request = new ChatHistoryRequest(requestId, channel, beforeMessageId, count);
            SendFrame(ChatWireCodec.Write(_sendBuffer, in request));

            return pending.Task;
        }

        /// <inheritdoc />
        public void Dispose() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            Unsubscribe();
            FailEverythingInFlight();
            _ledger.Clear();
            SetState(ChatProviderState.Disconnected);

            StateChanged = null;
            MessageReceived = null;
            ChannelChanged = null;
        }

        private void Subscribe() {
            if (_isSubscribed) {
                return;
            }

            _isSubscribed = true;
            _transport.Connected += HandleTransportConnected;
            _transport.Disconnected += HandleTransportDisconnected;
            _transport.PayloadReceived += HandlePayload;
        }

        private void Unsubscribe() {
            if (!_isSubscribed) {
                return;
            }

            _isSubscribed = false;
            _transport.Connected -= HandleTransportConnected;
            _transport.Disconnected -= HandleTransportDisconnected;
            _transport.PayloadReceived -= HandlePayload;
        }

        private void HandleTransportConnected() {
            SetState(ChatProviderState.Connected);

            PendingRequest<bool> pending = _pendingConnect;
            _pendingConnect = null;
            pending?.TryComplete(true);
        }

        private void HandleTransportDisconnected() {
            FailEverythingInFlight();
            _ledger.Clear();
            SetState(ChatProviderState.Disconnected);
        }

        private void HandlePayload(ArraySegment<byte> payload) {
            if (payload.Array == null || payload.Count < 1) {
                return;
            }

            try {
                DispatchPayload(payload);
            }
            catch (NetProtocolException) {
                // One undecodable frame is not a reason to tear chat down: the connection is still good,
                // the next frame will very likely decode, and there is no consumer that could act on the
                // failure anyway.
            }
        }

        private void DispatchPayload(ArraySegment<byte> payload) {
            NetReader reader = ChatWireCodec.OpenPayload(payload, out ChatWireMessageType messageType);

            switch (messageType) {
                case ChatWireMessageType.SendAck:
                    CompleteSend(ChatWireCodec.ReadSendAck(ref reader));
                    return;
                case ChatWireMessageType.Broadcast:
                    DeliverMessage(ChatWireCodec.ReadBroadcast(ref reader).Message);
                    return;
                case ChatWireMessageType.HistoryResponse:
                    DeliverHistory(ChatWireCodec.ReadHistoryResponse(ref reader));
                    return;
                case ChatWireMessageType.ChannelEvent:
                    DeliverChannelEvent(ChatWireCodec.ReadChannelEvent(ref reader));
                    return;
                default:
                    // SendRequest and HistoryRequest are client to server. Receiving one means the peer
                    // is confused about which end it is, and there is nothing sensible to answer with.
                    return;
            }
        }

        private void CompleteSend(ChatSendAck ack) {
            if (!_pendingSends.TryGetValue(ack.Nonce, out PendingRequest<ChatSendResult> pending)) {
                return;
            }

            _pendingSends.Remove(ack.Nonce);
            pending.TryComplete(ack.ToResult());
        }

        private void DeliverMessage(ChatMessage message) {
            if (message == null || !_ledger.TryRecord(message.Channel, message.MessageId)) {
                return;
            }

            MessageReceived?.Invoke(message);
        }

        private void DeliverHistory(ChatHistoryResponse response) {
            if (response.RequestId != 0u) {
                CompleteFetch(response);
                return;
            }

            MergeHistoryPush(response);
        }

        /// <summary>
        /// Folds an unsolicited page into the live stream. The page overlaps whatever arrived between
        /// subscribing and the push landing, so the ledger — not the page's own ordering — decides what
        /// the consumer has already seen.
        /// </summary>
        private void MergeHistoryPush(ChatHistoryResponse response) {
            List<ChatMessage> messages = response.Messages;

            if (messages == null) {
                return;
            }

            for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++) {
                DeliverMessage(messages[messageIndex]);
            }
        }

        private void CompleteFetch(ChatHistoryResponse response) {
            if (!_pendingFetches.TryGetValue(response.RequestId, out PendingRequest<IReadOnlyList<ChatMessage>> pending)) {
                return;
            }

            _pendingFetches.Remove(response.RequestId);
            pending.TryComplete(response.Messages ?? new List<ChatMessage>());
        }

        private void DeliverChannelEvent(ChatChannelEvent channelEvent) {
            if (channelEvent.Change == ChatChannelChange.Left || channelEvent.Change == ChatChannelChange.Closed) {
                _ledger.ForgetChannel(channelEvent.Channel);
            }

            ChannelChanged?.Invoke(channelEvent);
        }

        private void FailEverythingInFlight() {
            FailPendingSends();
            FailPendingFetches();

            PendingRequest<bool> connect = _pendingConnect;
            _pendingConnect = null;
            connect?.TryFail(new InvalidOperationException("The chat transport dropped before the connection completed."));
        }

        private void FailPendingSends() {
            if (_pendingSends.Count < 1) {
                return;
            }

            var pending = new List<PendingRequest<ChatSendResult>>(_pendingSends.Values);
            _pendingSends.Clear();

            for (int pendingIndex = 0; pendingIndex < pending.Count; pendingIndex++) {
                pending[pendingIndex].TryComplete(ChatSendResult.Rejected(ChatSendStatus.NotConnected));
            }
        }

        private void FailPendingFetches() {
            if (_pendingFetches.Count < 1) {
                return;
            }

            var pending = new List<PendingRequest<IReadOnlyList<ChatMessage>>>(_pendingFetches.Values);
            _pendingFetches.Clear();

            for (int pendingIndex = 0; pendingIndex < pending.Count; pendingIndex++) {
                pending[pendingIndex].TryComplete(Array.Empty<ChatMessage>());
            }
        }

        private void CancelPendingSend(uint nonce) {
            if (!_pendingSends.TryGetValue(nonce, out PendingRequest<ChatSendResult> pending)) {
                return;
            }

            _pendingSends.Remove(nonce);
            pending.TryCancel();
        }

        private void CancelPendingFetch(uint requestId) {
            if (!_pendingFetches.TryGetValue(requestId, out PendingRequest<IReadOnlyList<ChatMessage>> pending)) {
                return;
            }

            _pendingFetches.Remove(requestId);
            pending.TryCancel();
        }

        private void CancelPendingConnect() {
            PendingRequest<bool> pending = _pendingConnect;
            _pendingConnect = null;
            pending?.TryCancel();
        }

        private void SendFrame(int length) {
            if (length < 1) {
                return;
            }

            _transport.Send(_sendBuffer, 0, length);
        }

        private uint NextNonce() {
            _nextNonce++;
            return _nextNonce;
        }

        /// <summary>Request ids start at one: zero is reserved for the server's unsolicited history push.</summary>
        private uint NextRequestId() {
            _nextRequestId++;
            return _nextRequestId;
        }

        private void SetState(ChatProviderState state) {
            if (_state == state) {
                return;
            }

            _state = state;
            StateChanged?.Invoke(state);
        }

        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(LiteNetChatProvider));
            }
        }

        /// <summary>
        /// One request waiting on the server, with the cancellation hookup that removes it from the
        /// provider's table if the caller gives up first.
        /// </summary>
        private sealed class PendingRequest<TResult> {
            private readonly TaskCompletionSource<TResult> _completion =
                new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            private CancellationTokenRegistration _registration;

            public PendingRequest(CancellationToken cancellationToken, Action onCancelled) {
                if (cancellationToken.CanBeCanceled) {
                    _registration = cancellationToken.Register(onCancelled);
                }
            }

            /// <summary>The task handed back to the caller.</summary>
            public Task<TResult> Task => _completion.Task;

            /// <summary>Settles the request with the server's answer.</summary>
            public bool TryComplete(TResult result) {
                _registration.Dispose();
                return _completion.TrySetResult(result);
            }

            /// <summary>Settles the request as cancelled by its caller.</summary>
            public bool TryCancel() {
                _registration.Dispose();
                return _completion.TrySetCanceled();
            }

            /// <summary>Settles the request as failed.</summary>
            public bool TryFail(Exception error) {
                _registration.Dispose();
                return _completion.TrySetException(error);
            }
        }
    }
}
