using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Chat.Transport;
using AlpineLib.DI;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Networking;
using AlpineLib.Sessions;
using UnityEngine;

namespace AlpineLib.Chat {
    /// <summary>
    /// Chat as the game sees it: a line to send, a list of recent lines to show, and an event for each
    /// one that arrives.
    /// </summary>
    /// <remarks>
    /// The provider underneath is deliberately invisible from here. Today it is the built-in one riding
    /// the game connection; replacing it with a hosted service changes what this service composes and
    /// nothing else, which is the whole reason <see cref="IChatProvider"/> exists.
    /// </remarks>
    public interface IChatService : IDependencyProvider {
        /// <summary>Where chat stands with its backend right now.</summary>
        ChatProviderState State { get; }

        /// <summary>True while lines can actually be sent.</summary>
        bool IsConnected { get; }

        /// <summary>The session's room channel, or an invalid channel while there is no session.</summary>
        ChatChannelId RoomChannel { get; }

        /// <summary>The most recent lines, oldest first, capped at the configured client buffer size.</summary>
        IReadOnlyList<ChatMessage> GetRecentMessages();

        /// <summary>Raised for every line delivered to a channel the local player is in.</summary>
        event Action<ChatMessage> OnMessageReceived;

        /// <summary>Raised whenever <see cref="State"/> changes.</summary>
        event Action<ChatProviderState> OnStateChanged;

        /// <summary>Installs the authored tuning. Null leaves chat inert.</summary>
        void Configure(ChatConfig config);

        /// <summary>Sends a line to the session's room channel.</summary>
        Task<ChatSendResult> SendAsync(string text);

        /// <summary>Sends a line to an explicit channel.</summary>
        Task<ChatSendResult> SendAsync(ChatChannelId channel, string text);
    }

    /// <summary>
    /// App-root resident implementation of <see cref="IChatService"/>, composing the built-in provider
    /// over the game connection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Chat frames travel as opaque payloads inside an envelope message on the game connection's
    /// reliable chat channel, so chat needs no socket, no port and no second connection of its own. That
    /// is what the envelope transport does, and why this service can be brought up and torn down purely
    /// as a consequence of the session's own lifecycle.
    /// </para>
    /// <para>
    /// Both collaborators are resolved through <see cref="Injector.TryResolve{T}"/> rather than
    /// injected: a game may install chat and no networking, or open a scene with neither, and in both
    /// cases this service must degrade to a no-op instead of refusing to wake up.
    /// </para>
    /// </remarks>
    public class ChatService : MonoBehaviour, IChatService {
        /// <inheritdoc />
        public ChatProviderState State => _provider?.State ?? ChatProviderState.Disconnected;

        /// <inheritdoc />
        public bool IsConnected => State == ChatProviderState.Connected;

        /// <inheritdoc />
        public ChatChannelId RoomChannel => _provider?.RoomChannel ?? default;

        /// <inheritdoc />
        public event Action<ChatMessage> OnMessageReceived;

        /// <inheritdoc />
        public event Action<ChatProviderState> OnStateChanged;

        private readonly List<ChatMessage> _recentMessages = new List<ChatMessage>();

        private INetworkService _networkService;
        private ISessionService _sessionService;
        private ChatConfig _config;
        private ChatSettings _settings;
        private ChatClientEnvelopeTransport _transport;
        private LiteNetChatProvider _provider;

        /// <remarks>
        /// Declared on the concrete type rather than the interface, matching the library's other
        /// services: the injector reflects over the concrete type when registering a provider.
        /// </remarks>
        [Provide]
        public IChatService ProvideChatService() {
            return this;
        }

        /// <inheritdoc />
        public void Configure(ChatConfig config) {
            _config = config;
            _settings = config != null ? config.ToSettings() : new ChatSettings();
        }

        /// <inheritdoc />
        public IReadOnlyList<ChatMessage> GetRecentMessages() {
            return _recentMessages;
        }

        /// <inheritdoc />
        public Task<ChatSendResult> SendAsync(string text) {
            return SendAsync(RoomChannel, text);
        }

