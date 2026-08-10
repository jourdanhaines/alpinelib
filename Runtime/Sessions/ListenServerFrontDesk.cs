using System;
using System.Collections.Generic;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Messages;
using AlpineLib.Netcode.Transport;
using UnityEngine;

namespace AlpineLib.Sessions {
    /// <summary>
    /// The whole server side of a listen host: an authentication desk, one session, and the routing that
    /// decides which of the two an arriving message belongs to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A dedicated server's front desk hosts many sessions and hands each connection to the right one.
    /// A listen host is the degenerate case — one machine, one igloo, one session — but it still needs a
    /// front desk, because a <see cref="SessionHost"/> deliberately knows nothing about join codes,
    /// session lookup or the one-session-per-connection rule, and something must claim the create and
    /// join message ids on the shared router. This is that something, kept to the single-session case.
    /// </para>
    /// <para>
    /// It also owns the session's <see cref="ServerReplication"/>, because on a listen host the pawn
    /// simulation is server work like any other and has to be ticked from the same pump. The ground
    /// seam it steps over is the engine's collision world, which is the entire reason a listen host
    /// simulates differently from a v1 dedicated server.
    /// </para>
    /// </remarks>
    public class ListenServerFrontDesk : ISessionFrontDesk {
        /// <summary>Session id given to the one session a listen host runs.</summary>
        public const string LocalSessionId = "local";

        private readonly NetServer _server;
        private readonly SessionConfigData _config;
        private readonly NetConfig _netConfig;
        private readonly SessionAuthDesk _authDesk;
        private readonly JoinCodeGenerator _joinCodeGenerator = new JoinCodeGenerator();
        private readonly IGroundProvider _groundProvider;

        private SessionHost _host;
        private ServerReplication _replication;
        private bool _isClosed;

        /// <summary>
        /// Stands up the desk over a running server.
        /// </summary>
        /// <param name="server">The server facade the session broadcasts through.</param>
        /// <param name="config">Session rules, already converted from the authored asset.</param>
        /// <param name="netConfig">Transport and timing tuning the world is simulated and judged with.</param>
        /// <param name="validator">Who decides whether an identity claim is accepted.</param>
        /// <param name="groundProvider">Where the floor is for the pawns this host simulates.</param>
        public ListenServerFrontDesk(
            NetServer server,
            SessionConfigData config,
            NetConfig netConfig,
            IAuthValidator validator,
            IGroundProvider groundProvider) {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _netConfig = netConfig ?? throw new ArgumentNullException(nameof(netConfig));
            _groundProvider = groundProvider ?? new FlatGroundProvider();
            _authDesk = new SessionAuthDesk(server, validator ?? new AnonymousAuthValidator());

            _authDesk.RegisterHandlers(server.Router);
            RegisterHandlers();

            _server.OnPeerDisconnected += HandlePeerDisconnected;
        }

        /// <summary>The one session this host runs, or null before anybody has created it.</summary>
        public SessionHost Host => _host;

        /// <summary>The session's replicated world, or null before the session exists.</summary>
        public ServerReplication Replication => _replication;

        /// <summary>Code a second player types to reach this session, or empty before it exists.</summary>
        public string JoinCode => _host != null ? _host.JoinCode : string.Empty;

        /// <inheritdoc />
        public void HandleCreateSession(PeerHandle peer, PlayerIdentity identity, string profileId) {
            if (_isClosed) return;

            if (_host != null && _host.HasPeer(peer)) {
                _server.Send(peer, SessionMessageIds.JoinSessionDenied,
                    new JoinSessionDenied(SessionEndReason.AlreadyInSession), DeliveryClass.ReliableOrdered);
                return;
            }

            if (_host == null) {
                OpenSession();
            }

            _server.Send(peer, SessionMessageIds.SessionCreated,
                new SessionCreated(_host.SessionId, _host.JoinCode), DeliveryClass.ReliableOrdered);

            AttachPeer(peer, identity);
        }

        /// <inheritdoc />
        public void HandleJoinSession(PeerHandle peer, PlayerIdentity identity, string joinCode) {
            if (_isClosed) return;

            if (_host == null) {
                Deny(peer, SessionEndReason.SessionNotFound);
                return;
            }

            if (_host.HasPeer(peer)) {
                Deny(peer, SessionEndReason.AlreadyInSession);
                return;
            }

            if (!JoinCodeGenerator.TryNormalize(joinCode, out string normalizedCode) || normalizedCode != _host.JoinCode) {
                Deny(peer, SessionEndReason.SessionNotFound);
                return;
            }

            AttachPeer(peer, identity);
        }

        /// <summary>
        /// One pump of the server side: advance the session, then the world it contains.
        /// </summary>
        /// <remarks>
        /// Called from the host's <c>Update</c>, on the main thread, immediately after the server facade
        /// has polled — so the session acts on the messages of this frame rather than the last one.
        /// </remarks>
        public void Tick(float deltaSeconds) {
            if (_isClosed) return;

            _host?.Tick(deltaSeconds);
            _replication?.Tick(_server.Tick, deltaSeconds);
        }

