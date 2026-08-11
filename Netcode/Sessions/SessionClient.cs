using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Netcode.Sessions.Messages;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// The player's side of a session: it dials the server, proves who the player is, asks for a session
    /// to be created or joined, and then keeps a mirror of the roster, the phase and the current match.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The journey is deliberately in two halves. <see cref="ConnectAsync(NetEndpoint)"/> gets as far as
    /// authenticated-and-attached-to-nothing; only then does
    /// <see cref="CreateSessionAsync(string)"/> or <see cref="JoinSessionAsync(string)"/> pick a session.
    /// That split is what lets a join code select a session rather than encode an address, and what lets
    /// a client bounce between sessions on one connection later without a second handshake.
    /// </para>
    /// <para>
    /// <b>Rejoining is just joining.</b> The player id is persisted by the caller and sent again in the
    /// join request; if the server is still holding a reservation under it, the same call comes back with
    /// <see cref="SessionJoinResult.IsRejoin"/> set and a match context to load into.
    /// </para>
    /// <para>
    /// <b>Threading.</b> Every event fires on the thread that calls <see cref="Tick"/>, from inside the
    /// transport poll. The tasks handed back complete from there too, with their continuations forced
    /// asynchronous so an awaiting caller can never end up running its own code inside the poll.
    /// </para>
    /// </remarks>
    public sealed class SessionClient {
        /// <summary>
        /// Grace between announcing a leave and closing the socket. Closing discards whatever is still
        /// queued, so a notice sent and immediately followed by a disconnect would usually never arrive —
        /// and the server would reserve a rejoin seat for a player who meant to quit.
        /// </summary>
        private const float LeaveGraceSeconds = 0.1f;

        private readonly NetClient _client;
        private readonly IAuthProvider _authProvider;
        private readonly PlayerIdentity _identity;
        private readonly List<SessionMember> _members = new List<SessionMember>();

        private TaskCompletionSource<SessionJoinResult> _connectCompletion;
        private TaskCompletionSource<SessionJoinResult> _attachCompletion;
        private TaskCompletionSource<bool> _leaveCompletion;
        private CancellationTokenRegistration _connectRegistration;
        private CancellationTokenRegistration _attachRegistration;

        private ClientSessionState _state = ClientSessionState.Offline;
        private SessionConfigData _serverConfig;
        private MatchContextData _currentMatch;
        private SessionPhase _phase = SessionPhase.Lobby;
        private string _sessionId = string.Empty;
        private string _joinCode = string.Empty;
        private float _leaveGraceLeftSeconds;
        private bool _isLeavePending;

        public SessionClient(NetClient client, IAuthProvider authProvider, PlayerIdentity identity) {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _authProvider = authProvider ?? throw new ArgumentNullException(nameof(authProvider));
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));

            _client.OnConnected += HandleTransportConnected;
            _client.OnDisconnected += HandleTransportDisconnected;

            RegisterHandlers();
        }

        /// <summary>The client moved along its session lifecycle.</summary>
        public event Action<ClientSessionState> OnStateChanged;

        /// <summary>Someone joined the session. The flag marks a rejoin rather than a new arrival.</summary>
        public event Action<SessionMember, bool> OnMemberJoined;

        /// <summary>Someone left the session, or lost their link.</summary>
        public event Action<SessionMember, LeaveReason> OnMemberLeft;

        /// <summary>The session changed phase.</summary>
        public event Action<SessionPhase> OnPhaseChanged;

        /// <summary>A match is loading — the scene named in the context should be brought up now.</summary>
        public event Action<MatchContextData> OnMatchLoading;

        /// <summary>Everyone is loaded and the match is live.</summary>
        public event Action<MatchContextData> OnMatchActive;

        /// <summary>The match finished; results are being held before the return to the lobby.</summary>
        public event Action<MatchResultData> OnMatchEnded;

        /// <summary>Back to the igloo, either after results or because this client was left behind.</summary>
        public event Action OnReturnedToLobby;

        /// <summary>Ownership of the session moved to this player id.</summary>
        public event Action<PlayerId> OnOwnerChanged;

        /// <summary>A launch request was refused, with the server's explanation.</summary>
        public event Action<string> OnLaunchDenied;

        /// <summary>The session ended for this client: kicked, closed, or the link went away.</summary>
        public event Action<SessionEndReason, string> OnSessionEnded;

        /// <summary>Where the client sits between offline and in-session.</summary>
        public ClientSessionState State => _state;

        /// <summary>The roster as last reported by the server, in join order.</summary>
        public IReadOnlyList<SessionMember> Members => _members;

        /// <summary>This player's roster entry, or null before the session is joined.</summary>
        public SessionMember LocalMember => FindMember(_identity.PlayerId);

        /// <summary>The identity this client authenticates as. Persisting its id is what enables rejoin.</summary>
        public PlayerIdentity Identity => _identity;

        /// <summary>True when this player currently owns the session.</summary>
        public bool IsOwner {
            get {
                SessionMember member = LocalMember;
                return member != null && member.IsOwner;
            }
        }

        /// <summary>Configuration the server handed over on join.</summary>
        public SessionConfigData ServerConfig => _serverConfig;

        /// <summary>The phase the session was last reported to be in.</summary>
        public SessionPhase Phase => _phase;

        /// <summary>The match being loaded or played, or null in lobby.</summary>
        public MatchContextData CurrentMatch => _currentMatch;

        /// <summary>Identifier of the joined session, or empty.</summary>
        public string SessionId => _sessionId;

        /// <summary>Code friends can use to join the session, or empty.</summary>
        public string JoinCode => _joinCode;

        /// <summary>True once the handshake has cleared and a session may be created or joined.</summary>
        public bool IsAuthenticated {
            get { return _state == ClientSessionState.Authenticated || _state == ClientSessionState.Joining || _state == ClientSessionState.InSession; }
        }

        /// <summary>Dials the server and authenticates. Resolves attached to no session yet.</summary>
        public Task<SessionJoinResult> ConnectAsync(NetEndpoint endpoint) {
            return ConnectAsync(endpoint, CancellationToken.None);
        }

        /// <inheritdoc cref="ConnectAsync(NetEndpoint)" />
        public Task<SessionJoinResult> ConnectAsync(NetEndpoint endpoint, CancellationToken cancellationToken) {
            if (_state != ClientSessionState.Offline && _state != ClientSessionState.Failed) {
                throw new InvalidOperationException("SessionClient cannot connect while " + _state.ToString() + ".");
            }

            ResetSessionState();
            _connectCompletion = new TaskCompletionSource<SessionJoinResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _connectRegistration = cancellationToken.Register(CancelConnect);
            SetState(ClientSessionState.Connecting);
            _client.Connect(endpoint);
            return _connectCompletion.Task;
        }

        /// <summary>Asks the server to stand up a new session with this player as its owner.</summary>
        public Task<SessionJoinResult> CreateSessionAsync(string profileId) {
            return CreateSessionAsync(profileId, CancellationToken.None);
        }

        /// <inheritdoc cref="CreateSessionAsync(string)" />
        public Task<SessionJoinResult> CreateSessionAsync(string profileId, CancellationToken cancellationToken) {
            RequireAuthenticated();
            BeginAttach(cancellationToken);

            CreateSessionRequest request = new CreateSessionRequest(profileId ?? string.Empty);
            _client.Send(SessionMessageIds.CreateSessionRequest, in request, DeliveryClass.ReliableOrdered);
            return _attachCompletion.Task;
        }

        /// <summary>
        /// Asks to join the session a code selects. The persisted player id rides along, which is what
        /// turns this same call into a rejoin when the server is holding a seat under it.
        /// </summary>
        public Task<SessionJoinResult> JoinSessionAsync(string joinCode) {
            return JoinSessionAsync(joinCode, CancellationToken.None);
        }

        /// <inheritdoc cref="JoinSessionAsync(string)" />
        public Task<SessionJoinResult> JoinSessionAsync(string joinCode, CancellationToken cancellationToken) {
            RequireAuthenticated();
            BeginAttach(cancellationToken);

            JoinSessionRequest request = new JoinSessionRequest(NormalizeJoinCode(joinCode), _identity.PlayerId);
            _client.Send(SessionMessageIds.JoinSessionRequest, in request, DeliveryClass.ReliableOrdered);
            return _attachCompletion.Task;
        }

        /// <summary>One pump of the connection, plus the leave grace. Call once per frame.</summary>
        public void Tick(float deltaSeconds) {
            _client.Update(deltaSeconds);
            TickLeaveGrace(deltaSeconds);
        }

        /// <summary>Announces the leave, then closes the link once the notice has had time to fly.</summary>
        public Task LeaveAsync() {
            if (_state == ClientSessionState.Offline) {
                return Task.CompletedTask;
            }

            _leaveCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            if (_client.IsConnected) {
                LeaveNotice notice = new LeaveNotice();
                _client.Send(SessionMessageIds.LeaveNotice, in notice, DeliveryClass.ReliableOrdered);
            }

            _isLeavePending = true;
            _leaveGraceLeftSeconds = LeaveGraceSeconds;
            SetState(ClientSessionState.Leaving);
            return _leaveCompletion.Task;
        }

        /// <summary>Asks the owner's privilege of launching a match. Refusals arrive as OnLaunchDenied.</summary>
        public void RequestLaunchMatch(string matchId) {
            if (_state != ClientSessionState.InSession) {
                return;
            }

            LaunchMatchRequest request = new LaunchMatchRequest(matchId ?? string.Empty);
            _client.Send(SessionMessageIds.LaunchMatchRequest, in request, DeliveryClass.ReliableOrdered);
        }

        /// <summary>Reports that this client finished loading the given match. Clears the ready barrier.</summary>
        public void NotifyClientReady(int matchSequence) {
            if (_state != ClientSessionState.InSession) {
                return;
            }

            ClientReady ready = new ClientReady(matchSequence);
            _client.Send(SessionMessageIds.ClientReady, in ready, DeliveryClass.ReliableOrdered);
        }

        /// <summary>The roster entry for a player, or null when there is none.</summary>
        public SessionMember FindMember(PlayerId playerId) {
            for (int memberIndex = 0; memberIndex < _members.Count; memberIndex++) {
                SessionMember candidate = _members[memberIndex];
                if (candidate.PlayerId == playerId) {
                    return candidate;
                }
            }

            return null;
        }

        private void RegisterHandlers() {
            _client.Router.Register<AuthResponse>(SessionMessageIds.AuthResponse, HandleAuthResponse);
            _client.Router.Register<SessionCreated>(SessionMessageIds.SessionCreated, HandleSessionCreated);
            _client.Router.Register<JoinAccepted>(SessionMessageIds.JoinAccepted, HandleJoinAccepted);
            _client.Router.Register<JoinSessionDenied>(SessionMessageIds.JoinSessionDenied, HandleJoinSessionDenied);
            _client.Router.Register<MemberJoined>(SessionMessageIds.MemberJoined, HandleMemberJoined);
            _client.Router.Register<MemberLeft>(SessionMessageIds.MemberLeft, HandleMemberLeft);
            _client.Router.Register<PhaseChanged>(SessionMessageIds.PhaseChanged, HandlePhaseChanged);
            _client.Router.Register<LaunchMatchDenied>(SessionMessageIds.LaunchMatchDenied, HandleLaunchMatchDenied);
            _client.Router.Register<MatchLoad>(SessionMessageIds.MatchLoad, HandleMatchLoad);
            _client.Router.Register<MatchStart>(SessionMessageIds.MatchStart, HandleMatchStart);
            _client.Router.Register<MatchEnd>(SessionMessageIds.MatchEnd, HandleMatchEnd);
            _client.Router.Register<ReturnToLobby>(SessionMessageIds.ReturnToLobby, HandleReturnToLobby);
            _client.Router.Register<Kick>(SessionMessageIds.Kick, HandleKick);
            _client.Router.Register<SessionClosing>(SessionMessageIds.SessionClosing, HandleSessionClosing);
            _client.Router.Register<OwnerChanged>(SessionMessageIds.OwnerChanged, HandleOwnerChanged);
        }

        private void HandleTransportConnected() {
            SetState(ClientSessionState.Authenticating);
            BeginCredentialAcquisition();
        }

        private void BeginCredentialAcquisition() {
            PendingCredentials pending = new PendingCredentials(this);

            try {
                pending.Begin(_authProvider.AcquireAsync(_identity, CancellationToken.None));
            }
            catch (Exception) {
                FailConnect(SessionEndReason.AuthRejected, "Could not acquire credentials.");
            }
        }

        private void PostCompletion(Action completion) {
            _client.Inbox.Post(completion);
        }

        private void OnCredentialsReady(Task<AuthCredentials> acquisition) {
            if (acquisition.IsFaulted || acquisition.IsCanceled) {
                FailConnect(SessionEndReason.AuthRejected, "Could not acquire credentials.");
                return;
            }

            AuthCredentials credentials = acquisition.Result ?? AuthCredentials.Anonymous();
            AuthRequest request = new AuthRequest(_identity, credentials.Token);
            request.AuthMethod = credentials.Method;
            _client.Send(SessionMessageIds.AuthRequest, in request, DeliveryClass.ReliableOrdered);
        }

        private void HandleTransportDisconnected(DisconnectReason reason) {
            bool wasInSession = _state == ClientSessionState.InSession;
            bool wasLeaving = _state == ClientSessionState.Leaving;

            _isLeavePending = false;
            SetState(ClientSessionState.Offline);
            FailPendingOperations(SessionEndReason.TransportLost, "The connection ended.");
            CompleteLeave();

            if (!wasInSession || wasLeaving) {
                return;
            }

            OnSessionEnded?.Invoke(SessionEndReason.TransportLost, "The connection ended.");
        }

        private void HandleAuthResponse(in AuthResponse message, PeerHandle sender) {
            if (!message.Accepted) {
                FailConnect(SessionEndReason.AuthRejected, message.Reason);
                return;
            }

            SetState(ClientSessionState.Authenticated);
            CompleteConnect(SessionJoinResult.Connected());
        }

        private void HandleSessionCreated(in SessionCreated message, PeerHandle sender) {
            _sessionId = message.SessionId;
            _joinCode = message.JoinCode;
        }

        private void HandleJoinAccepted(in JoinAccepted message, PeerHandle sender) {
            _serverConfig = message.Config;
            ApplyLobbySnapshot(message.Lobby);
            _phase = message.Phase;
            _currentMatch = message.MatchContext;
            SetState(ClientSessionState.InSession);

            CompleteAttach(SessionJoinResult.Joined(_sessionId, _joinCode, _phase, message.IsRejoin));
            RaiseRejoinMatchEvent(message.MatchContext, message.Phase);
        }

        private void RaiseRejoinMatchEvent(MatchContextData matchContext, SessionPhase phase) {
            if (matchContext == null) {
                return;
            }

            if (phase == SessionPhase.MatchActive) {
                OnMatchActive?.Invoke(matchContext);
                return;
            }

            OnMatchLoading?.Invoke(matchContext);
        }

        private void HandleJoinSessionDenied(in JoinSessionDenied message, PeerHandle sender) {
            SetState(ClientSessionState.Authenticated);
            CompleteAttach(SessionJoinResult.Denied(message.Reason, string.Empty));
        }

        private void HandleMemberJoined(in MemberJoined message, PeerHandle sender) {
            SessionMember member = UpsertMember(message.Member);
            OnMemberJoined?.Invoke(member, message.IsRejoin);
        }

        private void HandleMemberLeft(in MemberLeft message, PeerHandle sender) {
            SessionMember member = FindMember(message.PlayerId);
            if (member == null) {
                return;
            }

            _members.Remove(member);
            OnMemberLeft?.Invoke(member, message.LeaveReason);
        }

        private void HandlePhaseChanged(in PhaseChanged message, PeerHandle sender) {
            _phase = message.Phase;
            OnPhaseChanged?.Invoke(message.Phase);
        }

        private void HandleLaunchMatchDenied(in LaunchMatchDenied message, PeerHandle sender) {
            OnLaunchDenied?.Invoke(message.Reason);
        }

        private void HandleMatchLoad(in MatchLoad message, PeerHandle sender) {
            _currentMatch = message.MatchContext;
            OnMatchLoading?.Invoke(message.MatchContext);
        }

        private void HandleMatchStart(in MatchStart message, PeerHandle sender) {
            OnMatchActive?.Invoke(_currentMatch);
        }

        private void HandleMatchEnd(in MatchEnd message, PeerHandle sender) {
            OnMatchEnded?.Invoke(message.Result);
        }

        private void HandleReturnToLobby(in ReturnToLobby message, PeerHandle sender) {
            _currentMatch = null;
            OnReturnedToLobby?.Invoke();
        }

        private void HandleKick(in Kick message, PeerHandle sender) {
            OnSessionEnded?.Invoke(SessionEndReason.Kicked, message.Reason);
        }

        private void HandleSessionClosing(in SessionClosing message, PeerHandle sender) {
            OnSessionEnded?.Invoke(message.Reason, string.Empty);
        }

        private void HandleOwnerChanged(in OwnerChanged message, PeerHandle sender) {
            ApplyOwner(message.PlayerId);
            OnOwnerChanged?.Invoke(message.PlayerId);
        }

        private void ApplyOwner(PlayerId ownerId) {
            for (int memberIndex = 0; memberIndex < _members.Count; memberIndex++) {
                SessionMember member = _members[memberIndex];
                member.IsOwner = member.PlayerId == ownerId;
            }
        }

        private void ApplyLobbySnapshot(LobbySnapshot snapshot) {
            _members.Clear();
            if (snapshot == null) {
                return;
            }

            _sessionId = snapshot.SessionId;
            _joinCode = snapshot.JoinCode;
            if (snapshot.Members == null) {
                return;
            }

            _members.AddRange(snapshot.Members);
        }

        private SessionMember UpsertMember(SessionMember incoming) {
            if (incoming == null) {
                return null;
            }

            SessionMember existing = FindMember(incoming.PlayerId);
            if (existing == null) {
                _members.Add(incoming);
                return incoming;
            }

            existing.PeerId = incoming.PeerId;
            existing.DisplayName = incoming.DisplayName;
            existing.IsOwner = incoming.IsOwner;
            existing.IsConnected = incoming.IsConnected;
            existing.PartyId = incoming.PartyId;
            existing.AvatarData = incoming.AvatarData;
            return existing;
        }

        private void TickLeaveGrace(float deltaSeconds) {
            if (!_isLeavePending) {
                return;
            }

            _leaveGraceLeftSeconds -= deltaSeconds;
            if (_leaveGraceLeftSeconds > 0f) {
                return;
            }

            _isLeavePending = false;
            _client.Disconnect();
        }

        private void BeginAttach(CancellationToken cancellationToken) {
            _attachCompletion = new TaskCompletionSource<SessionJoinResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _attachRegistration = cancellationToken.Register(CancelAttach);
            SetState(ClientSessionState.Joining);
        }

        private void RequireAuthenticated() {
            if (_state == ClientSessionState.Authenticated) {
                return;
            }

            throw new InvalidOperationException("SessionClient must be authenticated before attaching to a session; it is " + _state.ToString() + ".");
        }

        private void CancelConnect() {
            _connectCompletion?.TrySetCanceled();
        }

        private void CancelAttach() {
            _attachCompletion?.TrySetCanceled();
        }

        private void CompleteConnect(SessionJoinResult result) {
            _connectRegistration.Dispose();
            TaskCompletionSource<SessionJoinResult> completion = _connectCompletion;
            _connectCompletion = null;
            completion?.TrySetResult(result);
        }

        private void CompleteAttach(SessionJoinResult result) {
            _attachRegistration.Dispose();
            TaskCompletionSource<SessionJoinResult> completion = _attachCompletion;
            _attachCompletion = null;
            completion?.TrySetResult(result);
        }

        private void CompleteLeave() {
            TaskCompletionSource<bool> completion = _leaveCompletion;
            _leaveCompletion = null;
            completion?.TrySetResult(true);
        }

        private void FailConnect(SessionEndReason reason, string message) {
            SetState(ClientSessionState.Failed);
            CompleteConnect(SessionJoinResult.Denied(reason, message));
            CompleteAttach(SessionJoinResult.Denied(reason, message));
        }

        private void FailPendingOperations(SessionEndReason reason, string message) {
            CompleteConnect(SessionJoinResult.Denied(reason, message));
            CompleteAttach(SessionJoinResult.Denied(reason, message));
        }

        private void ResetSessionState() {
            _members.Clear();
            _serverConfig = null;
            _currentMatch = null;
            _phase = SessionPhase.Lobby;
            _sessionId = string.Empty;
            _joinCode = string.Empty;
            _isLeavePending = false;
        }

        private void SetState(ClientSessionState state) {
            if (_state == state) {
                return;
            }

            _state = state;
            OnStateChanged?.Invoke(state);
        }

        private static string NormalizeJoinCode(string joinCode) {
            if (JoinCodeGenerator.TryNormalize(joinCode, out string normalized)) {
                return normalized;
            }

            // A code that does not normalize is sent as typed: the server owns the verdict, and a client
            // that silently swallowed a typo would leave the player staring at nothing happening.
            return joinCode ?? string.Empty;
        }

        /// <summary>
        /// One credential acquisition in flight. It exists so the hop from the provider's thread back to
        /// the frame thread is two plain method groups instead of closures over client state.
        /// </summary>
        private sealed class PendingCredentials {
            private readonly SessionClient _owner;

            private Task<AuthCredentials> _acquisition;

            public PendingCredentials(SessionClient owner) {
                _owner = owner;
            }

            /// <summary>Attaches to the provider's task. Nothing about it is awaited.</summary>
            public void Begin(Task<AuthCredentials> acquisition) {
                _acquisition = acquisition ?? Task.FromResult(AuthCredentials.Anonymous());
                _acquisition.ContinueWith(Publish, TaskContinuationOptions.ExecuteSynchronously);
            }

            private void Publish(Task<AuthCredentials> completed) {
                _owner.PostCompletion(Deliver);
            }

            private void Deliver() {
                _owner.OnCredentialsReady(_acquisition);
            }
        }
    }
}
