using System;
using System.Collections.Generic;
using AlpineLib.Chat.Wire;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Replication.Messages;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Claims.Messages;
using AlpineLib.Netcode.Sessions.Messages;
using AlpineLib.Netcode.Sessions.Spawning;
using AlpineLib.Netcode.Transport;
using AlpineLib.Server.Configuration;
using Microsoft.Extensions.Logging;

namespace AlpineLib.Server.Sessions {
    /// <summary>
    /// The front desk of the dedicated server: it owns every authenticated connection that is not yet in
    /// a session, decides which session each one belongs to, and holds the sessions themselves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One process, one socket, one router — but many sessions. That is the whole reason this type
    /// exists. A <see cref="SessionHost"/> claims no message ids precisely because several of them share
    /// the wire, so somebody has to claim the create/join ids, the replication ids, the claim ids and the
    /// chat envelope once, work out which session an arriving message belongs to, and forward it. The
    /// game's own ids go the same way, through <see cref="ISessionModuleFactory.RegisterHandlers"/>.
    /// Everything else here follows from that: the join-code table, the peer-to-session map, the session
    /// cap.
    /// </para>
    /// <para>
    /// <b>One session per connection.</b> A peer that is already attached is refused rather than moved,
    /// and leaving hands it back to the desk still connected and still authenticated — able to create or
    /// join again without a second handshake. That is what makes "quit to menu, join a friend" a single
    /// connection rather than a reconnect.
    /// </para>
    /// <para>
    /// <b>Threading.</b> Everything here runs on the game-loop thread, inside the transport poll or the
    /// tick that follows it. Nothing is locked and nothing may be touched from another thread: a caller
    /// off the loop reads through <c>GameThreadInbox</c> and gets a copy.
    /// </para>
    /// </remarks>
    public sealed class SessionRegistry : ISessionFrontDesk, IDisposable {
        private readonly NetServer _server;
        private readonly ServerConfigBundle _config;
        private readonly ILogger<SessionRegistry> _logger;
        private readonly SessionAuthDesk _authDesk;
        private readonly MovementValidator _movementValidator;
        private readonly SceneGeometryLibrary _geometry;
        private readonly ISessionModuleFactory _moduleFactory;
        private readonly Func<ServerConfigBundle, ISpawnPlacement> _placementFactory;
        private readonly JoinCodeGenerator _joinCodes = new JoinCodeGenerator();
        private readonly Func<long> _clock;
        private readonly int _maxSessions;

        private readonly List<SessionEntry> _entries = new List<SessionEntry>();
        private readonly Dictionary<string, SessionEntry> _entryByJoinCode = new Dictionary<string, SessionEntry>(StringComparer.Ordinal);
        private readonly Dictionary<int, SessionEntry> _entryByPeerId = new Dictionary<int, SessionEntry>();

        private int _nextSessionNumber = 1;
        private bool _disposed;

        /// <summary>Wires the desk onto a server's router. The server need not be started yet.</summary>
        /// <param name="geometry">
        /// Collision geometry for every scene this deployment exported, loaded once at startup and shared
        /// by every session the desk opens — the sessions only ever resolve out of it, never into it.
        /// </param>
        /// <param name="moduleFactory">
        /// The game's own simulation, or null for a server that hosts sessions and nothing more. Its
        /// handlers are registered here, once, because the router is process-wide.
        /// </param>
        /// <param name="placementFactory">
        /// Builds a fresh placement for each session opened. Null falls back to the exported spawn
        /// settings, which is what a game that authored a <c>spawn</c> section and nothing else wants.
        /// </param>
        public SessionRegistry(
            NetServer server,
            ServerConfigBundle config,
            IAuthValidator authValidator,
            SceneGeometryLibrary geometry,
            ISessionModuleFactory moduleFactory,
            Func<ServerConfigBundle, ISpawnPlacement> placementFactory,
            int maxSessions,
            Func<long> clock,
            ILogger<SessionRegistry> logger) {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _moduleFactory = moduleFactory;
            _placementFactory = placementFactory ?? CreateDefaultPlacement;
            _maxSessions = maxSessions < 1 ? 1 : maxSessions;
            _movementValidator = new MovementValidator(config.Net);

            _authDesk = new SessionAuthDesk(server, authValidator ?? throw new ArgumentNullException(nameof(authValidator)));
            _authDesk.RegisterHandlers(server.Router);

            RegisterSessionHandlers();
            RegisterReplicationHandlers();
            RegisterClaimHandlers();
            _server.RegisterRaw(ChatMessageIds.ChatPayload, HandleChatEnvelope);
            _moduleFactory?.RegisterHandlers(_server.Router, FindByPeer);
            _server.OnPeerDisconnected += HandlePeerDisconnected;
        }

