using System;
using System.Collections.Generic;
using System.Text;
using AlpineLib.Chat.Wire;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Chat {
    /// <summary>
    /// The authoritative half of chat: every line a player sends passes through this object's filter
    /// chain, gets an id from the channel it landed in, and is fanned out to that channel's subscribers.
    /// </summary>
    /// <remarks>
    /// One instance serves one session. That is what makes room chat trivially session-scoped — the
    /// service only ever knows about the channels its own members are in, so a broadcast cannot leak
    /// across sessions even if two of them pick the same room key.
    /// <para>
    /// <b>Threading.</b> Everything happens on the thread that pumps the transport: the Unity main
    /// thread on a listen host, the fixed-step loop on the dedicated server. Filters are synchronous by
    /// contract and the moderation sink is expected to return promptly, so nothing here ever leaves the
    /// tick.
    /// </para>
    /// <para>
    /// <b>Disconnected members.</b> The service deliberately does not subscribe to
    /// <see cref="IChatServerHost.PlayerDisconnected"/>. A member whose link dropped while their roster
    /// slot is held for a rejoin keeps their subscription and their place in history; delivery is gated
    /// on <see cref="IChatServerHost.IsOnline"/> at send time, so they simply receive nothing until they
    /// are back, and the history push they get on rejoin fills the gap.
    /// </para>
    /// </remarks>
    public sealed class ChatServerService {
        /// <summary>
        /// Bytes one frame may carry, once the envelope id is accounted for.
        /// </summary>
        /// <remarks>
        /// The netcode send path serialises into a pooled buffer sized to fit an unfragmented datagram,
        /// so a chat frame larger than this cannot be sent at all — the writer overflows. Chat therefore
        /// measures every frame before it goes out: a history page is trimmed to fit, and a line whose
        /// encoded form would not fit is refused as too long rather than accepted, given an id and then
        /// silently lost on the way out.
        /// </remarks>
        private const int MaxFramePayloadBytes = NetBufferPool.DefaultBufferSize - NetEnvelope.HeaderSize;

        /// <summary>The <see cref="ChatWireMessageType"/> byte every frame starts with.</summary>
        private const int FrameTypeBytes = 1;

        /// <summary>Worst case for a var-uint length or count prefix.</summary>
        private const int VarUIntBytes = 5;

        private readonly IChatServerHost _host;
        private readonly IChatServerTransport _transport;
        private readonly ChatSettings _settings;
        private readonly IChatFilter[] _filters;
        private readonly IChatModerationSink _moderationSink;

        private readonly Dictionary<ChatChannelId, ChatChannelState> _channels =
            new Dictionary<ChatChannelId, ChatChannelState>();

        private readonly Dictionary<PlayerId, long> _mutedUntilUnixMs = new Dictionary<PlayerId, long>();
        private readonly Dictionary<PlayerId, int> _violationCounts = new Dictionary<PlayerId, int>();
        private readonly List<ChatMessage> _historyScratch = new List<ChatMessage>();
        private readonly List<ChatMessage> _pageScratch = new List<ChatMessage>();
        private readonly byte[] _sendBuffer = ChatWireCodec.CreateBuffer();

        private bool _isRunning;

        /// <summary>Creates a service using the standard filter chain built from the settings.</summary>
        public ChatServerService(IChatServerHost host, IChatServerTransport transport, ChatSettings settings)
            : this(host, transport, settings, null, null) { }

        /// <summary>Creates a service with an explicit filter chain and moderation sink.</summary>
        /// <param name="host">The session the chat belongs to.</param>
        /// <param name="transport">The pipe frames travel on, addressed by player.</param>
        /// <param name="settings">Policy: lengths, rates, history sizes, mute durations.</param>
        /// <param name="filters">
        /// The chain, run in order, first refusal wins. Null installs
        /// <see cref="ChatSettings.BuildDefaultFilters"/>.
        /// </param>
        /// <param name="moderationSink">Where rulings are reported. Null discards them.</param>
        public ChatServerService(
            IChatServerHost host,
            IChatServerTransport transport,
            ChatSettings settings,
            IReadOnlyList<IChatFilter> filters,
            IChatModerationSink moderationSink) {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _filters = CopyFilters(filters, _settings);
            _moderationSink = moderationSink;
        }

        /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
        public bool IsRunning => _isRunning;

        /// <summary>The channel every member is subscribed to for server-authored notices.</summary>
        public ChatChannelId SystemChannel => ChatChannelId.System();

        /// <summary>Subscribes to the host and the transport. Safe to call when already running.</summary>
        public void Start() {
            if (_isRunning) {
                return;
            }

            _isRunning = true;
            _transport.PayloadReceived += HandlePayload;
            _host.PlayerJoinedRoom += HandlePlayerJoinedRoom;
            _host.PlayerLeftRoom += HandlePlayerLeftRoom;
        }

        /// <summary>Unsubscribes and drops every channel, subscription and moderation record.</summary>
        public void Stop() {
            if (!_isRunning) {
                return;
            }

            _isRunning = false;
            _transport.PayloadReceived -= HandlePayload;
            _host.PlayerJoinedRoom -= HandlePlayerJoinedRoom;
            _host.PlayerLeftRoom -= HandlePlayerLeftRoom;

            _channels.Clear();
            _mutedUntilUnixMs.Clear();
            _violationCounts.Clear();
        }

        /// <summary>
        /// Decodes one frame from a player and acts on it. Wired to the transport by
        /// <see cref="Start"/>, and public so a host that routes its own envelopes can pump frames in
        /// directly.
        /// </summary>
        public void HandlePayload(PlayerId sender, ArraySegment<byte> payload) {
            if (payload.Array == null || payload.Count < 1) {
                return;
            }

            try {
                DispatchPayload(sender, payload);
            }
            catch (NetProtocolException) {
                // A frame that will not decode is a peer-scoped fault: one malformed client must never
                // take the session's tick down with it, and there is nothing to tell the sender because
                // the nonce that would have identified their request is exactly what failed to parse.
            }
        }

        /// <summary>Silences a player for a while. Replaces any shorter mute already in force.</summary>
        public void Mute(PlayerId player, TimeSpan duration) {
            MuteInternal(player, duration, "Muted by an operator.");
        }

        /// <summary>Lifts a mute immediately.</summary>
        public void Unmute(PlayerId player) {
            _mutedUntilUnixMs.Remove(player);
            _violationCounts.Remove(player);
        }

        /// <summary>True when the player is silenced as of the host's current clock.</summary>
        public bool IsMuted(PlayerId player) {
            return RemainingMuteMs(player, _host.ServerTimeUnixMs) > 0;
        }

        /// <summary>
        /// Says something as the server on a channel: a match countdown, a rule reminder, a moderation
        /// notice. Takes the next id in that channel's sequence exactly as a player line would, so
        /// system lines interleave with player lines in one order everybody agrees on.
        /// </summary>
        public void PostSystemMessage(ChatChannelId channel, string text) {
            if (string.IsNullOrEmpty(text)) {
                return;
            }

            ChatChannelState state = GetOrCreateChannel(channel);
            ChatMessage message = ChatMessage.FromSystem(0uL, channel, text, _host.ServerTimeUnixMs);

            if (!FitsOneFrame(message)) {
                return;
            }

            message.MessageId = state.NextMessageId();
            state.Append(message);
            BroadcastMessage(state, message);
            _moderationSink?.OnMessageDelivered(message);
        }

        /// <summary>The channel's server-side state, or null when nobody has ever used it.</summary>
        public ChatChannelState FindChannel(ChatChannelId channel) {
            return _channels.TryGetValue(channel, out ChatChannelState state) ? state : null;
        }

        private static IChatFilter[] CopyFilters(IReadOnlyList<IChatFilter> filters, ChatSettings settings) {
            if (filters == null) {
                return settings.BuildDefaultFilters();
            }

            IChatFilter[] copy = new IChatFilter[filters.Count];

            for (int filterIndex = 0; filterIndex < filters.Count; filterIndex++) {
                copy[filterIndex] = filters[filterIndex];
            }

            return copy;
        }

        private void DispatchPayload(PlayerId sender, ArraySegment<byte> payload) {
            NetReader reader = ChatWireCodec.OpenPayload(payload, out ChatWireMessageType messageType);

            if (messageType == ChatWireMessageType.SendRequest) {
                HandleSendRequest(sender, ChatWireCodec.ReadSendRequest(ref reader));
                return;
            }

            if (messageType == ChatWireMessageType.HistoryRequest) {
                HandleHistoryRequest(sender, ChatWireCodec.ReadHistoryRequest(ref reader));
            }

            // Every other frame type is server to client. A client that sends one is either broken or
            // probing; either way there is nothing useful to answer with.
        }

        private void HandleSendRequest(PlayerId sender, ChatSendRequest request) {
            ChatSendResult result = EvaluateSend(sender, request);
            SendAck(sender, new ChatSendAck(request.Nonce, result));
        }

        private ChatSendResult EvaluateSend(PlayerId sender, ChatSendRequest request) {
            ChatChannelState state = FindChannel(request.Channel);

            if (state == null || !state.IsSubscribed(sender)) {
                return ChatSendResult.Rejected(ChatSendStatus.UnknownChannel);
            }

            if (request.Channel.Kind == ChatChannelKind.System) {
                // The system channel carries the server's voice. Accepting a client line on it would let
                // any player forge a server notice, so it is refused at the door rather than filtered.
                return ChatSendResult.Rejected(ChatSendStatus.UnknownChannel);
            }

            long nowUnixMs = _host.ServerTimeUnixMs;
            int remainingMuteMs = RemainingMuteMs(sender, nowUnixMs);

            if (remainingMuteMs > 0) {
                return ChatSendResult.Throttled(ChatSendStatus.Muted, remainingMuteMs);
            }

            return RunPipeline(sender, state, request.Text, nowUnixMs);
        }

        private ChatSendResult RunPipeline(
            PlayerId sender,
            ChatChannelState state,
            string text,
            long nowUnixMs) {
            var context = new ChatFilterContext(
                sender,
                _host.GetDisplayName(sender),
                state.Channel,
                text,
                nowUnixMs);

            if (!TryRunFilters(context, out string acceptedText, out ChatFilterResult rejection)) {
                return RejectSend(sender, state.Channel, text, rejection, nowUnixMs);
            }

            var message = new ChatMessage(
                0uL,
                state.Channel,
                sender,
                _host.GetDisplayName(sender),
                acceptedText,
                nowUnixMs,
                ChatMessageKind.Player);

            if (!FitsOneFrame(message)) {
                // Refused before an id is spent: a line nobody could have received must not leave a gap
                // in the channel's sequence, which is the very thing clients merge history against.
                return ChatSendResult.Rejected(ChatSendStatus.TooLong);
            }

            message.MessageId = state.NextMessageId();
            state.Append(message);
            BroadcastMessage(state, message);
            _moderationSink?.OnMessageDelivered(message);

            return ChatSendResult.Accepted(message.MessageId);
        }

        private bool TryRunFilters(
            ChatFilterContext context,
            out string acceptedText,
            out ChatFilterResult rejection) {
            for (int filterIndex = 0; filterIndex < _filters.Length; filterIndex++) {
                ChatFilterResult result = _filters[filterIndex].Evaluate(in context);

                if (!result.IsAllowed) {
                    acceptedText = null;
                    rejection = result;
                    return false;
                }

                context = result.HasReplacement ? context.WithText(result.ReplacementText) : context;
            }

            acceptedText = context.Text;
            rejection = ChatFilterResult.Allow();
            return true;
        }

        private ChatSendResult RejectSend(
            PlayerId sender,
            ChatChannelId channel,
            string text,
            ChatFilterResult rejection,
            long nowUnixMs) {
            _moderationSink?.OnMessageRejected(sender, channel, text, rejection);

            if (!rejection.IsViolation || !TallyViolation(sender)) {
                return new ChatSendResult(rejection.Status, 0uL, rejection.RetryAfterMs);
            }

            MuteInternal(sender, _settings.MuteDuration, "Reached the violation threshold.");

            return ChatSendResult.Throttled(ChatSendStatus.Muted, RemainingMuteMs(sender, nowUnixMs));
        }

        /// <summary>Counts one violation against a player and reports whether it earned a mute.</summary>
        private bool TallyViolation(PlayerId player) {
            if (!_settings.AutoMuteEnabled) {
                return false;
            }

            _violationCounts.TryGetValue(player, out int previous);
            int tally = previous + 1;

            if (tally < _settings.MuteAfterViolations) {
                _violationCounts[player] = tally;
                return false;
            }

            _violationCounts.Remove(player);
            return true;
        }

        private void MuteInternal(PlayerId player, TimeSpan duration, string reason) {
            long nowUnixMs = _host.ServerTimeUnixMs;
            long untilUnixMs = nowUnixMs + (long)duration.TotalMilliseconds;

            if (_mutedUntilUnixMs.TryGetValue(player, out long existing) && existing > untilUnixMs) {
                return;
            }

            _mutedUntilUnixMs[player] = untilUnixMs;
            _moderationSink?.OnPlayerMuted(player, untilUnixMs, reason);
        }

        private int RemainingMuteMs(PlayerId player, long nowUnixMs) {
            if (!_mutedUntilUnixMs.TryGetValue(player, out long untilUnixMs)) {
                return 0;
            }

            if (untilUnixMs <= nowUnixMs) {
                _mutedUntilUnixMs.Remove(player);
                return 0;
            }

            long remaining = untilUnixMs - nowUnixMs;
            return remaining > int.MaxValue ? int.MaxValue : (int)remaining;
        }

        private void HandleHistoryRequest(PlayerId sender, ChatHistoryRequest request) {
            ChatChannelState state = FindChannel(request.Channel);

            if (state == null || !state.IsSubscribed(sender)) {
                return;
            }

            _historyScratch.Clear();
            state.CopyBefore(request.BeforeMessageId, ClampPageCount(request.Count), _historyScratch);
            TrimToFrameBudget(_historyScratch, request.Channel);

            // Answered even when the page came back empty: the client is holding a pending fetch open
            // against this request id, and only a response closes it.
            var response = new ChatHistoryResponse(request.RequestId, request.Channel, _historyScratch);
            SendFrame(sender, ChatWireCodec.Write(_sendBuffer, in response));
        }

        private void HandlePlayerJoinedRoom(PlayerId player, string roomKey) {
            ChatChannelState room = GetOrCreateChannel(ChatChannelId.Room(roomKey));
            ChatChannelState system = GetOrCreateChannel(SystemChannel);
            bool isNewMember = room.Subscribe(player);

            system.Subscribe(player);

            // History first, then the join notice: a client that has been handed the backlog can place
            // the notice at the end of it, whereas the reverse order makes the newcomer's own arrival
            // appear before the conversation they arrived into.
            PushHistory(player, room);
            PushHistory(player, system);

            SendChannelEvent(player, room.Channel, ChatChannelChange.Joined, player);

            if (!isNewMember) {
                return;
            }

            BroadcastChannelEvent(room, ChatChannelChange.MemberJoined, player);
        }

        private void HandlePlayerLeftRoom(PlayerId player, string roomKey) {
            ChatChannelState room = FindChannel(ChatChannelId.Room(roomKey));

            if (room == null || !room.Unsubscribe(player)) {
                return;
            }

            FindChannel(SystemChannel)?.Unsubscribe(player);
            SendChannelEvent(player, room.Channel, ChatChannelChange.Left, player);
            BroadcastChannelEvent(room, ChatChannelChange.MemberLeft, player);
            ForgetPlayer(player);
        }

        /// <summary>Drops every scrap of per-player state a departing member leaves behind.</summary>
        private void ForgetPlayer(PlayerId player) {
            for (int filterIndex = 0; filterIndex < _filters.Length; filterIndex++) {
                _filters[filterIndex].Forget(player);
            }

            _violationCounts.Remove(player);
            _mutedUntilUnixMs.Remove(player);
        }

        private void PushHistory(PlayerId player, ChatChannelState state) {
            if (state.HistoryCount < 1 || _settings.HistoryOnJoinCount < 1) {
                return;
            }

            _historyScratch.Clear();
            state.CopyRecent(ClampPageCount(_settings.HistoryOnJoinCount), _historyScratch);

            // A configured backlog of a few dozen lines is far more than one datagram holds, so the push
            // goes out as however many frames it takes, oldest first. The client merges each of them
            // against what it has already seen, so nothing depends on the split landing anywhere in
            // particular — which is exactly why the backlog can be split at all.
            int pageStart = 0;

            while (pageStart < _historyScratch.Count) {
                int pageCount = MeasurePage(_historyScratch, pageStart, state.Channel);
                SendHistoryPage(player, state.Channel, pageStart, pageCount);
                pageStart += pageCount;
            }
        }

        /// <summary>Sends one slice of the backlog as an unsolicited page.</summary>
        /// <remarks>
        /// Request id zero marks a page as volunteered rather than asked for, so the client merges it
        /// into the live stream instead of resolving it against a pending fetch.
        /// </remarks>
        private void SendHistoryPage(PlayerId player, ChatChannelId channel, int start, int count) {
            _pageScratch.Clear();

            for (int offset = 0; offset < count; offset++) {
                _pageScratch.Add(_historyScratch[start + offset]);
            }

            var response = new ChatHistoryResponse(0u, channel, _pageScratch);
            SendFrame(player, ChatWireCodec.Write(_sendBuffer, in response));
        }

        /// <summary>
        /// How many messages from <paramref name="start"/> fit in one frame. Always at least one, so a
        /// caller walking a backlog cannot stall.
        /// </summary>
        private static int MeasurePage(List<ChatMessage> messages, int start, ChatChannelId channel) {
            int remainingBudget = MaxFramePayloadBytes
                - FrameTypeBytes
                - VarUIntBytes
                - VarUIntBytes
                - EstimateChannelBytes(channel);

            int count = 0;

            for (int messageIndex = start; messageIndex < messages.Count; messageIndex++) {
                int cost = EstimateSerializedBytes(messages[messageIndex]);

                if (count > 0 && cost > remainingBudget) {
                    break;
                }

                remainingBudget -= cost;
                count++;
            }

            return count;
        }

        /// <summary>Holds a page to the largest the wire format admits.</summary>
        private static int ClampPageCount(int count) {
            if (count < 0) {
                return 0;
            }

            return count > ChatHistoryRequest.MaxCount ? ChatHistoryRequest.MaxCount : count;
        }

        /// <summary>
        /// Drops the oldest lines from a page until what remains fits one frame. A page is always the
        /// newest lines below some boundary, so trimming from the front keeps the ones the reader is
        /// actually about to look at.
        /// </summary>
        private static void TrimToFrameBudget(List<ChatMessage> page, ChatChannelId channel) {
            int remainingBudget = MaxFramePayloadBytes
                - FrameTypeBytes
                - VarUIntBytes
                - VarUIntBytes
                - EstimateChannelBytes(channel);

            int firstThatFits = page.Count;

            for (int messageIndex = page.Count - 1; messageIndex >= 0; messageIndex--) {
                int cost = EstimateSerializedBytes(page[messageIndex]);

                if (cost > remainingBudget) {
                    break;
                }

                remainingBudget -= cost;
                firstThatFits = messageIndex;
            }

            if (firstThatFits > 0) {
                page.RemoveRange(0, firstThatFits);
            }
        }

        /// <summary>True when the message can be broadcast in a single datagram.</summary>
        private static bool FitsOneFrame(ChatMessage message) {
            return FrameTypeBytes + EstimateSerializedBytes(message) <= MaxFramePayloadBytes;
        }

        /// <summary>Upper bound on the bytes one message costs on the wire, never an underestimate.</summary>
        private static int EstimateSerializedBytes(ChatMessage message) {
            // Message id, sender id, timestamp, message kind byte.
            const int FixedBytes = 8 + PlayerId.SerializedByteCount + 8 + 1;

            if (message == null) {
                return FixedBytes;
            }

            return FixedBytes
                + EstimateChannelBytes(message.Channel)
                + VarUIntBytes + Encoding.UTF8.GetByteCount(message.SenderDisplayName ?? string.Empty)
                + VarUIntBytes + Encoding.UTF8.GetByteCount(message.Text ?? string.Empty);
        }

        /// <summary>Upper bound on the bytes a channel id costs on the wire.</summary>
        private static int EstimateChannelBytes(ChatChannelId channel) {
            const int KindBytes = 1;
            return KindBytes + VarUIntBytes + Encoding.UTF8.GetByteCount(channel.Key);
        }

        private ChatChannelState GetOrCreateChannel(ChatChannelId channel) {
            if (_channels.TryGetValue(channel, out ChatChannelState existing)) {
                return existing;
            }

            var created = new ChatChannelState(channel, _settings.HistoryBufferSize);
            _channels.Add(channel, created);
            return created;
        }

        private void BroadcastMessage(ChatChannelState state, ChatMessage message) {
            var broadcast = new ChatBroadcast(message);
            int length = ChatWireCodec.Write(_sendBuffer, in broadcast);
            IReadOnlyList<PlayerId> subscribers = state.Subscribers;

            for (int subscriberIndex = 0; subscriberIndex < subscribers.Count; subscriberIndex++) {
                SendFrame(subscribers[subscriberIndex], length);
            }
        }

        private void BroadcastChannelEvent(ChatChannelState state, ChatChannelChange change, PlayerId subject) {
            var channelEvent = new ChatChannelEvent(
                state.Channel,
                change,
                subject,
                _host.GetDisplayName(subject));

            int length = ChatWireCodec.Write(_sendBuffer, in channelEvent);
            IReadOnlyList<PlayerId> subscribers = state.Subscribers;

            for (int subscriberIndex = 0; subscriberIndex < subscribers.Count; subscriberIndex++) {
                PlayerId recipient = subscribers[subscriberIndex];

                if (recipient != subject) {
                    SendFrame(recipient, length);
                }
            }
        }

        private void SendChannelEvent(
            PlayerId recipient,
            ChatChannelId channel,
            ChatChannelChange change,
            PlayerId subject) {
            var channelEvent = new ChatChannelEvent(channel, change, subject, _host.GetDisplayName(subject));
            SendFrame(recipient, ChatWireCodec.Write(_sendBuffer, in channelEvent));
        }

        private void SendAck(PlayerId recipient, ChatSendAck ack) {
            SendFrame(recipient, ChatWireCodec.Write(_sendBuffer, in ack));
        }

        /// <summary>Delivers whatever currently sits in the send buffer, if the player is there to take it.</summary>
        private void SendFrame(PlayerId recipient, int length) {
            if (length < 1 || length > MaxFramePayloadBytes || !_host.IsOnline(recipient)) {
                return;
            }

            _transport.SendTo(recipient, _sendBuffer, 0, length);
        }
    }
}
