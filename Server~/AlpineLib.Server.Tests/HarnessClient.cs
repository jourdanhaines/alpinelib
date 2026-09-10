using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Chat;
using AlpineLib.Chat.Transport;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Claims;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// One player's whole client stack pointed at the harness's server: a socket, a session client, the
    /// replicated world, the claim view and the chat provider, all riding the same connection.
    /// </summary>
    /// <remarks>
    /// This is the shape the Unity services have — the same facades, the same envelope adapters, the same
    /// order of operations — with the engine left out. If a handshake works here it works there, and if
    /// it does not, the failure is in the netcode rather than in a MonoBehaviour.
    /// </remarks>
    internal sealed class HarnessClient : IDisposable {
        private readonly LiteNetTransport _transport;
        private readonly NetClient _netClient;
        private readonly SessionClient _sessionClient;
        private readonly ClientReplication _replication;
        private readonly ClientClaims _claims;
        private readonly ChatClientEnvelopeTransport _chatTransport;
        private readonly LiteNetChatProvider _chatProvider;

        private bool _disposed;

        public HarnessClient(string displayName, PlayerId playerId, NetConfig netConfig, ChatSettings chatSettings) {
            DisplayName = displayName;
            PlayerId = playerId;

            _transport = new LiteNetTransport(netConfig.DisconnectTimeoutMs);
            _netClient = new NetClient(_transport, netConfig);
            _sessionClient = new SessionClient(
                _netClient,
                new AnonymousAuthProvider(),
                new PlayerIdentity(playerId, displayName, AuthMethod.Anonymous));

            _replication = new ClientReplication(_netClient, netConfig);
            _claims = new ClientClaims(_netClient);
            _chatTransport = new ChatClientEnvelopeTransport(_netClient);
            _chatProvider = new LiteNetChatProvider(_chatTransport, chatSettings);
            _chatProvider.MessageReceived += ChatMessages.Add;
            _sessionClient.OnSessionEnded += RecordSessionEnded;
            _sessionClient.OnMemberJoined += AdoptPeerIdOnJoin;
            _sessionClient.OnStateChanged += AdoptPeerIdOnStateChange;
        }

        /// <summary>Name this client authenticates under.</summary>
        public string DisplayName { get; }

        /// <summary>Stable identity, persisted by a real client so a rejoin can reclaim its seat.</summary>
        public PlayerId PlayerId { get; }

        /// <summary>The session half of the stack.</summary>
        public SessionClient Session => _sessionClient;

        /// <summary>The world this client sees, spawns and all.</summary>
        public ClientReplication Replication => _replication;

        /// <summary>Which slots this client believes are taken, and by whom.</summary>
        public ClientClaims Claims => _claims;

        /// <summary>Every chat line the provider raised, in order.</summary>
        public List<ChatMessage> ChatMessages { get; } = new List<ChatMessage>();

        /// <summary>Every reason this client was told its session ended.</summary>
        public List<SessionEndReason> SessionEndings { get; } = new List<SessionEndReason>();

        /// <summary>
        /// The peer id the server knows this client by, or <see cref="SessionMember.NoPeerId"/> before the
        /// roster arrives. Never zero as a sentinel: peer zero is the first connection a socket accepts.
        /// </summary>
        public int LocalPeerId =>
            _sessionClient.LocalMember == null ? SessionMember.NoPeerId : _sessionClient.LocalMember.PeerId;

        /// <summary>True once the roster has told this client which peer it is.</summary>
        public bool HasLocalPeerId => LocalPeerId >= 0;

        /// <summary>One pump of this client's connection and its world.</summary>
        public void Tick(float deltaSeconds) {
            if (_disposed) {
                return;
            }

            _sessionClient.Tick(deltaSeconds);
            _replication.Tick(deltaSeconds);
        }

        /// <summary>The pawn this client owns, or null before the spawn arrives.</summary>
        public NetEntity FindOwnPawn() {
            IReadOnlyList<NetEntity> entities = _replication.Entities;

            for (int entityIndex = 0; entityIndex < entities.Count; entityIndex++) {
                NetEntity candidate = entities[entityIndex];

                if (HasLocalPeerId && candidate.OwnerPeerId == LocalPeerId) {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>Sends a message of the game's own, on the same connection the session rides.</summary>
        public void Send<TMessage>(ushort messageId, in TMessage message)
            where TMessage : struct, INetMessage {
            _netClient.Send(messageId, in message, DeliveryClass.ReliableOrdered);
        }

        /// <summary>Claims the chat envelope and joins the session's room. Call after the session is joined.</summary>
        public Task ConnectChatAsync(string roomKey) {
            _chatTransport.Start();
            return _chatProvider.ConnectAsync(new ChatIdentity(PlayerId, DisplayName, roomKey), CancellationToken.None);
        }

        /// <summary>Says something in a channel.</summary>
        public Task<ChatSendResult> SendChatAsync(ChatChannelId channel, string text) {
            return _chatProvider.SendAsync(channel, text, CancellationToken.None);
        }

        /// <inheritdoc />
        public void Dispose() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            _sessionClient.OnSessionEnded -= RecordSessionEnded;
            _sessionClient.OnMemberJoined -= AdoptPeerIdOnJoin;
            _sessionClient.OnStateChanged -= AdoptPeerIdOnStateChange;
            _claims.Dispose();
            _replication.Dispose();
            _chatProvider.Dispose();
            _chatTransport.Dispose();
            _netClient.Dispose();
            _transport.Dispose();
        }

        private void RecordSessionEnded(SessionEndReason reason, string message) {
            SessionEndings.Add(reason);
        }

        private void AdoptPeerIdOnJoin(SessionMember member, bool isRejoin) {
            AdoptLocalPeerId();
        }

        private void AdoptPeerIdOnStateChange(ClientSessionState state) {
            AdoptLocalPeerId();
        }

        /// <summary>
        /// Tells the world and the claim view which peer this client is, exactly as the Unity session
        /// service does — off the roster, on every event that could have brought it.
        /// </summary>
        private void AdoptLocalPeerId() {
            SessionMember localMember = _sessionClient.LocalMember;

            if (localMember == null) {
                return;
            }

            _replication.LocalPeerId = localMember.PeerId;
            _claims.LocalPeerId = localMember.PeerId;
        }
    }
}