        /// <summary>Sessions currently standing, in creation order.</summary>
        public IReadOnlyList<SessionEntry> Sessions => _entries;

        /// <summary>How many sessions this process will host at once.</summary>
        public int MaxSessions => _maxSessions;

        /// <summary>Connections that have authenticated, whether or not they are in a session.</summary>
        public int AuthenticatedPeerCount => _authDesk.AuthenticatedCount;

        /// <summary>Builds the placement a bundle's spawn settings describe. The default when none is given.</summary>
        public static ISpawnPlacement CreateDefaultPlacement(ServerConfigBundle config) {
            if (config == null) {
                throw new ArgumentNullException(nameof(config));
            }

            return config.Spawn.CreatePlacement();
        }

        /// <inheritdoc />
        public void HandleCreateSession(PeerHandle peer, PlayerIdentity identity, string profileId) {
            if (_entryByPeerId.ContainsKey(peer.Id)) {
                Deny(peer, SessionEndReason.AlreadyInSession);
                return;
            }

            if (_entries.Count >= _maxSessions) {
                _logger.LogWarning("Refused a session for {DisplayName}: the process cap of {MaxSessions} is reached.",
                    identity.DisplayName, _maxSessions);
                Deny(peer, SessionEndReason.Full);
                return;
            }

            SessionEntry entry = OpenSession(profileId);
            SessionCreated created = new SessionCreated(entry.Host.SessionId, entry.Host.JoinCode);
            _server.Send(peer, SessionMessageIds.SessionCreated, in created, DeliveryClass.ReliableOrdered);

            if (Attach(entry, peer, identity)) {
                return;
            }

            // The owner never got in, so the session it was created for has nobody to wait for. Retiring
            // it now beats letting an empty session hold a join code until the shutdown timer notices.
            RetireStillbornSession(entry);
        }

        /// <inheritdoc />
        public void HandleJoinSession(PeerHandle peer, PlayerIdentity identity, string joinCode) {
            if (_entryByPeerId.ContainsKey(peer.Id)) {
                Deny(peer, SessionEndReason.AlreadyInSession);
                return;
            }

            if (!JoinCodeGenerator.TryNormalize(joinCode, out string normalized)) {
                Deny(peer, SessionEndReason.SessionNotFound);
                return;
            }

            if (!_entryByJoinCode.TryGetValue(normalized, out SessionEntry entry)) {
                Deny(peer, SessionEndReason.SessionNotFound);
                return;
            }

            Attach(entry, peer, identity);
        }

        /// <summary>One step of every session this process hosts.</summary>
        public void TickAll(float deltaSeconds) {
            uint serverTick = _server.Tick;

            for (int entryIndex = 0; entryIndex < _entries.Count; entryIndex++) {
                _entries[entryIndex].Tick(serverTick, deltaSeconds);
            }
        }

        /// <summary>
        /// Retires sessions that have closed — on an empty-shutdown timer, an owner's departure, or a
        /// call to <see cref="CloseAll"/> — and hands their peers back to the desk.
        /// </summary>
        /// <remarks>
        /// Closing is decided inside the session, which knows its own lifetime rules; disposing is decided
        /// here, which knows what else was hung off it. Splitting them means a session that closes
        /// mid-tick finishes its broadcast before its chat pipeline is torn down.
        /// </remarks>
        public void SweepLifetimes() {
            for (int entryIndex = _entries.Count - 1; entryIndex >= 0; entryIndex--) {
                SessionEntry entry = _entries[entryIndex];

                if (!entry.Host.IsClosed) {
                    continue;
                }

                RetireSession(entry, entryIndex);
            }
        }

