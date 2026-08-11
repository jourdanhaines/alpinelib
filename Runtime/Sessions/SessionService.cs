using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Collision;
using AlpineLib.DI;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Transport;
using AlpineLib.Networking;
using UnityEngine;

namespace AlpineLib.Sessions {
    /// <summary>
    /// The game's whole relationship with a session: hosting one, joining one by code, leaving, and
    /// following what the server says is happening inside it.
    /// </summary>
    /// <remarks>
    /// Everything above this service — menus, scene flow, HUD — talks to it rather than to the netcode
    /// session client, so the difference between a dedicated server, a listen host and no networking at
    /// all stays inside this one object. Offline is a supported resting state: every accessor answers,
    /// every call is a no-op, and nothing throws.
    /// </remarks>
    public interface ISessionService : IDependencyProvider {
        /// <summary>Where the local client sits in the connect, authenticate, attach handshake.</summary>
        ClientSessionState State { get; }

        /// <summary>What the session is currently doing, or <c>Lobby</c> while there is none.</summary>
        SessionPhase Phase { get; }

        /// <summary>Code a friend types to reach the current session, or empty when there is none.</summary>
        string CurrentJoinCode { get; }

        /// <summary>
        /// Server-side id of the current session, or empty when there is none.
        /// </summary>
        /// <remarks>
        /// The id, not the join code, is what server-side scopes are keyed by — chat rooms above all —
        /// because a code is a human-facing selector that a session may outlive.
        /// </remarks>
        string SessionId { get; }

        /// <summary>The current roster, empty outside a session.</summary>
        IReadOnlyList<SessionMember> Members { get; }

        /// <summary>True while attached to a session.</summary>
        bool IsInSession { get; }

        /// <summary>True while the local player owns the current session.</summary>
        bool IsOwner { get; }

        /// <summary>The local player's persistent identity.</summary>
        PlayerIdentity Identity { get; }

        /// <summary>The authored configuration this service was handed, or null while unconfigured.</summary>
        SessionConfig Config { get; }

        /// <summary>The replicated client world, or null outside a session.</summary>
        ClientReplication Replication { get; }

        /// <summary>The match currently loading or running, or null in a lobby.</summary>
        MatchContextData CurrentMatch { get; }

        /// <summary>Raised whenever <see cref="State"/> changes.</summary>
        event Action<ClientSessionState> OnStateChanged;

        /// <summary>Raised for every member that arrives; the flag is true for a rejoin.</summary>
        event Action<SessionMember, bool> OnMemberJoined;

        /// <summary>Raised for every member that leaves, with the reason.</summary>
        event Action<SessionMember, LeaveReason> OnMemberLeft;

        /// <summary>Raised whenever the session's phase advances.</summary>
        event Action<SessionPhase> OnPhaseChanged;

        /// <summary>Raised when a match is announced and its scene must be loaded.</summary>
        event Action<MatchContextData> OnMatchLoading;

        /// <summary>Raised when every participant is ready and the match begins.</summary>
        event Action<MatchContextData> OnMatchActive;

        /// <summary>Raised when a match finishes, with its results.</summary>
        event Action<MatchResultData> OnMatchEnded;

        /// <summary>Raised when the session leaves its results screen for the lobby.</summary>
        event Action OnReturnedToLobby;

        /// <summary>Raised when ownership of the session moves to another member.</summary>
        event Action<PlayerId> OnOwnerChanged;

        /// <summary>Raised when a launch request is refused, with the server's reason.</summary>
        event Action<string> OnLaunchDenied;

        /// <summary>Raised when the session ends, for any reason including a lost connection.</summary>
        event Action<SessionEndReason, string> OnSessionEnded;

        /// <summary>Installs the configuration every later call reads. Null leaves the service offline.</summary>
        void Configure(SessionConfig config);

        /// <summary>Renames the local player and persists the new name.</summary>
        void SetDisplayName(string displayName);

        /// <summary>Sets the game-defined appearance code sent with the next host or join.</summary>
        void SetAvatarData(ushort avatarData);

        /// <summary>Connects to the configured server and asks it for a session of our own.</summary>
        Task<SessionJoinResult> HostSessionAsync();