        /// <inheritdoc />
        public Task<ChatSendResult> SendAsync(ChatChannelId channel, string text) {
            if (_provider == null || !IsConnected) {
                return Task.FromResult(ChatSendResult.Rejected(ChatSendStatus.NotConnected));
            }

            if (string.IsNullOrWhiteSpace(text)) {
                return Task.FromResult(ChatSendResult.Rejected(ChatSendStatus.Empty));
            }

            return _provider.SendAsync(channel, text, CancellationToken.None);
        }

        private void Awake() {
            // Application-shutdown guard, not a race guard: this service is installed on the app root,
            // so an absent injector means the game is already tearing down.
            if (!Injector.HasInstance) return;

            Injector.Instance.RegisterProvider(this);
        }

        private void Start() {
            if (!Injector.HasInstance) return;

            Injector.Instance.TryResolve(out _networkService);

            if (!Injector.Instance.TryResolve(out _sessionService)) return;

            _sessionService.OnStateChanged += HandleSessionStateChanged;
        }

        private void OnDestroy() {
            if (_sessionService != null) {
                _sessionService.OnStateChanged -= HandleSessionStateChanged;
            }

            TearDownProvider();

            if (!Injector.HasInstance) return;

            Injector.Instance.UnregisterProvider(this);
        }

        /// <summary>
        /// Brings chat up when the session is attached and takes it down again when it is not, so chat
        /// exists exactly as long as the room it belongs to.
        /// </summary>
        private void HandleSessionStateChanged(ClientSessionState state) {
            if (state == ClientSessionState.InSession) {
                _ = ConnectAsync();
                return;
            }

            TearDownProvider();
        }

        /// <summary>
        /// Composes the transport and provider over the live connection and joins the session's room.
        /// </summary>
        private async Task ConnectAsync() {
            if (_provider != null) return;

            NetClient client = _networkService?.Client;

            if (client == null) {
                Debug.LogWarning("ChatService::ConnectAsync->No client connection; chat stays offline.");
                return;
            }

            _settings ??= new ChatSettings();
            _transport = new ChatClientEnvelopeTransport(client);
            _transport.Start();

            _provider = new LiteNetChatProvider(_transport, _settings);
            _provider.MessageReceived += HandleMessageReceived;
            _provider.StateChanged += HandleProviderStateChanged;

            _recentMessages.Clear();

            await _provider.ConnectAsync(BuildIdentity(), CancellationToken.None);
        }

        /// <summary>
        /// Who the local player is in chat, and which room they belong to.
        /// </summary>
        /// <remarks>
        /// The room key is the session id rather than the join code: a code is a human-facing selector
        /// that a session may outlive, while the id is what the server scopes its broadcasts by.
        /// </remarks>
        private ChatIdentity BuildIdentity() {
            PlayerIdentity identity = _sessionService?.Identity;
            PlayerId playerId = identity?.PlayerId ?? PlayerId.None;
            string displayName = identity?.DisplayName ?? string.Empty;

            return new ChatIdentity(playerId, displayName, ResolveRoomKey());
        }

        private string ResolveRoomKey() {
            return _sessionService?.SessionId ?? string.Empty;
        }

        private void TearDownProvider() {
            if (_provider != null) {
                _provider.MessageReceived -= HandleMessageReceived;
                _provider.StateChanged -= HandleProviderStateChanged;
                _provider.Dispose();
                _provider = null;
            }

            if (_transport == null) return;

            _transport.Dispose();
            _transport = null;
        }

        /// <summary>
        /// Records a line in the client view and passes it on, dropping the oldest once the buffer is
        /// full.
        /// </summary>
        private void HandleMessageReceived(ChatMessage message) {
            _recentMessages.Add(message);

            int capacity = ResolveViewCapacity();

            while (_recentMessages.Count > capacity) {
                _recentMessages.RemoveAt(0);
            }

            OnMessageReceived?.Invoke(message);
        }

        private void HandleProviderStateChanged(ChatProviderState state) {
            OnStateChanged?.Invoke(state);
        }

        private int ResolveViewCapacity() {
            if (_settings == null || _settings.ClientViewBufferSize <= 0) return 1;

            return _settings.ClientViewBufferSize;
        }
    }
}
