using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Sessions.Messages;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// One session — one igloo and every match played out of it — as an authoritative object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A session is not a server and not a room. It is a roster plus a phase, and a match is a phase and
    /// a scene change over that same roster: nobody is transferred anywhere when a match starts, which is
    /// why the party, the chat scope and the replicated world all stay exactly as they were.
    /// </para>
    /// <para>
    /// <b>It does not own the socket.</b> Several hosts share one <see cref="NetServer"/> and therefore
    /// one <see cref="MessageRouter"/>, so this type registers no handlers and claims no message ids.
    /// A front desk (see <see cref="ISessionFrontDesk"/>) owns the registrations, resolves which session
    /// a peer belongs to, and forwards through <see cref="AttachPeer"/>,
    /// <see cref="HandleLaunchMatchRequest"/>, <see cref="HandleClientReady"/>,
    /// <see cref="HandleLeaveNotice"/> and <see cref="DetachPeer"/>. Peers arrive already authenticated.
    /// </para>
    /// <para>
    /// <b>Threading.</b> Everything happens on the thread that calls <see cref="Tick"/> — the Unity main
    /// thread on a listen host, the fixed-step game-loop thread on the dedicated server. There is no
    /// locking here because there is nothing to lock against; asynchronous work reaches this layer only
    /// through <c>NetServer.Inbox</c>.
    /// </para>
    /// <para>
    /// <b>Disconnects are not departures.</b> When the profile allows a rejoin, a peer that drops keeps
    /// its seat: the member stays on the roster marked disconnected, its <see cref="PlayerId"/> reserved,
    /// and reclaiming it is an ordinary join carrying the same player id. That is what makes a mid-match
    /// reconnect restore a player rather than admit a stranger.
    /// </para>
    /// </remarks>
    public sealed class SessionHost {
        /// <summary>Party id every member carries in v1: the party is the lobby.</summary>
        private const byte DefaultPartyId = 0;

        private readonly string _sessionId;
        private readonly string _joinCode;
        private readonly SessionConfigData _config;
        private readonly SessionProfileData _profile;
        private readonly LobbyConfigData _lobby;
        private readonly NetServer _server;

        private readonly List<SessionMember> _members = new List<SessionMember>();
        private readonly List<PeerHandle> _connectedPeers = new List<PeerHandle>();
        private readonly Dictionary<int, PlayerId> _playerByPeerId = new Dictionary<int, PlayerId>();
        private readonly Dictionary<PlayerId, float> _reservationSecondsLeft = new Dictionary<PlayerId, float>();
        private readonly List<PlayerId> _reservationScratch = new List<PlayerId>();
        private readonly HashSet<PlayerId> _readyPlayers = new HashSet<PlayerId>();
        private readonly List<SessionMember> _stragglerScratch = new List<SessionMember>();

        private SessionPhase _phase = SessionPhase.Lobby;
        private PlayerId _ownerId = PlayerId.None;
        private MatchContextData _currentMatch;
        private MatchDefinitionData _currentDefinition;
        private int _matchSequence;
        private float _readyElapsedSeconds;
        private float _matchElapsedSeconds;
        private float _resultsElapsedSeconds;
        private float _emptyElapsedSeconds;
        private bool _isOpen;
        private bool _isClosed;

        public SessionHost(string sessionId, string joinCode, SessionConfigData config, NetServer server) {
            _sessionId = sessionId ?? string.Empty;
            _joinCode = joinCode ?? string.Empty;
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _profile = config.Profile ?? new SessionProfileData();
            _lobby = config.Lobby ?? new LobbyConfigData();
        }

        /// <summary>A peer became a member. The flag distinguishes a reclaimed seat from a new one.</summary>
        public event Action<SessionMember, bool> OnMemberJoined;

        /// <summary>A member left the session or lost its link. Fires for reservations too.</summary>
        public event Action<SessionMember, LeaveReason> OnMemberLeft;

        /// <summary>
        /// A member needs the world sent to it whole — on a fresh join and on a rejoin alike. The
        /// replication layer answers this with a keyframe snapshot; nothing here knows how.
        /// </summary>
        public event Action<SessionMember> OnMemberNeedsKeyframe;

        /// <summary>The session moved to a new phase, after the change was broadcast.</summary>
        public event Action<SessionPhase> OnPhaseChanged;

        /// <summary>The ready barrier cleared and the match is live.</summary>
        public event Action<MatchContextData> OnMatchActive;

        /// <summary>A match finished; the session is holding on its results.</summary>
        public event Action<MatchResultData> OnMatchEnded;

        /// <summary>Ownership moved to another member, after the change was broadcast.</summary>
        public event Action<PlayerId> OnOwnerChanged;

        /// <summary>The session is over and its roster has been cleared.</summary>
        public event Action<SessionEndReason> OnClosed;

        /// <summary>Server-unique identifier for this session.</summary>
        public string SessionId => _sessionId;

        /// <summary>The code friends type to reach this session.</summary>
        public string JoinCode => _joinCode;

        /// <summary>Configuration every member is handed on join.</summary>
        public SessionConfigData Config => _config;

        /// <summary>Where the session sits in its lifecycle.</summary>
        public SessionPhase Phase => _phase;

        /// <summary>
        /// The roster, in join order, including members currently disconnected but still holding a
        /// rejoin reservation.
        /// </summary>
        public IReadOnlyList<SessionMember> Members => _members;

        /// <summary>
        /// Peers to broadcast to: every connected member. This is the peer source the replication layer
        /// reads, which is what makes replication session-scoped without knowing sessions exist.
        /// </summary>
        public IReadOnlyList<PeerHandle> ConnectedPeers => _connectedPeers;

        /// <summary>The member who may launch matches, or <see cref="PlayerId.None"/> while ownerless.</summary>
        public PlayerId OwnerId => _ownerId;

        /// <summary>The match being loaded or played, or null in lobby and results.</summary>
        public MatchContextData CurrentMatch => _currentMatch;

        /// <summary>Counter incremented per launch; clients stamp their ready signal with it.</summary>
        public int MatchSequence => _matchSequence;

        /// <summary>True between <see cref="Open"/> and <see cref="Close"/>.</summary>
        public bool IsOpen => _isOpen && !_isClosed;

        /// <summary>True once the session has closed. A closed host is not reopened.</summary>
        public bool IsClosed => _isClosed;

        /// <summary>How many members currently hold a live connection.</summary>
        public int ConnectedMemberCount => CountConnectedMembers();

        /// <summary>Opens the session for business. Idempotent.</summary>
        public void Open() {
            if (_isClosed) {
                throw new InvalidOperationException("A closed SessionHost cannot be reopened.");
            }

            if (_isOpen) {
                return;
            }

            _isOpen = true;
            _phase = SessionPhase.Lobby;
        }

        /// <summary>
        /// One step of the session: age rejoin reservations, advance whatever the current phase is
        /// waiting on, and decide whether an empty session has waited long enough to shut down.
        /// </summary>
        public void Tick(float deltaSeconds) {
            if (!_isOpen || _isClosed) {
                return;
            }

            TickReservations(deltaSeconds);
            TickPhase(deltaSeconds);
            TickLifetime(deltaSeconds);
        }

        /// <summary>
        /// Admits an authenticated peer, either into a free seat or back into the one it was holding.
        /// Sends the newcomer its <c>JoinAccepted</c> and tells everyone else it arrived; denials are
        /// reported to the caller and answered by the front desk.
        /// </summary>
        public SessionAttachResult AttachPeer(PeerHandle peer, PlayerIdentity identity) {
            if (identity == null) {
                throw new ArgumentNullException(nameof(identity));
            }

            if (!_isOpen || _isClosed || _phase == SessionPhase.Closing) {
                return SessionAttachResult.Denied(SessionEndReason.HostClosed);
            }

            if (!identity.PlayerId.IsValid) {
                return SessionAttachResult.Denied(SessionEndReason.AuthRejected);
            }

            if (_playerByPeerId.ContainsKey(peer.Id)) {
                return SessionAttachResult.Denied(SessionEndReason.AlreadyInSession);
            }

            SessionMember existing = FindMember(identity.PlayerId);
            if (existing != null && existing.IsConnected) {
                return SessionAttachResult.Denied(SessionEndReason.AlreadyInSession);
            }

            if (existing != null) {
                return ReclaimSeat(existing, peer, identity);
            }

            return AdmitNewMember(peer, identity);
        }

        /// <summary>
        /// Releases a peer. A graceful leave or a kick retires the member outright; a lost link keeps the
        /// seat reserved when the profile allows a rejoin.
        /// </summary>
        public void DetachPeer(PeerHandle peer, LeaveReason reason) {
            if (!_playerByPeerId.TryGetValue(peer.Id, out PlayerId playerId)) {
                return;
            }

            _playerByPeerId.Remove(peer.Id);
            SessionMember member = FindMember(playerId);
            if (member == null) {
                RebuildConnectedPeers();
                return;
            }

            RetireMember(member, playerId, reason);
            AnnounceDeparture(playerId, member, reason);
            HandleOwnerDeparture(playerId);
            EvaluateReadyBarrier();
        }

        /// <summary>A member asked for a match. Denials are answered on the wire, not thrown.</summary>
        public void HandleLaunchMatchRequest(PeerHandle peer, in LaunchMatchRequest message) {
            if (!_playerByPeerId.TryGetValue(peer.Id, out PlayerId playerId)) {
                return;
            }

            if (TryLaunchMatch(message.MatchId, playerId, out string failReason)) {
                return;
            }

            LaunchMatchDenied denied = new LaunchMatchDenied(failReason);
            _server.Send(peer, SessionMessageIds.LaunchMatchDenied, in denied, DeliveryClass.ReliableOrdered);
        }

        /// <summary>
        /// A client finished loading. The match sequence is checked because a straggler's ready signal
        /// for the previous match must never satisfy the barrier for the next one.
        /// </summary>
        public void HandleClientReady(PeerHandle peer, in ClientReady message) {
            if (_phase != SessionPhase.MatchLoading || _currentMatch == null) {
                return;
            }

            if (message.MatchSequence != _matchSequence) {
                return;
            }

            if (!_playerByPeerId.TryGetValue(peer.Id, out PlayerId playerId)) {
                return;
            }

            if (!_currentMatch.HasParticipant(playerId)) {
                return;
            }

            _readyPlayers.Add(playerId);
            EvaluateReadyBarrier();
        }

        /// <summary>A member said it is leaving on purpose. No reservation is kept.</summary>
        public void HandleLeaveNotice(PeerHandle peer) {
            DetachPeer(peer, LeaveReason.Quit);
        }

        /// <summary>Launches a match on the server's own authority, skipping the owner check.</summary>
        public bool TryLaunchMatch(string matchId, out string failReason) {
            return TryLaunchMatch(matchId, PlayerId.None, out failReason);
        }

        /// <summary>
        /// Launches a match on behalf of a member. The party is the whole lobby: every connected member
        /// becomes a participant.
        /// </summary>
        public bool TryLaunchMatch(string matchId, PlayerId requester, out string failReason) {
            failReason = string.Empty;

            if (!_isOpen || _isClosed) {
                failReason = "The session is not open.";
                return false;
            }

            if (_phase != SessionPhase.Lobby) {
                failReason = "A match is already in progress.";
                return false;
            }

            if (!IsLaunchAuthorized(requester)) {
                failReason = "Only the session owner can launch a match.";
                return false;
            }

            MatchDefinitionData definition = _config.FindMatch(matchId);
            if (definition == null) {
                failReason = "Unknown match '" + (matchId ?? string.Empty) + "'.";
                return false;
            }

            return TryLaunchDefinition(definition, out failReason);
        }

        /// <summary>Ends the running match and holds the session on its results.</summary>
        public void EndMatch(MatchResultData results) {
            if (_phase != SessionPhase.MatchLoading && _phase != SessionPhase.MatchActive) {
                return;
            }

            MatchResultData resolved = results ?? BuildDefaultResult();
            MatchEnd end = new MatchEnd(resolved);
            Broadcast(SessionMessageIds.MatchEnd, in end);

            _resultsElapsedSeconds = 0f;
            SetPhase(SessionPhase.MatchResults);
            OnMatchEnded?.Invoke(resolved);
        }

        /// <summary>Cuts the results hold short and takes everyone back to the igloo now.</summary>
        public void ReturnToLobbyNow() {
            if (_phase != SessionPhase.MatchResults) {
                return;
            }

            EnterLobbyPhase();
        }

        /// <summary>Removes a member for a game-level reason and closes its connection.</summary>
        public void Kick(PlayerId playerId, string reason) {
            SessionMember member = FindMember(playerId);
            if (member == null || !member.IsConnected) {
                return;
            }

            PeerHandle peer = new PeerHandle(member.PeerId);
            string detail = reason ?? string.Empty;
            Kick notice = new Kick(detail);
            _server.Send(peer, SessionMessageIds.Kick, in notice, DeliveryClass.ReliableOrdered);

            DetachPeer(peer, LeaveReason.Kicked);
            _server.Kick(peer, DisconnectReason.Kicked, detail);
        }

        /// <summary>Ends the session, tells whoever is still connected, and empties the roster.</summary>
        public void Close(SessionEndReason reason) {
            if (_isClosed) {
                return;
            }

            SessionClosing closing = new SessionClosing(reason);
            Broadcast(SessionMessageIds.SessionClosing, in closing);

            _isClosed = true;
            _phase = SessionPhase.Closing;
            ClearSessionState();

            OnPhaseChanged?.Invoke(SessionPhase.Closing);
            OnClosed?.Invoke(reason);
        }

        /// <summary>The roster entry for a player, connected or reserved, or null when there is none.</summary>
        public SessionMember FindMember(PlayerId playerId) {
            for (int memberIndex = 0; memberIndex < _members.Count; memberIndex++) {
                SessionMember candidate = _members[memberIndex];
                if (candidate.PlayerId == playerId) {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>The roster entry a peer is driving, or null when the peer is not a member here.</summary>
        public SessionMember FindMemberByPeer(PeerHandle peer) {
            if (!_playerByPeerId.TryGetValue(peer.Id, out PlayerId playerId)) {
                return null;
            }

            return FindMember(playerId);
        }

        /// <summary>True when the peer is attached to this session.</summary>
        public bool HasPeer(PeerHandle peer) {
            return _playerByPeerId.ContainsKey(peer.Id);
        }

        /// <summary>The roster as it goes on the wire.</summary>
        public LobbySnapshot BuildLobbySnapshot() {
            return new LobbySnapshot {
                SessionId = _sessionId,
                JoinCode = _joinCode,
                Phase = _phase,
                Members = new List<SessionMember>(_members)
            };
        }

        private SessionAttachResult AdmitNewMember(PeerHandle peer, PlayerIdentity identity) {
            if (_members.Count >= _profile.MaxPlayers) {
                return SessionAttachResult.Denied(SessionEndReason.Full);
            }

            if (_phase != SessionPhase.Lobby && !_profile.AllowJoinDuringMatch) {
                return SessionAttachResult.Denied(SessionEndReason.JoinRejectedMatchInProgress);
            }

            bool takesOwnership = !_ownerId.IsValid;
            SessionMember member = new SessionMember(peer.Id, identity.PlayerId, identity.DisplayName, takesOwnership, DefaultPartyId) {
                AvatarData = identity.AvatarData
            };
            _members.Add(member);
            _playerByPeerId[peer.Id] = identity.PlayerId;
            RebuildConnectedPeers();

            if (takesOwnership) {
                AssignOwner(identity.PlayerId, _members.Count > 1);
            }

            SendJoinAccepted(peer, false);
            AnnounceArrival(member, false);
            return SessionAttachResult.Accepted(member, false);
        }

        private SessionAttachResult ReclaimSeat(SessionMember member, PeerHandle peer, PlayerIdentity identity) {
            if (!_profile.AllowsRejoin()) {
                return SessionAttachResult.Denied(SessionEndReason.SessionNotFound);
            }

            member.PeerId = peer.Id;
            member.IsConnected = true;
            member.DisplayName = PlayerIdentity.Sanitize(identity.DisplayName);
            member.AvatarData = identity.AvatarData;
            _playerByPeerId[peer.Id] = member.PlayerId;
            _reservationSecondsLeft.Remove(member.PlayerId);
            RebuildConnectedPeers();

            RestoreMatchParticipation(member);

            if (!_ownerId.IsValid) {
                AssignOwner(member.PlayerId, true);
            }

            SendJoinAccepted(peer, true);
            AnnounceArrival(member, true);
            return SessionAttachResult.Accepted(member, true);
        }

        private void RestoreMatchParticipation(SessionMember member) {
            if (_currentMatch == null || _phase == SessionPhase.MatchResults) {
                return;
            }

            if (_currentMatch.HasParticipant(member.PlayerId)) {
                return;
            }

            _currentMatch.Participants.Add(member);
        }

        private void SendJoinAccepted(PeerHandle peer, bool isRejoin) {
            JoinAccepted accepted = new JoinAccepted {
                Config = _config,
                Lobby = BuildLobbySnapshot(),
                IsRejoin = isRejoin,
                Phase = _phase,
                MatchContext = ResolveJoinMatchContext()
            };

            _server.Send(peer, SessionMessageIds.JoinAccepted, in accepted, DeliveryClass.ReliableOrdered);
        }

        private MatchContextData ResolveJoinMatchContext() {
            if (_phase == SessionPhase.MatchLoading || _phase == SessionPhase.MatchActive) {
                return _currentMatch;
            }

            return null;
        }

        private void AnnounceArrival(SessionMember member, bool isRejoin) {
            MemberJoined joined = new MemberJoined(member, isRejoin);
            Broadcast(SessionMessageIds.MemberJoined, in joined);
            OnMemberJoined?.Invoke(member, isRejoin);

            // Fresh or reclaimed, the arriving client's world is empty. Replication owes it a keyframe
            // before any delta snapshot can mean anything, and this is the only place that knows.
            OnMemberNeedsKeyframe?.Invoke(member);
        }

        private void RetireMember(SessionMember member, PlayerId playerId, LeaveReason reason) {
            member.IsConnected = false;
            member.PeerId = SessionMember.NoPeerId;
            _readyPlayers.Remove(playerId);
            PruneMatchParticipant(playerId);

            if (ShouldReserveSeat(reason)) {
                _reservationSecondsLeft[playerId] = _profile.ResolveRejoinWindowSeconds();
                RebuildConnectedPeers();
                return;
            }

            member.IsOwner = false;
            _members.Remove(member);
            _reservationSecondsLeft.Remove(playerId);
            RebuildConnectedPeers();
        }

        private bool ShouldReserveSeat(LeaveReason reason) {
            if (reason != LeaveReason.TransportLost) {
                return false;
            }

            if (_phase == SessionPhase.Closing || _isClosed) {
                return false;
            }

            return _profile.AllowsRejoin();
        }

        private void AnnounceDeparture(PlayerId playerId, SessionMember member, LeaveReason reason) {
            MemberLeft left = new MemberLeft(playerId, reason);
            Broadcast(SessionMessageIds.MemberLeft, in left);
            OnMemberLeft?.Invoke(member, reason);
        }

        private void HandleOwnerDeparture(PlayerId departedPlayerId) {
            if (_ownerId != departedPlayerId) {
                return;
            }

            SessionMember previous = FindMember(departedPlayerId);
            if (previous != null) {
                previous.IsOwner = false;
            }

            _ownerId = PlayerId.None;

            if (_profile.HostPolicy == HostPolicy.EndSession) {
                Close(SessionEndReason.HostClosed);
                return;
            }

            SessionMember successor = FindFirstConnectedMember();
            if (successor == null) {
                return;
            }

            AssignOwner(successor.PlayerId, true);
        }

        private void AssignOwner(PlayerId playerId, bool announce) {
            _ownerId = playerId;
            SessionMember member = FindMember(playerId);
            if (member != null) {
                member.IsOwner = true;
            }

            OnOwnerChanged?.Invoke(playerId);

            if (!announce) {
                return;
            }

            OwnerChanged changed = new OwnerChanged(playerId);
            Broadcast(SessionMessageIds.OwnerChanged, in changed);
        }

        private bool IsLaunchAuthorized(PlayerId requester) {
            if (!requester.IsValid) {
                // No requester means the server itself asked; there is nobody to check against.
                return true;
            }

            if (!_lobby.OwnerLaunchesMatches) {
                return true;
            }

            return requester == _ownerId;
        }

        private bool TryLaunchDefinition(MatchDefinitionData definition, out string failReason) {
            failReason = string.Empty;
            int participantCount = CountConnectedMembers();

            if (participantCount < definition.MinPlayers) {
                failReason = "This match needs at least " + definition.MinPlayers.ToString() + " players.";
                return false;
            }

            if (definition.MaxPlayers > 0 && participantCount > definition.MaxPlayers) {
                failReason = "This match takes at most " + definition.MaxPlayers.ToString() + " players.";
                return false;
            }

            BeginMatchLoading(definition);
            return true;
        }

        private void BeginMatchLoading(MatchDefinitionData definition) {
            _matchSequence++;
            _currentDefinition = definition;
            _currentMatch = BuildMatchContext(definition);
            _readyPlayers.Clear();
            _readyElapsedSeconds = 0f;
            _matchElapsedSeconds = 0f;

            MatchLoad load = new MatchLoad(_currentMatch);
            Broadcast(SessionMessageIds.MatchLoad, in load);
            SetPhase(SessionPhase.MatchLoading);
        }

        private MatchContextData BuildMatchContext(MatchDefinitionData definition) {
            return new MatchContextData {
                MatchId = definition.MatchId,
                SceneName = definition.SceneName,
                MatchSequence = _matchSequence,
                Participants = CollectConnectedMembers()
            };
        }

        private void EvaluateReadyBarrier() {
            if (_phase != SessionPhase.MatchLoading || _currentMatch == null) {
                return;
            }

            if (_currentMatch.Participants.Count == 0) {
                AbortMatch();
                return;
            }

            if (!AreAllParticipantsReady()) {
                return;
            }

            StartMatch();
        }

        private bool AreAllParticipantsReady() {
            List<SessionMember> participants = _currentMatch.Participants;
            for (int participantIndex = 0; participantIndex < participants.Count; participantIndex++) {
                if (!_readyPlayers.Contains(participants[participantIndex].PlayerId)) {
                    return false;
                }
            }

            return true;
        }

        private void StartMatch() {
            if (_phase != SessionPhase.MatchLoading) {
                return;
            }

            MatchStart start = new MatchStart();
            Broadcast(SessionMessageIds.MatchStart, in start);
            SetPhase(SessionPhase.MatchActive);
            OnMatchActive?.Invoke(_currentMatch);
        }

        private void AbortMatch() {
            if (_phase != SessionPhase.MatchLoading) {
                return;
            }

            EnterLobbyPhase();
        }

        private void EnterLobbyPhase() {
            _currentMatch = null;
            _currentDefinition = null;
            _readyPlayers.Clear();
            _matchElapsedSeconds = 0f;
            _resultsElapsedSeconds = 0f;

            ReturnToLobby returnToLobby = new ReturnToLobby();
            Broadcast(SessionMessageIds.ReturnToLobby, in returnToLobby);
            SetPhase(SessionPhase.Lobby);
        }

        private MatchResultData BuildDefaultResult() {
            return new MatchResultData {
                MatchId = _currentMatch == null ? string.Empty : _currentMatch.MatchId,
                MatchSequence = _matchSequence,
                Payload = Array.Empty<byte>()
            };
        }

        private void TickPhase(float deltaSeconds) {
            if (_phase == SessionPhase.MatchLoading) {
                TickMatchLoading(deltaSeconds);
                return;
            }

            if (_phase == SessionPhase.MatchActive) {
                TickMatchActive(deltaSeconds);
                return;
            }

            if (_phase == SessionPhase.MatchResults) {
                TickMatchResults(deltaSeconds);
            }
        }

        private void TickMatchLoading(float deltaSeconds) {
            _readyElapsedSeconds += deltaSeconds;
            if (_profile.ReadyTimeoutSeconds <= 0f || _readyElapsedSeconds < _profile.ReadyTimeoutSeconds) {
                return;
            }

            ResolveStragglers();
        }

        /// <summary>
        /// The barrier expired. Whoever has not reported in is removed from the match — the rest of the
        /// party has already waited the whole timeout, and one slow loader must not hold them forever.
        /// </summary>
        private void ResolveStragglers() {
            CollectStragglers();

            // Participation is revoked for all of them first: dropping one at a time would let the
            // barrier clear halfway through the list and start the match under our feet.
            for (int stragglerIndex = 0; stragglerIndex < _stragglerScratch.Count; stragglerIndex++) {
                _currentMatch.Participants.Remove(_stragglerScratch[stragglerIndex]);
            }

            for (int stragglerIndex = 0; stragglerIndex < _stragglerScratch.Count; stragglerIndex++) {
                DropStraggler(_stragglerScratch[stragglerIndex]);
            }

            if (_currentMatch != null && _currentMatch.Participants.Count == 0) {
                AbortMatch();
                return;
            }

            StartMatch();
        }

        private void CollectStragglers() {
            _stragglerScratch.Clear();
            List<SessionMember> participants = _currentMatch.Participants;
            for (int participantIndex = 0; participantIndex < participants.Count; participantIndex++) {
                SessionMember participant = participants[participantIndex];
                if (!_readyPlayers.Contains(participant.PlayerId)) {
                    _stragglerScratch.Add(participant);
                }
            }
        }

        private void DropStraggler(SessionMember straggler) {
            if (_profile.LateLoadPolicy == LateLoadPolicy.Disconnect) {
                Kick(straggler.PlayerId, "Did not finish loading the match in time.");
                return;
            }

            // DropToLobby: the straggler stays a member of the igloo, it just misses this match.
            if (!straggler.IsConnected) {
                return;
            }

            ReturnToLobby returnToLobby = new ReturnToLobby();
            _server.Send(new PeerHandle(straggler.PeerId), SessionMessageIds.ReturnToLobby, in returnToLobby, DeliveryClass.ReliableOrdered);
        }

        private void TickMatchActive(float deltaSeconds) {
            _matchElapsedSeconds += deltaSeconds;
            if (_currentDefinition == null || !_currentDefinition.HasTimeLimit()) {
                return;
            }

            if (_matchElapsedSeconds < _currentDefinition.MaxDurationSeconds) {
                return;
            }

            EndMatch(null);
        }

        private void TickMatchResults(float deltaSeconds) {
            _resultsElapsedSeconds += deltaSeconds;
            if (_resultsElapsedSeconds < _profile.ResultsHoldSeconds) {
                return;
            }

            if (_profile.LifetimeMode == SessionLifetimeMode.MatchScoped) {
                Close(SessionEndReason.HostClosed);
                return;
            }

            EnterLobbyPhase();
        }

        private void TickLifetime(float deltaSeconds) {
            if (_profile.LifetimeMode == SessionLifetimeMode.LongLived) {
                return;
            }

            if (CountConnectedMembers() > 0) {
                _emptyElapsedSeconds = 0f;
                return;
            }

            _emptyElapsedSeconds += deltaSeconds;
            if (_emptyElapsedSeconds < _profile.EmptyShutdownSeconds) {
                return;
            }

            Close(SessionEndReason.HostClosed);
        }

        private void TickReservations(float deltaSeconds) {
            if (_reservationSecondsLeft.Count == 0) {
                return;
            }

            _reservationScratch.Clear();
            _reservationScratch.AddRange(_reservationSecondsLeft.Keys);

            for (int reservationIndex = 0; reservationIndex < _reservationScratch.Count; reservationIndex++) {
                AgeReservation(_reservationScratch[reservationIndex], deltaSeconds);
            }
        }

        private void AgeReservation(PlayerId playerId, float deltaSeconds) {
            if (!_reservationSecondsLeft.TryGetValue(playerId, out float remainingSeconds)) {
                return;
            }

            // An AnyTime policy resolves to positive infinity, which never counts down to zero — the
            // seat is held until the session itself ends.
            float left = remainingSeconds - deltaSeconds;
            if (left > 0f) {
                _reservationSecondsLeft[playerId] = left;
                return;
            }

            _reservationSecondsLeft.Remove(playerId);
            ExpireReservation(playerId);
        }

        private void ExpireReservation(PlayerId playerId) {
            SessionMember member = FindMember(playerId);
            if (member == null || member.IsConnected) {
                return;
            }

            _members.Remove(member);
            PruneMatchParticipant(playerId);
            RebuildConnectedPeers();

            // No MemberLeft goes out: everyone was told when the link dropped, and the roster they hold
            // already shows this player gone. The reservation was a server-side promise, not a member.
            HandleOwnerDeparture(playerId);
        }

        private void PruneMatchParticipant(PlayerId playerId) {
            if (_currentMatch == null) {
                return;
            }

            for (int participantIndex = _currentMatch.Participants.Count - 1; participantIndex >= 0; participantIndex--) {
                if (_currentMatch.Participants[participantIndex].PlayerId == playerId) {
                    _currentMatch.Participants.RemoveAt(participantIndex);
                }
            }
        }

        private void SetPhase(SessionPhase phase) {
            if (_phase == phase) {
                return;
            }

            _phase = phase;
            PhaseChanged changed = new PhaseChanged(phase);
            Broadcast(SessionMessageIds.PhaseChanged, in changed);
            OnPhaseChanged?.Invoke(phase);
        }

        private void Broadcast<TMessage>(ushort messageId, in TMessage message) where TMessage : struct, INetMessage {
            _server.SendToMany(_connectedPeers, messageId, in message, DeliveryClass.ReliableOrdered);
        }

        private void RebuildConnectedPeers() {
            _connectedPeers.Clear();
            for (int memberIndex = 0; memberIndex < _members.Count; memberIndex++) {
                SessionMember member = _members[memberIndex];
                if (member.IsConnected) {
                    _connectedPeers.Add(new PeerHandle(member.PeerId));
                }
            }
        }

        private int CountConnectedMembers() {
            int count = 0;
            for (int memberIndex = 0; memberIndex < _members.Count; memberIndex++) {
                if (_members[memberIndex].IsConnected) {
                    count++;
                }
            }

            return count;
        }

        private List<SessionMember> CollectConnectedMembers() {
            List<SessionMember> connected = new List<SessionMember>(_members.Count);
            for (int memberIndex = 0; memberIndex < _members.Count; memberIndex++) {
                SessionMember member = _members[memberIndex];
                if (member.IsConnected) {
                    connected.Add(member);
                }
            }

            return connected;
        }

        private SessionMember FindFirstConnectedMember() {
            for (int memberIndex = 0; memberIndex < _members.Count; memberIndex++) {
                SessionMember candidate = _members[memberIndex];
                if (candidate.IsConnected) {
                    return candidate;
                }
            }

            return null;
        }

        private void ClearSessionState() {
            _readyPlayers.Clear();
            _reservationSecondsLeft.Clear();
            _reservationScratch.Clear();
            _stragglerScratch.Clear();
            _playerByPeerId.Clear();
            _members.Clear();
            _connectedPeers.Clear();
            _currentMatch = null;
            _currentDefinition = null;
            _ownerId = PlayerId.None;
        }
    }
}
