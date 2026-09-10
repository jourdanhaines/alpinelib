using System;
using System.Collections.Generic;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Claims;
using AlpineLib.Netcode.Sessions.Messages;
using AlpineLib.Netcode.Sessions.Spawning;
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
    /// A listen host is the degenerate case — one machine, one lobby, one session — but it still needs a
    /// front desk, because a <see cref="SessionHost"/> deliberately knows nothing about join codes,
    /// session lookup or the one-session-per-connection rule, and something must claim the create and
    /// join message ids on the shared router. This is that something, kept to the single-session case.
    /// </para>
    /// <para>
    /// It also owns the session's <see cref="ServerReplication"/>, because on a listen host the pawn
    /// simulation is server work like any other and has to be ticked from the same pump. The collision
    /// world it steps against is the scene's exported geometry — the same bytes a dedicated server
    /// loads, so a listen host and a dedicated one simulate the same lobby rather than two of them. Its
    /// <see cref="ServerClaimRegistry"/> is here for the same reason: the host arbitrates who holds a
    /// slot even when the holder is the player sitting at this machine.
    /// </para>
    /// <para>
    /// Player bodies are not its business, though. Handing an arriving member a pawn and taking it away
    /// again is the same job on a listen host as on a dedicated server, so it is delegated to a
    /// <see cref="SessionPawnSpawner"/> the desk merely owns the lifetime of.
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
        private readonly ushort _pawnPrefabId;
        private readonly AuthorityMode _pawnAuthority;
        private readonly ISpawnPlacement _spawnPlacement;

        private CollisionWorld _collisionWorld;
        private SessionHost _host;
        private ServerReplication _replication;
        private ServerClaimRegistry _claims;
        private SessionPawnSpawner _pawnSpawner;
        private bool _isClosed;

        /// <summary>
        /// Stands up the desk over a running server.
        /// </summary>
        /// <param name="server">The server facade the session broadcasts through.</param>
        /// <param name="config">Session rules, already converted from the authored asset.</param>
        /// <param name="netConfig">Transport and timing tuning the world is simulated and judged with.</param>
        /// <param name="validator">Who decides whether an identity claim is accepted.</param>
        /// <param name="collisionWorld">
        /// The scene collision the pawns this host simulates are stepped against. Null falls back to an
        /// endless floor at y = 0, which is what a scene with no exported geometry gets.
        /// </param>
        /// <param name="pawnPrefabId">Which entry of the client's prefab registry a player body is.</param>
        /// <param name="pawnAuthority">Who simulates a player body once it exists.</param>
        /// <param name="placement">
        /// Where arriving players appear. Null falls back to a ring around the origin, which is what a
        /// scene with no authored spawn points gets.
        /// </param>
        public ListenServerFrontDesk(
            NetServer server,
            SessionConfigData config,
            NetConfig netConfig,
            IAuthValidator validator,
            CollisionWorld collisionWorld,
            ushort pawnPrefabId,
            AuthorityMode pawnAuthority,
            ISpawnPlacement placement) {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _netConfig = netConfig ?? throw new ArgumentNullException(nameof(netConfig));
            _collisionWorld = collisionWorld ?? CollisionWorld.Flat();
            _pawnPrefabId = pawnPrefabId;
            _pawnAuthority = pawnAuthority;
            _spawnPlacement = placement ?? new RingSpawnPlacement();
            _authDesk = new SessionAuthDesk(server, validator ?? new AnonymousAuthValidator());

            _authDesk.RegisterHandlers(server.Router);
            RegisterHandlers();

            _server.OnPeerDisconnected += HandlePeerDisconnected;
        }

        /// <summary>The one session this host runs, or null before anybody has created it.</summary>
        public SessionHost Host => _host;

        /// <summary>The session's replicated world, or null before the session exists.</summary>
        public ServerReplication Replication => _replication;

        /// <summary>The session's claim slots, or null before the session exists.</summary>
        public ServerClaimRegistry Claims => _claims;

        /// <summary>What gives the session's members their bodies, or null before the session exists.</summary>
        public SessionPawnSpawner PawnSpawner => _pawnSpawner;

        /// <summary>Code a second player types to reach this session, or empty before it exists.</summary>
        public string JoinCode => _host != null ? _host.JoinCode : string.Empty;

        /// <summary>The scene collision this host simulates against.</summary>
        public CollisionWorld CollisionWorld => _collisionWorld;

        /// <summary>
        /// Moves the host onto another scene's collision, replacing the platforms of the old scene with
        /// the new one's.
        /// </summary>
        /// <remarks>
        /// A listen host changes scene the same way a dedicated server's session does, and for the same
        /// reason: the pawns stay, the geometry under them does not. Called by whoever owns the phase — the
        /// session service, from the very event it swaps its own prediction world on — so that the host's
        /// two halves never disagree about the floor. Before a session exists the world is only recorded;
        /// the replication that would spawn its movers is created by the first create request.
        /// </remarks>
        public void UseWorld(CollisionWorld collisionWorld) {
            if (collisionWorld == null) {
                throw new ArgumentNullException(nameof(collisionWorld));
            }

            _collisionWorld = collisionWorld;
            _replication?.UseWorld(collisionWorld);
        }

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

            _pawnSpawner?.Dispose();
            _pawnSpawner = null;

            if (_host != null) {
                _host.OnMemberNeedsKeyframe -= HandleMemberNeedsKeyframe;
                _host.Close(reason);
                _host = null;
            }

            _replication?.DetachFromRouter();
            _replication = null;

            _claims?.DetachFromRouter();
            _claims = null;

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
            _host.Open();

            _replication = new ServerReplication(
                _server, ResolveSessionPeers, new MovementValidator(_netConfig), _collisionWorld
            );
            _replication.AttachToRouter();

            _claims = new ServerClaimRegistry(_server, ResolveSessionPeers);
            _claims.AttachToRouter();

            // The constructor installs the world but spawns nothing: entities for the scene's movers are
            // UseWorld's doing, and without this call a listen host would simulate platforms nobody was
            // ever told about. Nobody has attached yet, so the spawns go to an empty peer list and reach
            // the first member as part of the keyframe its join sends.
            _replication.UseWorld(_collisionWorld);

            _pawnSpawner = new SessionPawnSpawner(_host, _replication, _pawnPrefabId, _pawnAuthority, _spawnPlacement);

            // Subscribed after the spawner because subscription order is invocation order: a newcomer has
            // to be told the world exists before it is told who is holding what in it.
            _host.OnMemberNeedsKeyframe += HandleMemberNeedsKeyframe;
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

        /// <summary>
        /// Sees a member out, freeing the levers it was holding on the way.
        /// </summary>
        /// <remarks>
        /// A graceful leave never touches the transport, so the disconnect path may not run for this peer
        /// at all — and the host retires the member before it announces the departure, which zeroes the
        /// peer id. This is the last place the handle still exists, so it is the only place the slots can
        /// be freed by name.
        /// </remarks>
        private void HandleLeaveNotice(in LeaveNotice message, PeerHandle sender) {
            if (_host == null) return;

            _claims?.ReleaseAllHeldBy(sender);
            _host.HandleLeaveNotice(sender);
        }

        private void HandlePeerDisconnected(PeerHandle peer, DisconnectReason reason) {
            _authDesk.Forget(peer);
            _replication?.OnPeerLeft(peer);
            _claims?.ReleaseAllHeldBy(peer);
            _host?.DetachPeer(peer, LeaveReason.TransportLost);
        }

        /// <summary>
        /// Sends the held claim slots in full to a member the session says needs them — a newcomer, or
        /// somebody who has just rejoined mid-match. The world's own keyframe is the spawner's business.
        /// </summary>
        private void HandleMemberNeedsKeyframe(SessionMember member) {
            if (member == null || member.PeerId == SessionMember.NoPeerId) return;

            _claims?.SendKeyframeTo(new PeerHandle(member.PeerId));
        }

        private IReadOnlyList<PeerHandle> ResolveSessionPeers() {
            if (_host == null) return Array.Empty<PeerHandle>();

            return _host.ConnectedPeers;
        }
    }
}