        /// <summary>Closes the session, drops every handler and leaves the desk inert.</summary>
        public void Close(SessionEndReason reason) {
            if (_isClosed) return;

            _isClosed = true;
            _server.OnPeerDisconnected -= HandlePeerDisconnected;

            if (_host != null) {
                _host.OnMemberNeedsKeyframe -= HandleMemberNeedsKeyframe;
                _host.OnMemberLeft -= HandleMemberLeft;
                _host.Close(reason);
                _host = null;
            }

            _replication?.DetachFromRouter();
            _replication = null;

            UnregisterHandlers();
            _authDesk.Clear();
        }

        /// <summary>
        /// Mints the session and the world under it. Split out because a create request is the only
        /// thing that brings a listen host's session into being, and it must be idempotent against a
        /// second request arriving before the first has attached.
        /// </summary>
        private void OpenSession() {
            string joinCode = _joinCodeGenerator.Generate();

            _host = new SessionHost(LocalSessionId, joinCode, _config, _server);
            _host.OnMemberNeedsKeyframe += HandleMemberNeedsKeyframe;
            _host.OnMemberLeft += HandleMemberLeft;
            _host.Open();

            _replication = new ServerReplication(
                _server, ResolveSessionPeers, new MovementValidator(_netConfig), _groundProvider
            );
            _replication.AttachToRouter();
        }

        private void AttachPeer(PeerHandle peer, PlayerIdentity identity) {
            SessionAttachResult result = _host.AttachPeer(peer, identity);

            if (!result.IsAccepted) {
                Deny(peer, result.DenialReason);
                return;
            }

            _replication?.OnPeerJoined(peer);
        }

        private void Deny(PeerHandle peer, SessionEndReason reason) {
            _server.Send(peer, SessionMessageIds.JoinSessionDenied,
                new JoinSessionDenied(reason), DeliveryClass.ReliableOrdered);
        }

        private void RegisterHandlers() {
            MessageRouter router = _server.Router;

            router.Register<CreateSessionRequest>(SessionMessageIds.CreateSessionRequest, HandleCreateSessionRequest);
            router.Register<JoinSessionRequest>(SessionMessageIds.JoinSessionRequest, HandleJoinSessionRequest);
            router.Register<LaunchMatchRequest>(SessionMessageIds.LaunchMatchRequest, HandleLaunchMatchRequest);
            router.Register<ClientReady>(SessionMessageIds.ClientReady, HandleClientReady);
            router.Register<LeaveNotice>(SessionMessageIds.LeaveNotice, HandleLeaveNotice);
        }

        private void UnregisterHandlers() {
            MessageRouter router = _server.Router;

            router.Unregister(SessionMessageIds.CreateSessionRequest);
            router.Unregister(SessionMessageIds.JoinSessionRequest);
            router.Unregister(SessionMessageIds.LaunchMatchRequest);
            router.Unregister(SessionMessageIds.ClientReady);
            router.Unregister(SessionMessageIds.LeaveNotice);
        }

        private void HandleCreateSessionRequest(in CreateSessionRequest message, PeerHandle sender) {
            if (!_authDesk.TryGetIdentity(sender, out PlayerIdentity identity)) return;

            HandleCreateSession(sender, identity, message.ProfileId);
        }

        private void HandleJoinSessionRequest(in JoinSessionRequest message, PeerHandle sender) {
            if (!_authDesk.TryGetIdentity(sender, out PlayerIdentity identity)) return;

            HandleJoinSession(sender, identity, message.JoinCode);
        }

        private void HandleLaunchMatchRequest(in LaunchMatchRequest message, PeerHandle sender) {
            if (_host == null) return;

            _host.HandleLaunchMatchRequest(sender, in message);
        }

        private void HandleClientReady(in ClientReady message, PeerHandle sender) {
            if (_host == null) return;

            _host.HandleClientReady(sender, in message);
        }

        private void HandleLeaveNotice(in LeaveNotice message, PeerHandle sender) {
            if (_host == null) return;

            _host.HandleLeaveNotice(sender);
        }

        private void HandlePeerDisconnected(PeerHandle peer, DisconnectReason reason) {
            _authDesk.Forget(peer);
            _replication?.OnPeerLeft(peer);
            _host?.DetachPeer(peer, LeaveReason.TransportLost);
        }

        /// <summary>
        /// Drops the pawns of a member the session has finished with, so a leave does not leave a body
        /// standing in the igloo.
        /// </summary>
        private void HandleMemberLeft(SessionMember member, LeaveReason reason) {
            if (_replication == null || member == null) return;
            if (member.PeerId == SessionMember.NoPeerId) return;

            _replication.DespawnOwnedBy(member.PeerId);
        }

        /// <summary>
        /// Sends a whole-world keyframe to a member the session says needs one — a newcomer, or somebody
        /// who has just rejoined mid-match.
        /// </summary>
        private void HandleMemberNeedsKeyframe(SessionMember member) {
            if (_replication == null || member == null) return;
            if (member.PeerId == SessionMember.NoPeerId) return;

            _replication.SendKeyframeTo(new PeerHandle(member.PeerId));
        }

        private IReadOnlyList<PeerHandle> ResolveSessionPeers() {
            if (_host == null) return Array.Empty<PeerHandle>();

            return _host.ConnectedPeers;
        }
    }
}