        /// <summary>Connects to the configured server and attaches to the session behind a join code.</summary>
        Task<SessionJoinResult> JoinSessionAsync(string joinCode);

        /// <summary>Leaves the session gracefully and drops back to offline.</summary>
        Task LeaveSessionAsync();

        /// <summary>Asks the server to launch a match. The verdict arrives as an event, never inline.</summary>
        Task LaunchMatchAsync(string matchId);

        /// <summary>Tells the server this client has finished loading a match run.</summary>
        void NotifyClientReady(int matchSequence);
    }

    /// <summary>
    /// App-root resident implementation of <see cref="ISessionService"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hosting has two shapes behind one method. Against a dedicated server — what Project Penguin
    /// ships — hosting is just a create request: the server mints the session and the join code, and the
    /// hosting player is a client like any other. Listen hosting instead stands a server up in this
    /// process, hands it a <see cref="ListenServerFrontDesk"/>, and dials loopback, so the local player
    /// still travels the whole handshake and nothing downstream can tell the two apart. That symmetry is
    /// the point: a bug that only appears over a real connection cannot hide in the host's client.
    /// </para>
    /// <para>
    /// The service ticks the session client — and, when listen hosting, the server side — from
    /// <c>Update</c>, after <see cref="INetworkService"/> has pumped the transports. Both live on the app
    /// root, and this one is installed after it, which is what puts them in that order.
    /// </para>
    /// </remarks>
    public class SessionService : MonoBehaviour, ISessionService {
        [Header("Hosting")]
        [Tooltip("Host the session in this process instead of asking the configured server for one. Local development convenience; shipped builds host on the dedicated server.")]
        [SerializeField] private bool listenHost;

        [Header("Collision")]
        [Tooltip("Every scene's exported collision geometry. The client predicts against the entry matching the scene the session is in; a scene missing from here stands on flat ground at y = 0, which will not match a server that has the export.")]
        [SerializeField] private SceneGeometryRegistry geometryRegistry;

        [Header("Diagnostics")]
        [Tooltip("Read-only. Round trip to the server in milliseconds, mirrored every frame so it can be watched in the inspector during play. Editing it does nothing.")]
        [SerializeField] private int pingMs;

        /// <summary>
        /// Tick length a collision world is built with when one is asked for before a net config has been
        /// installed. Matches <see cref="NetConfig"/>'s own default rate, so the mover paths such a world
        /// evaluates are the ones a default session would have produced anyway.
        /// </summary>
        private const float DefaultTickIntervalSeconds = 1f / 30f;

        /// <inheritdoc />
        public ClientSessionState State => _sessionClient?.State ?? ClientSessionState.Offline;

        /// <inheritdoc />
        public SessionPhase Phase => _sessionClient?.Phase ?? SessionPhase.Lobby;

        /// <inheritdoc />
        public string CurrentJoinCode => _sessionClient?.JoinCode ?? string.Empty;

        /// <inheritdoc />
        public string SessionId => _sessionClient?.SessionId ?? string.Empty;

        /// <inheritdoc />
        public IReadOnlyList<SessionMember> Members => _sessionClient?.Members ?? Array.Empty<SessionMember>();

        /// <inheritdoc />
        public bool IsInSession => State == ClientSessionState.InSession;

        /// <inheritdoc />
        public bool IsOwner => _sessionClient != null && _sessionClient.IsOwner;

        /// <inheritdoc />
        public PlayerIdentity Identity => _identity;

        /// <inheritdoc />
        public SessionConfig Config => _config;

        /// <inheritdoc />
        public ClientReplication Replication => _replication;

        /// <inheritdoc />
        public MatchContextData CurrentMatch => _sessionClient?.CurrentMatch;

        /// <summary>
        /// True while a server for this session is running in this process.
        /// </summary>
        /// <remarks>
        /// Exposed on the concrete type rather than the interface: only whoever composed the service —
        /// or a developer tool — has any business knowing, while everything else is written to work the
        /// same either way.
        /// </remarks>
        public bool IsListenHosting => _frontDesk != null;

        /// <summary>
        /// Whether the next <see cref="HostSessionAsync"/> hosts in this process. Settable so a
        /// development menu can flip it without a second config asset.
        /// </summary>
        public bool ListenHost {
            get => listenHost;
            set => listenHost = value;
        }

