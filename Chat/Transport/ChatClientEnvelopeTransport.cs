using System;
using AlpineLib.Chat.Wire;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Chat.Transport {
    /// <summary>
    /// Carries chat frames over the game connection a client already has, as the opaque body of
    /// envelope <see cref="ChatMessageIds.ChatPayload"/> on the reliable chat channel.
    /// </summary>
    /// <remarks>
    /// Chat therefore needs no socket, no port and no second connection, and the netcode layer never
    /// learns what chat is saying — it splits the envelope id off and hands the rest here untouched.
    /// <para>
    /// Wiring is explicit rather than done in the constructor: <see cref="NetClient.RegisterRaw"/>
    /// refuses a second claim on an id, so an adapter that registered itself on construction would make
    /// building a replacement adapter throw instead of simply taking over.
    /// </para>
    /// </remarks>
    public sealed class ChatClientEnvelopeTransport : IChatTransport, IDisposable {
        private readonly NetClient _client;

        private bool _isStarted;
        private bool _disposed;

        /// <summary>Wraps a client connection. Nothing is wired up until <see cref="Start"/>.</summary>
        public ChatClientEnvelopeTransport(NetClient client) {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <inheritdoc />
        public event Action Connected;

        /// <inheritdoc />
        public event Action Disconnected;

        /// <inheritdoc />
        public event Action<ArraySegment<byte>> PayloadReceived;

        /// <inheritdoc />
        public bool IsConnected => !_disposed && _client.IsConnected;

        /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
        public bool IsStarted => _isStarted;

        /// <summary>Claims the chat envelope id and begins reporting connection changes.</summary>
        public void Start() {
            ThrowIfDisposed();

            if (_isStarted) {
                return;
            }

            _isStarted = true;
            _client.RegisterRaw(ChatMessageIds.ChatPayload, HandleEnvelope);
            _client.OnConnected += HandleClientConnected;
            _client.OnDisconnected += HandleClientDisconnected;
        }

        /// <summary>Releases the chat envelope id. The adapter can be started again afterwards.</summary>
        public void Stop() {
            if (!_isStarted) {
                return;
            }

            _isStarted = false;
            _client.UnregisterRaw(ChatMessageIds.ChatPayload);
            _client.OnConnected -= HandleClientConnected;
            _client.OnDisconnected -= HandleClientDisconnected;
        }

        /// <inheritdoc />
        public void Send(byte[] payload, int offset, int length) {
            if (payload == null || length < 1 || !IsConnected) {
                return;
            }

            _client.SendRaw(
                ChatMessageIds.ChatPayload,
                new ReadOnlySpan<byte>(payload, offset, length),
                DeliveryClass.ReliableChat);
        }

        /// <inheritdoc />
        public void Dispose() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            Stop();

            Connected = null;
            Disconnected = null;
            PayloadReceived = null;
        }

        private void HandleClientConnected() {
            Connected?.Invoke();
        }

        private void HandleClientDisconnected(DisconnectReason reason) {
            Disconnected?.Invoke();
        }

        private void HandleEnvelope(ushort envelopeId, ArraySegment<byte> payload, PeerHandle sender) {
            PayloadReceived?.Invoke(payload);
        }

        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(ChatClientEnvelopeTransport));
            }
        }
    }
}
