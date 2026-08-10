using System;
using AlpineLib.Chat.Wire;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Chat.Transport {
    /// <summary>
    /// Carries chat frames over the game connections a server already has, translating between the
    /// transport's peer handles and the player identities chat addresses.
    /// </summary>
    /// <remarks>
    /// The translation is supplied by the session layer as two delegates rather than by a map this class
    /// owns, because the session is the only thing that knows who a connection belongs to — and it
    /// already tracks exactly that for the roster. Doing it here is what lets a player keep their chat
    /// identity across a reconnect: the handle changes, the id does not.
    /// <para>
    /// A process hosting several sessions has one socket and one <see cref="NetServer"/> but one chat
    /// service per session, so the envelope id cannot be claimed by each of them. Such a host claims the
    /// id itself, resolves which session a frame belongs to, and pumps it in through
    /// <see cref="Receive"/> without ever calling <see cref="Start"/>.
    /// </para>
    /// </remarks>
    public sealed class ChatServerEnvelopeTransport : IChatServerTransport, IDisposable {
        private readonly NetServer _server;
        private readonly Func<PeerHandle, PlayerId> _playerForPeer;
        private readonly Func<PlayerId, PeerHandle> _peerForPlayer;

        private bool _isStarted;
        private bool _disposed;

        /// <summary>Wraps a server. Nothing is wired up until <see cref="Start"/>.</summary>
        /// <param name="server">The facade whose connections chat rides on.</param>
        /// <param name="playerForPeer">
        /// Session-supplied lookup from a connection to the player on it, returning
        /// <see cref="PlayerId.None"/> for a connection that has not authenticated.
        /// </param>
        /// <param name="peerForPlayer">
        /// Session-supplied lookup from a player to their current connection, returning
        /// <see cref="PeerHandle.None"/> for a player who is not connected right now.
        /// </param>
        public ChatServerEnvelopeTransport(
            NetServer server,
            Func<PeerHandle, PlayerId> playerForPeer,
            Func<PlayerId, PeerHandle> peerForPlayer) {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _playerForPeer = playerForPeer ?? throw new ArgumentNullException(nameof(playerForPeer));
            _peerForPlayer = peerForPlayer ?? throw new ArgumentNullException(nameof(peerForPlayer));
        }

        /// <inheritdoc />
        public event Action<PlayerId, ArraySegment<byte>> PayloadReceived;

        /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
        public bool IsStarted => _isStarted;

        /// <summary>Claims the chat envelope id on the server facade.</summary>
        public void Start() {
            ThrowIfDisposed();

            if (_isStarted) {
                return;
            }

            _isStarted = true;
            _server.RegisterRaw(ChatMessageIds.ChatPayload, HandleEnvelope);
        }

        /// <summary>Releases the chat envelope id. The adapter can be started again afterwards.</summary>
        public void Stop() {
            if (!_isStarted) {
                return;
            }

            _isStarted = false;
            _server.UnregisterRaw(ChatMessageIds.ChatPayload);
        }

        /// <summary>
        /// Reports a frame that arrived on a connection, for a host that routes the chat envelope itself
        /// instead of letting this adapter claim it.
        /// </summary>
        public void Receive(PeerHandle sender, ArraySegment<byte> payload) {
            PlayerId player = _playerForPeer(sender);

            if (!player.IsValid) {
                return;
            }

            PayloadReceived?.Invoke(player, payload);
        }

        /// <inheritdoc />
        public void SendTo(PlayerId player, byte[] payload, int offset, int length) {
            if (payload == null || length < 1 || _disposed) {
                return;
            }

            PeerHandle peer = _peerForPlayer(player);

            if (!peer.IsValid) {
                return;
            }

            _server.SendRaw(
                peer,
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
            PayloadReceived = null;
        }

        private void HandleEnvelope(ushort envelopeId, ArraySegment<byte> payload, PeerHandle sender) {
            Receive(sender, payload);
        }

        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(ChatServerEnvelopeTransport));
            }
        }
    }
}