        /// <summary>
        /// Round trip to the server in milliseconds, or zero outside a connection.
        /// </summary>
        /// <remarks>
        /// Mirrored from <see cref="INetworkService.PingMs"/> rather than forwarded live so the value is
        /// also visible in the inspector while playing, which is where it is read during a playtest. A
        /// HUD may read this property or the network service directly; both answer the same number.
        /// </remarks>
        public int PingMs => pingMs;

        /// <inheritdoc />
        public event Action<ClientSessionState> OnStateChanged;

        /// <inheritdoc />
        public event Action<SessionMember, bool> OnMemberJoined;

        /// <inheritdoc />
        public event Action<SessionMember, LeaveReason> OnMemberLeft;

        /// <inheritdoc />
        public event Action<SessionPhase> OnPhaseChanged;

        /// <inheritdoc />
        public event Action<MatchContextData> OnMatchLoading;

        /// <inheritdoc />
        public event Action<MatchContextData> OnMatchActive;

        /// <inheritdoc />
        public event Action<MatchResultData> OnMatchEnded;

        /// <inheritdoc />
        public event Action OnReturnedToLobby;

        /// <inheritdoc />
        public event Action<PlayerId> OnOwnerChanged;

        /// <inheritdoc />
        public event Action<string> OnLaunchDenied;

        /// <inheritdoc />
        public event Action<SessionEndReason, string> OnSessionEnded;

        private INetworkService _networkService;
        private IIdentityStore _identityStore;
        private SessionConfig _config;
        private NetConfig _netConfig;
        private PlayerIdentity _identity;
        private SessionClient _sessionClient;
        private ClientReplication _replication;
        private ListenServerFrontDesk _frontDesk;
        private CollisionWorld _collisionWorld;
        private string _currentSceneName = string.Empty;
        private SessionEndReason _pendingTearDownReason;
        private bool _isTearDownPending;

        /// <remarks>
        /// Declared on the concrete type rather than the interface, matching the library's other
        /// services: the injector reflects over the concrete type when registering a provider.
        /// </remarks>
        [Provide]
        public ISessionService ProvideSessionService() {
            return this;
        }

        /// <inheritdoc />
        public void Configure(SessionConfig config) {
            _config = config;

            if (_config == null) {
                Debug.LogWarning("SessionService::Configure->No session config; the game stays offline.");
                return;
            }

            _netConfig = _config.ToNetConfig();
            ResolveNetworkService()?.Configure(_netConfig);
            _identity = ResolveIdentityStore().Load(_config.defaultDisplayName);
        }

        /// <summary>
        /// Installs the registry the client resolves each scene's collision geometry from.
        /// </summary>
        /// <remarks>
        /// The registry is normally dragged onto the serialized field in the inspector, which is the
        /// right answer whenever this service is authored into a scene or a prefab. A game that installs
        /// its app root entirely from code — Project Penguin does — has no inspector to drag it onto, so
        /// it hands the asset over here instead, alongside the session config, before anything asks for a
        /// world. Passing null leaves whatever the field already holds alone: a caller with no registry to
        /// offer should not be able to unassign an authored one by accident.
        /// </remarks>
        public void ConfigureGeometry(SceneGeometryRegistry registry) {
            if (registry == null) {
                Debug.LogWarning("SessionService::ConfigureGeometry->No scene geometry registry; scenes without one stand on flat ground at y = 0.");
                return;
            }

            geometryRegistry = registry;
        }