        /// <summary>Copies the whole directory out for a caller on another thread.</summary>
        public DirectorySnapshot BuildSnapshot() {
            List<SessionSnapshot> sessions = new List<SessionSnapshot>(_entries.Count);

            for (int entryIndex = 0; entryIndex < _entries.Count; entryIndex++) {
                sessions.Add(BuildSessionSnapshot(_entries[entryIndex].Host));
            }

            return new DirectorySnapshot(_clock(), _server.Tick, _maxSessions, _server.Peers.Count, sessions);
        }

        /// <summary>Removes a player from a session by administrative order. False when nobody matched.</summary>
        public bool KickPlayer(string sessionId, PlayerId playerId, string reason) {
            SessionEntry entry = FindBySessionId(sessionId);

            if (entry == null) {
                return false;
            }

            SessionMember member = entry.Host.FindMember(playerId);

            if (member == null || !member.IsConnected) {
                return false;
            }

            _logger.LogInformation("Kicking {DisplayName} from {SessionId}: {Reason}", member.DisplayName, sessionId, reason);
            entry.Host.Kick(playerId, reason ?? string.Empty);
            return true;
        }

        /// <summary>The session with this id, or null.</summary>
        public SessionEntry FindBySessionId(string sessionId) {
            if (string.IsNullOrEmpty(sessionId)) {
                return null;
            }

            for (int entryIndex = 0; entryIndex < _entries.Count; entryIndex++) {
                SessionEntry candidate = _entries[entryIndex];

                if (string.Equals(candidate.Host.SessionId, sessionId, StringComparison.Ordinal)) {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>The session a peer is attached to, or null while it is still at the desk.</summary>
        public SessionEntry FindByPeer(PeerHandle peer) {
            return _entryByPeerId.TryGetValue(peer.Id, out SessionEntry entry) ? entry : null;
        }

        /// <summary>
        /// Tells every session it is over. Each broadcasts <c>SessionClosing</c> to its members, which is
        /// the notice a client turns into "the server went away" instead of a silent timeout.
        /// </summary>
        public void CloseAll(SessionEndReason reason) {
            for (int entryIndex = 0; entryIndex < _entries.Count; entryIndex++) {
                _entries[entryIndex].Host.Close(reason);
            }
        }

        /// <inheritdoc />
        public void Dispose() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            _server.OnPeerDisconnected -= HandlePeerDisconnected;
            _server.UnregisterRaw(ChatMessageIds.ChatPayload);

            for (int entryIndex = 0; entryIndex < _entries.Count; entryIndex++) {
                _entries[entryIndex].Dispose();
            }

            _entries.Clear();
            _entryByJoinCode.Clear();
            _entryByPeerId.Clear();
            _authDesk.Clear();
        }

        private void RegisterSessionHandlers() {
            _server.Router.Register<CreateSessionRequest>(SessionMessageIds.CreateSessionRequest, ReceiveCreateSessionRequest);
            _server.Router.Register<JoinSessionRequest>(SessionMessageIds.JoinSessionRequest, ReceiveJoinSessionRequest);
            _server.Router.Register<LaunchMatchRequest>(SessionMessageIds.LaunchMatchRequest, ReceiveLaunchMatchRequest);
            _server.Router.Register<ClientReady>(SessionMessageIds.ClientReady, ReceiveClientReady);
            _server.Router.Register<LeaveNotice>(SessionMessageIds.LeaveNotice, ReceiveLeaveNotice);
        }

        private void RegisterReplicationHandlers() {
            _server.Router.Register<InputCommand>(ReplicationMessageIds.InputCommand, ReceiveInputCommand);
            _server.Router.Register<OwnerPawnUpdate>(ReplicationMessageIds.OwnerPawnUpdate, ReceiveOwnerPawnUpdate);
            _server.Router.Register<EntityEvent>(ReplicationMessageIds.EntityEvent, ReceiveEntityEvent);
        }

        private void RegisterClaimHandlers() {
            _server.Router.Register<ClaimRequest>(ClaimMessageIds.ClaimRequest, ReceiveClaimRequest);
            _server.Router.Register<ClaimRelease>(ClaimMessageIds.ClaimRelease, ReceiveClaimRelease);
        }

        private SessionEntry OpenSession(string profileId) {
            string joinCode = _joinCodes.Generate(IsJoinCodeTaken);
            string sessionId = "session-" + _nextSessionNumber.ToString();
            _nextSessionNumber++;

            SessionHost host = new SessionHost(sessionId, joinCode, _config.Session, _server);
            host.Open();

            SessionEntry entry = new SessionEntry(
                host,
                _server,
                _movementValidator,
                _config.Chat,
                _geometry,
                _config.Spawn,
                _placementFactory(_config),
                _moduleFactory,
                _clock,
                _logger);

            _entries.Add(entry);
            _entryByJoinCode.Add(joinCode, entry);

            _logger.LogInformation("Opened session {SessionId} with join code {JoinCode} (profile '{ProfileId}').",
                sessionId, joinCode, ResolveProfileId(profileId));
            return entry;
        }

        private string ResolveProfileId(string requestedProfileId) {
            string configured = _config.Session.Profile == null ? string.Empty : _config.Session.Profile.ProfileId ?? string.Empty;

            if (string.IsNullOrEmpty(requestedProfileId) || string.Equals(requestedProfileId, configured, StringComparison.Ordinal)) {
                return configured;
            }

            // One profile per deployment. A client asking for another is not an error worth refusing a
            // session over, but an operator should see that the ask was ignored.
            _logger.LogDebug("Ignoring requested session profile '{RequestedProfileId}'; this server only serves '{ConfiguredProfileId}'.",
                requestedProfileId, configured);
            return configured;
        }

        private bool Attach(SessionEntry entry, PeerHandle peer, PlayerIdentity identity) {
            SessionAttachResult result = entry.Host.AttachPeer(peer, identity);

            if (!result.IsAccepted) {
                _logger.LogInformation("Refused {DisplayName} entry to {SessionId}: {Reason}.",
                    identity.DisplayName, entry.Host.SessionId, result.DenialReason);
                Deny(peer, result.DenialReason);
                return false;
            }

            _entryByPeerId[peer.Id] = entry;
            entry.OnPeerJoined(peer);
            _logger.LogInformation("{DisplayName} {Verb} {SessionId} ({ConnectedCount} connected).",
                identity.DisplayName, result.IsRejoin ? "rejoined" : "joined", entry.Host.SessionId, entry.Host.ConnectedMemberCount);
            return true;
        }

        private void Deny(PeerHandle peer, SessionEndReason reason) {
            JoinSessionDenied denied = new JoinSessionDenied(reason);
            _server.Send(peer, SessionMessageIds.JoinSessionDenied, in denied, DeliveryClass.ReliableOrdered);
        }

        private bool IsJoinCodeTaken(string candidate) {
            return _entryByJoinCode.ContainsKey(candidate);
        }

        private void RetireStillbornSession(SessionEntry entry) {
            entry.Host.Close(SessionEndReason.HostClosed);
            int entryIndex = _entries.IndexOf(entry);

            if (entryIndex < 0) {
                return;
            }

            RetireSession(entry, entryIndex);
        }

        private void RetireSession(SessionEntry entry, int entryIndex) {
            _entries.RemoveAt(entryIndex);
            _entryByJoinCode.Remove(entry.Host.JoinCode);
            ReleasePeersOf(entry);
            entry.Dispose();
            _logger.LogInformation("Retired session {SessionId}.", entry.Host.SessionId);
        }

        private void ReleasePeersOf(SessionEntry entry) {
            List<int> released = new List<int>();

            foreach (KeyValuePair<int, SessionEntry> pair in _entryByPeerId) {
                if (pair.Value == entry) {
                    released.Add(pair.Key);
                }
            }

            for (int peerIndex = 0; peerIndex < released.Count; peerIndex++) {
                _entryByPeerId.Remove(released[peerIndex]);
            }
        }

        private SessionSnapshot BuildSessionSnapshot(SessionHost host) {
            IReadOnlyList<SessionMember> members = host.Members;
            List<SessionMemberSnapshot> rows = new List<SessionMemberSnapshot>(members.Count);

            for (int memberIndex = 0; memberIndex < members.Count; memberIndex++) {
                SessionMember member = members[memberIndex];
                rows.Add(new SessionMemberSnapshot(member.PlayerId, member.DisplayName, member.IsOwner, member.IsConnected));
            }

            return new SessionSnapshot(
                host.SessionId,
                host.JoinCode,
                host.Phase,
                host.OwnerId,
                host.CurrentMatch == null ? string.Empty : host.CurrentMatch.MatchId,
                host.MatchSequence,
                host.ConnectedMemberCount,
                rows);
        }

        private void ReceiveCreateSessionRequest(in CreateSessionRequest message, PeerHandle sender) {
            if (!_authDesk.TryGetIdentity(sender, out PlayerIdentity identity)) {
                Deny(sender, SessionEndReason.AuthRejected);
                return;
            }

            HandleCreateSession(sender, identity, message.ProfileId);
        }

        private void ReceiveJoinSessionRequest(in JoinSessionRequest message, PeerHandle sender) {
            if (!_authDesk.TryGetIdentity(sender, out PlayerIdentity identity)) {
                Deny(sender, SessionEndReason.AuthRejected);
                return;
            }

            HandleJoinSession(sender, identity, message.JoinCode);
        }

        private void ReceiveLaunchMatchRequest(in LaunchMatchRequest message, PeerHandle sender) {
            SessionEntry entry = FindByPeer(sender);
            entry?.Host.HandleLaunchMatchRequest(sender, in message);
        }

        private void ReceiveClientReady(in ClientReady message, PeerHandle sender) {
            SessionEntry entry = FindByPeer(sender);
            entry?.Host.HandleClientReady(sender, in message);
        }

        private void ReceiveLeaveNotice(in LeaveNotice message, PeerHandle sender) {
            SessionEntry entry = FindByPeer(sender);

            if (entry == null) {
                return;
            }

            // Back to the desk, not off the server: the connection stays authenticated and may create or
            // join another session without a second handshake. The departure is announced first, while the
            // sender is still on the roster and its handle still names somebody.
            _entryByPeerId.Remove(sender.Id);
            entry.OnPeerLeft(sender);
            entry.Host.HandleLeaveNotice(sender);
        }

        private void ReceiveInputCommand(in InputCommand message, PeerHandle sender) {
            SessionEntry entry = FindByPeer(sender);
            entry?.Replication.HandleInputCommand(in message, sender);
        }

        private void ReceiveOwnerPawnUpdate(in OwnerPawnUpdate message, PeerHandle sender) {
            SessionEntry entry = FindByPeer(sender);
            entry?.Replication.HandleOwnerPawnUpdate(in message, sender);
        }

        private void ReceiveEntityEvent(in EntityEvent message, PeerHandle sender) {
            SessionEntry entry = FindByPeer(sender);
            entry?.Replication.HandleEntityEvent(in message, sender);
        }

        private void ReceiveClaimRequest(in ClaimRequest message, PeerHandle sender) {
            SessionEntry entry = FindByPeer(sender);
            entry?.Claims.HandleClaimRequest(in message, sender);
        }

        private void ReceiveClaimRelease(in ClaimRelease message, PeerHandle sender) {
            SessionEntry entry = FindByPeer(sender);
            entry?.Claims.HandleClaimRelease(in message, sender);
        }

        private void HandleChatEnvelope(ushort envelopeId, ArraySegment<byte> payload, PeerHandle sender) {
            SessionEntry entry = FindByPeer(sender);
            entry?.ReceiveChatPayload(sender, payload);
        }

        private void HandlePeerDisconnected(PeerHandle peer, DisconnectReason reason) {
            _authDesk.Forget(peer);

            if (!_entryByPeerId.TryGetValue(peer.Id, out SessionEntry entry)) {
                return;
            }

            _entryByPeerId.Remove(peer.Id);
            entry.OnPeerLeft(peer);
            entry.Host.DetachPeer(peer, ToLeaveReason(reason));
        }

        private static LeaveReason ToLeaveReason(DisconnectReason reason) {
            if (reason == DisconnectReason.Kicked) {
                return LeaveReason.Kicked;
            }

            // Everything else is a link that went away. A player who meant to quit sent a LeaveNotice
            // first, and the session retired them before this ever arrived.
            return LeaveReason.TransportLost;
        }
    }
}