        /// <inheritdoc />
        public void SetDisplayName(string displayName) {
            if (_identity == null) {
                Debug.LogWarning("SessionService::SetDisplayName->No identity yet; configure the service first.");
                return;
            }

            // The session client holds this very instance, so renaming here renames it there too.
            _identity.DisplayName = PlayerIdentity.Sanitize(displayName);
            ResolveIdentityStore().Save(_identity);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Not persisted, unlike the display name: the appearance is session state owned by the game,
        /// which re-sends it before every host or join. The identity store stays a name-and-id file.
        /// </remarks>
        public void SetAvatarData(ushort avatarData) {
            if (_identity == null) {
                Debug.LogWarning("SessionService::SetAvatarData->No identity yet; configure the service first.");
                return;
            }

            _identity.AvatarData = avatarData;
        }

        /// <inheritdoc />
        public async Task<SessionJoinResult> HostSessionAsync() {
            if (!IsConfigured()) return SessionJoinResult.Denied(SessionEndReason.HostClosed, "No session config.");

            NetEndpoint endpoint = listenHost
                ? StartListenHost()
                : await ResolveServerEndpointAsync();

            if (!endpoint.IsValid) {
                return SessionJoinResult.Denied(SessionEndReason.TransportLost, "No server endpoint.");
            }

            SessionJoinResult connectResult = await ConnectAsync(endpoint);

            if (!connectResult.IsSuccess) return connectResult;

            return await _sessionClient.CreateSessionAsync(ResolveProfileId());
        }

        /// <inheritdoc />
        public async Task<SessionJoinResult> JoinSessionAsync(string joinCode) {
            if (!IsConfigured()) return SessionJoinResult.Denied(SessionEndReason.HostClosed, "No session config.");

            if (!JoinCodeGenerator.TryNormalize(joinCode, out string normalizedCode)) {
                return SessionJoinResult.Denied(SessionEndReason.SessionNotFound, "That is not a join code.");
            }

            NetEndpoint endpoint = await ResolveServerEndpointAsync();

            if (!endpoint.IsValid) {
                return SessionJoinResult.Denied(SessionEndReason.TransportLost, "No server endpoint.");
            }

            SessionJoinResult connectResult = await ConnectAsync(endpoint);

            if (!connectResult.IsSuccess) return connectResult;

            return await _sessionClient.JoinSessionAsync(normalizedCode);
        }

        /// <inheritdoc />
        public async Task LeaveSessionAsync() {
            if (_sessionClient != null) {
                await _sessionClient.LeaveAsync();
            }

            TearDownSession(SessionEndReason.HostClosed);
        }

        /// <inheritdoc />
        public Task LaunchMatchAsync(string matchId) {
            if (_sessionClient == null) return Task.CompletedTask;

            _sessionClient.RequestLaunchMatch(matchId);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void NotifyClientReady(int matchSequence) {
            _sessionClient?.NotifyClientReady(matchSequence);
        }

        private void Awake() {
            // Application-shutdown guard, not a race guard: this service is installed on the app root,
            // so an absent injector means the game is already tearing down.
            if (!Injector.HasInstance) return;

            Injector.Instance.RegisterProvider(this);
        }

        private void OnDestroy() {
            TearDownSession(SessionEndReason.HostClosed);

            if (!Injector.HasInstance) return;

            Injector.Instance.UnregisterProvider(this);
        }

        /// <remarks>
        /// The session client is ticked here rather than from the network service because it owns
        /// timeouts, not sockets: the transports have already been polled this frame, so a reply that
        /// arrived is seen before the request waiting on it is aged.
        /// </remarks>
        private void Update() {
            if (_isTearDownPending) {
                _isTearDownPending = false;
                TearDownSession(_pendingTearDownReason);
                return;
            }

            float deltaSeconds = Time.deltaTime;

            _frontDesk?.Tick(deltaSeconds);
            _sessionClient?.Tick(deltaSeconds);
            _replication?.Tick(deltaSeconds);

            pingMs = _networkService?.PingMs ?? 0;
        }

        /// <summary>
        /// Brings up the in-process server and its front desk, and reports the loopback endpoint the
        /// local client should dial.
        /// </summary>
        private NetEndpoint StartListenHost() {
            if (_frontDesk != null) return LoopbackEndpoint();

            INetworkService networkService = ResolveNetworkService();
            networkService.Configure(_netConfig);
            NetServer server = networkService.StartListenServer();

            if (server == null) return NetEndpoint.None;

            _frontDesk = new ListenServerFrontDesk(
                server,
                _config.ToData(),
                _netConfig,
                new AnonymousAuthValidator(_config.defaultDisplayName),
                CurrentCollisionWorld());

            return LoopbackEndpoint();
        }

        private NetEndpoint LoopbackEndpoint() {
            return NetEndpoint.Direct("127.0.0.1", _netConfig.Port);
        }

        /// <summary>Asks the configured locator where this build's server lives.</summary>
        private async Task<NetEndpoint> ResolveServerEndpointAsync() {
            if (_config.matchmaking == null) {
                Debug.LogError("SessionService::ResolveServerEndpointAsync->No matchmaking config.");
                return NetEndpoint.None;
            }

            ISessionLocator locator = _config.matchmaking.CreateLocator();

            if (locator == null) return NetEndpoint.None;

            return await locator.ResolveAsync(string.Empty, CancellationToken.None);
        }

        /// <summary>
        /// Brings the client facade, the session client and the replicated world up, then travels the
        /// connect and authenticate half of the handshake.
        /// </summary>
        private async Task<SessionJoinResult> ConnectAsync(NetEndpoint endpoint) {
            INetworkService networkService = ResolveNetworkService();
            networkService.Configure(_netConfig);
            NetClient client = networkService.StartClient();

            if (client == null) {
                return SessionJoinResult.Denied(SessionEndReason.TransportLost, "No client facade.");
            }

            BuildSessionClient(client);

            SessionJoinResult result = await _sessionClient.ConnectAsync(endpoint);

            if (!result.IsSuccess) {
                RequestTearDown(result.Reason);
            }

            return result;
        }

        /// <summary>
        /// Creates the session client and the client world over a connection, once per connection.
        /// </summary>
        /// <remarks>
        /// Both register handlers on the client's router, so building a second pair over the same
        /// connection would either throw or silently steal the first pair's messages.
        /// </remarks>
        private void BuildSessionClient(NetClient client) {
            if (_sessionClient != null) return;

            _identity ??= ResolveIdentityStore().Load(_config.defaultDisplayName);

            _sessionClient = new SessionClient(client, new AnonymousAuthProvider(), _identity);
            SubscribeToSessionClient();

            _replication = new ClientReplication(client, _netConfig, CurrentCollisionWorld());
        }

        /// <summary>
        /// The scene collision both halves of this process simulate against: the client's prediction and,
        /// on a listen host, the server's authority. Resolves the lobby's geometry the first time it is
        /// asked, so a world exists before the first phase change arrives.
        /// </summary>
        /// <remarks>
        /// A listen host must predict against exactly the world its server half steps, or the host's own
        /// pawn is corrected every tick — which is why one field answers for both and neither builds its
        /// own. A guest is in the same position against a dedicated server: it predicts against the
        /// registry's copy of the very bytes the server loaded, and a scene missing from the registry puts
        /// that client on a floor nobody else is standing on.
        /// </remarks>
        private CollisionWorld CurrentCollisionWorld() {
            if (_collisionWorld == null) {
                UseSceneGeometry(ResolveLobbySceneName());
            }

            return _collisionWorld;
        }

        /// <summary>
        /// Moves this process onto a scene's collision: the client's prediction world, and on a listen
        /// host the server half's as well.
        /// </summary>
        /// <remarks>
        /// Re-entry on the same scene is skipped, and that is load-bearing rather than an optimisation.
        /// A match announcement and the match start that follows it name the same scene, and on a listen
        /// host the second swap would despawn every platform and respawn it under fresh entity ids while
        /// the players are already standing on them.
        /// </remarks>
        private void UseSceneGeometry(string sceneName) {
            string requested = sceneName ?? string.Empty;

            if (_collisionWorld != null && string.Equals(requested, _currentSceneName, StringComparison.Ordinal)) return;

            _currentSceneName = requested;
            _collisionWorld = ResolveCollisionWorld(requested);

            if (_replication != null) {
                _replication.CollisionWorld = _collisionWorld;
            }

            _frontDesk?.UseWorld(_collisionWorld);
        }

        /// <summary>
        /// Builds — or reuses — the collision world a scene exported, falling back to an endless floor at
        /// y = 0 when the registry has nothing for it.
        /// </summary>
        /// <remarks>
        /// The fallback is loud when a scene was actually asked for, because it is the shape of the worst
        /// bug this system can produce: the client predicts on a plane, the server simulates the real
        /// igloo, and the owner's pawn is dragged back by a correction on every single tick. A session with
        /// no scene name asked for nothing and gets flat ground quietly, which is what a headless test or
        /// an unconfigured lobby wants.
        /// </remarks>
        private CollisionWorld ResolveCollisionWorld(string sceneName) {
            if (string.IsNullOrEmpty(sceneName)) return CollisionWorld.Flat();

            float tickIntervalSeconds = _netConfig != null ? _netConfig.ServerTickInterval : DefaultTickIntervalSeconds;

            if (geometryRegistry == null) {
                Debug.LogWarning($"SessionService::ResolveCollisionWorld->No scene geometry registry assigned; '{sceneName}' falls back to flat ground at y = 0.");
                return CollisionWorld.Flat();
            }

            // The null test is belt and braces against a registry row whose asset was deleted from under
            // it: both halves of this service hand the result straight to something that rejects null, and
            // a scene that resolves to nothing is exactly the scene that should fall back.
            if (geometryRegistry.TryResolveWorld(sceneName, tickIntervalSeconds, out CollisionWorld world) && world != null) {
                return world;
            }

            Debug.LogWarning($"SessionService::ResolveCollisionWorld->No geometry was exported for scene '{sceneName}'; it falls back to flat ground at y = 0.");
            return CollisionWorld.Flat();
        }

        /// <summary>Scene the lobby itself lives in, or empty when none is configured.</summary>
        private string ResolveLobbySceneName() {
            if (_config == null || _config.lobby == null) return string.Empty;

            return _config.lobby.lobbySceneName ?? string.Empty;
        }

        /// <summary>
        /// Scene a match run takes place in. Falls back to the lobby scene, because a match announced with
        /// no scene of its own leaves everybody standing where they already were.
        /// </summary>
        private string ResolveMatchSceneName(MatchContextData match) {
            if (match == null || string.IsNullOrEmpty(match.SceneName)) return ResolveLobbySceneName();

            return match.SceneName;
        }

        private void SubscribeToSessionClient() {
            _sessionClient.OnStateChanged += HandleStateChanged;
            _sessionClient.OnMemberJoined += HandleMemberJoined;
            _sessionClient.OnMemberLeft += HandleMemberLeft;
            _sessionClient.OnPhaseChanged += HandlePhaseChanged;
            _sessionClient.OnMatchLoading += HandleMatchLoading;
            _sessionClient.OnMatchActive += HandleMatchActive;
            _sessionClient.OnMatchEnded += HandleMatchEnded;
            _sessionClient.OnReturnedToLobby += HandleReturnedToLobby;
            _sessionClient.OnOwnerChanged += HandleOwnerChanged;
            _sessionClient.OnLaunchDenied += HandleLaunchDenied;
            _sessionClient.OnSessionEnded += HandleSessionEnded;
        }

        private void UnsubscribeFromSessionClient() {
            _sessionClient.OnStateChanged -= HandleStateChanged;
            _sessionClient.OnMemberJoined -= HandleMemberJoined;
            _sessionClient.OnMemberLeft -= HandleMemberLeft;
            _sessionClient.OnPhaseChanged -= HandlePhaseChanged;
            _sessionClient.OnMatchLoading -= HandleMatchLoading;
            _sessionClient.OnMatchActive -= HandleMatchActive;
            _sessionClient.OnMatchEnded -= HandleMatchEnded;
            _sessionClient.OnReturnedToLobby -= HandleReturnedToLobby;
            _sessionClient.OnOwnerChanged -= HandleOwnerChanged;
            _sessionClient.OnLaunchDenied -= HandleLaunchDenied;
            _sessionClient.OnSessionEnded -= HandleSessionEnded;
        }

        private void HandleStateChanged(ClientSessionState state) {
            AdoptLocalPeerId();
            OnStateChanged?.Invoke(state);
        }

        private void HandleMemberJoined(SessionMember member, bool isRejoin) {
            AdoptLocalPeerId();
            OnMemberJoined?.Invoke(member, isRejoin);
        }

        private void HandleMemberLeft(SessionMember member, LeaveReason reason) {
            OnMemberLeft?.Invoke(member, reason);
        }

        private void HandlePhaseChanged(SessionPhase phase) {
            OnPhaseChanged?.Invoke(phase);
        }

        /// <remarks>
        /// The world moves before anybody hears about the match, so the scene flow's load and this
        /// client's first predicted tick in the new scene both happen against the geometry the server has
        /// already switched to. The server swaps on the same event for the same reason.
        /// </remarks>
        private void HandleMatchLoading(MatchContextData match) {
            UseSceneGeometry(ResolveMatchSceneName(match));
            OnMatchLoading?.Invoke(match);
        }

        /// <remarks>
        /// Swapping here as well is not redundant with <see cref="HandleMatchLoading"/>: a player
        /// rejoining a match that is already running is told it started rather than that it is loading,
        /// and would otherwise predict the whole match against the lobby. In the ordinary path the scene
        /// is the one already in force and the swap costs nothing.
        /// </remarks>
        private void HandleMatchActive(MatchContextData match) {
            UseSceneGeometry(ResolveMatchSceneName(match));
            OnMatchActive?.Invoke(match);
        }

        private void HandleMatchEnded(MatchResultData results) {
            OnMatchEnded?.Invoke(results);
        }

        private void HandleReturnedToLobby() {
            UseSceneGeometry(ResolveLobbySceneName());
            OnReturnedToLobby?.Invoke();
        }

        private void HandleOwnerChanged(PlayerId ownerId) {
            OnOwnerChanged?.Invoke(ownerId);
        }

        private void HandleLaunchDenied(string reason) {
            OnLaunchDenied?.Invoke(reason);
        }

        /// <remarks>
        /// The teardown is queued rather than performed here. This runs inside the transport's own poll
        /// — the session end arrived as a message — and disposing the transport from inside its poll is
        /// how a socket gets torn down while it is still walking its event queue. The next
        /// <c>Update</c> is the first moment it is safe.
        /// </remarks>
        private void HandleSessionEnded(SessionEndReason reason, string message) {
            RequestTearDown(reason);
            OnSessionEnded?.Invoke(reason, message);
        }

        /// <summary>Queues a teardown for the start of the next frame.</summary>
        private void RequestTearDown(SessionEndReason reason) {
            _pendingTearDownReason = reason;
            _isTearDownPending = true;
        }

        /// <summary>
        /// Tells the client world which peer we are, so it can tell our pawn from everybody else's.
        /// </summary>
        /// <remarks>
        /// The peer id is only known once the server has answered the auth request and put us on a
        /// roster, which is why this is re-checked on every state change rather than done once.
        /// </remarks>
        private void AdoptLocalPeerId() {
            if (_replication == null || _sessionClient == null) return;

            SessionMember localMember = _sessionClient.LocalMember;

            if (localMember == null) return;

            _replication.LocalPeerId = localMember.PeerId;
        }

        /// <summary>
        /// Drops everything a session owns — the client world, the session client, the in-process
        /// server — and returns the process to offline. Safe to call when there is nothing to drop.
        /// </summary>
        private void TearDownSession(SessionEndReason reason) {
            _isTearDownPending = false;

            if (_sessionClient != null) {
                UnsubscribeFromSessionClient();
                _sessionClient = null;
            }

            _replication?.Dispose();
            _replication = null;

            _frontDesk?.Close(reason);
            _frontDesk = null;

            // Dropped rather than kept: a player who left in the middle of a match would otherwise host or
            // join their next session predicting against the match scene they walked out of, until a phase
            // change happened to name something else.
            _collisionWorld = null;
            _currentSceneName = string.Empty;

            _networkService?.Shutdown();
        }

        private string ResolveProfileId() {
            if (_config.profile == null) return string.Empty;

            return _config.profile.profileId;
        }

        private IIdentityStore ResolveIdentityStore() {
            return _identityStore ??= new FileIdentityStore();
        }

        /// <remarks>
        /// Resolved on demand rather than injected, because both services are installed on the app root
        /// in the same frame and neither may assume it woke up second.
        /// </remarks>
        private INetworkService ResolveNetworkService() {
            if (_networkService != null) return _networkService;
            if (!Injector.HasInstance) return null;

            Injector.Instance.TryResolve(out _networkService);
            return _networkService;
        }

        private bool IsConfigured() {
            if (_config != null && _netConfig != null && ResolveNetworkService() != null) return true;

            Debug.LogError("SessionService::IsConfigured->Configure must run, with a network service present.");
            return false;
        }
    }
}
