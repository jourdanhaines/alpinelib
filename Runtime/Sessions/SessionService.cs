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
using AlpineLib.Netcode.Sessions.Claims;
using AlpineLib.Netcode.Sessions.Spawning;
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

        /// <summary>The session's claim slots as this client sees them, or null outside a session.</summary>
        ClientClaims Claims { get; }

        /// <summary>The match currently loading or running, or null in a lobby.</summary>
        MatchContextData CurrentMatch { get; }

        /// <summary>
        /// The endpoint the session this client hosted was created on, or none when it did not host one.
        /// </summary>
        /// <remarks>
        /// Exists so a host screen can read out the address a friend on the same network types, which
        /// nothing else knows: the port a locally launched server bound may be an ephemeral one nobody
        /// chose, and the join code deliberately carries no address.
        /// </remarks>
        NetEndpoint HostEndpoint { get; }

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

        /// <summary>
        /// Raised whenever the objects a session owns — <see cref="Replication"/>, <see cref="Claims"/>
        /// and the network service's client — have been built or torn down.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The three are created together over one connection and dropped together, and they are
        /// exposed as properties rather than announced, so every consumer that needed to know had to
        /// reference-compare them in its own <c>Update</c>. This is that comparison, done once. It
        /// carries no payload on purpose: whichever of the three a listener cares about, it re-reads it
        /// from the service inside the handler, and any of them may be null (a teardown raises this
        /// too).
        /// </para>
        /// <para>
        /// It fires twice per connection, and the sequence is what a listener has to plan around. The
        /// first raise is the build: the three objects exist, but the connection has not been dialled,
        /// so <c>Claims.LocalPeerId</c> and <c>Replication.LocalPeerId</c> are both -1 and no question
        /// of the form "is this mine" has a true answer yet. The second is the adoption: the server has
        /// answered the handshake and put this client on a roster, the identity behind all three is now
        /// real, and any locally held slot has been replayed as a grant. Take references on the first
        /// and read ownership on the second — or, better, take ownership from
        /// <c>ClientClaims.OnClaimGranted</c> and replication's own events rather than from
        /// <c>LocalPeerId</c> at all.
        /// </para>
        /// </remarks>
        event Action OnSessionResourcesChanged;

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

        /// <summary>
        /// Attaches to a session behind a join code on a server the player named, instead of the one
        /// this build is configured for.
        /// </summary>
        /// <remarks>
        /// The locator answers "which server does this build talk to", which is the right question for a
        /// shipped deployment and the wrong one for two friends dialling a machine one of them is
        /// hosting on. A typed address bypasses it entirely rather than overriding it, so the configured
        /// server stays the default for everything else in the same run.
        /// </remarks>
        Task<SessionJoinResult> JoinSessionAsync(string joinCode, string serverAddress);

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
    /// Hosting has three shapes behind one method, and they differ only in where the endpoint comes
    /// from. Against a dedicated server — the shape a shipped game runs — hosting is just a create
    /// request: the server mints the session and the join code, and the hosting player is a client like
    /// any other. Listen hosting instead stands a server up in this process, hands it a
    /// <see cref="ListenServerFrontDesk"/>, and dials loopback. Local-process hosting launches the real
    /// server executable beside the build and dials the port it reports. In all three the local player
    /// travels the whole handshake and nothing downstream can tell them apart. That symmetry is the
    /// point: a bug that only appears over a real connection cannot hide in the host's client.
    /// </para>
    /// <para>
    /// The service ticks the session client — and, when listen hosting, the server side — from
    /// <c>Update</c>, after <see cref="INetworkService"/> has pumped the transports. Both live on the app
    /// root, and this one is installed after it, which is what puts them in that order.
    /// </para>
    /// <para>
    /// Local-process hosting is the one mode where leaving does not end everybody's session. The server
    /// is a process, not a piece of this client, so a host who leaves while other players are still in
    /// detaches it and lets it run on; only quitting, being destroyed, or leaving alone stops it. See
    /// <see cref="TearDownSession"/>.
    /// </para>
    /// </remarks>
    public class SessionService : MonoBehaviour, ISessionService {
        [Header("Hosting")]
        [Tooltip("Where the server behind a session this client hosts lives: the configured remote server, this process, or a server executable launched beside this build.")]
        [SerializeField] private SessionHostingMode hostingMode = SessionHostingMode.RemoteServer;
        [Tooltip("Which server executable to launch, and how. Read only in Local Server Process mode.")]
        [SerializeField] private LocalServerConfig localServer;

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
        public ClientClaims Claims => _claims;

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

        /// <inheritdoc />
        /// <remarks>
        /// Answered against the launcher, not from the field alone: a server process that exits on its
        /// own — the idle timeout, or a crash — leaves an address nothing answers on, and a menu reading
        /// this out to friends has to stop offering it the moment that happens.
        /// </remarks>
        public NetEndpoint HostEndpoint => IsHostEndpointLive() ? _hostEndpoint : NetEndpoint.None;

        /// <summary>
        /// The game's rule about which claim slots a session hosted in this process will answer for, or
        /// null to accept every slot number.
        /// </summary>
        /// <remarks>
        /// Only <see cref="SessionHostingMode.ListenHost"/> reads it, and it is read once, where the
        /// front desk is built — so it has to be set before each host, and a game that installs its rule
        /// from a scene component rather than the app root can easily be too late. Setting it while a
        /// front desk is already standing warns rather than passing silently, because the live session
        /// keeps the rule it was built with. The other two modes get their rule from the server process
        /// instead, through <c>ISessionModuleFactory.BuildClaimValidator</c>; this is the same delegate,
        /// and a game with an authority rule should install it in both places or its rule applies in one
        /// hosting mode and not the other. Exposed on the concrete type rather than the interface
        /// because only whoever composes the app root has a rule to hand over.
        /// </remarks>
        public Func<ushort, PeerHandle, ServerClaimRegistry, bool> ClaimValidator {
            get => _claimValidator;
            set {
                WarnIfClaimValidatorArrivesLate(value);
                _claimValidator = value;
            }
        }

        /// <summary>
        /// Where the next <see cref="HostSessionAsync"/> puts its server. Settable so an app root or a
        /// development menu can pick a mode without a second config asset.
        /// </summary>
        public SessionHostingMode HostingMode {
            get => hostingMode;
            set => hostingMode = value;
        }

        /// <summary>
        /// Whether the next <see cref="HostSessionAsync"/> hosts in this process.
        /// </summary>
        /// <remarks>
        /// Kept as a shim over <see cref="HostingMode"/> for callers written before there were three
        /// modes. Clearing it means "not in this process", which can only be read as the remote server:
        /// a caller that thinks in booleans has no third answer to give.
        /// </remarks>
        public bool ListenHost {
            get => hostingMode == SessionHostingMode.ListenHost;
            set => hostingMode = value ? SessionHostingMode.ListenHost : SessionHostingMode.RemoteServer;
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

        /// <inheritdoc />
        public event Action OnSessionResourcesChanged;

        private INetworkService _networkService;
        private IIdentityStore _identityStore;
        private SessionConfig _config;
        private NetConfig _netConfig;
        private PlayerIdentity _identity;
        private SessionClient _sessionClient;
        private ClientReplication _replication;
        private ClientClaims _claims;
        private Func<ushort, PeerHandle, ServerClaimRegistry, bool> _claimValidator;
        private ListenServerFrontDesk _frontDesk;
        private LocalServerLauncher _localServer;
        private LocalServerConfig _launchedServerConfig;
        private CollisionWorld _collisionWorld;
        private CancellationTokenSource _hostStartSource;
        private NetEndpoint _hostEndpoint;
        private string _hostEndpointFailure = string.Empty;
        private string _currentSceneName = string.Empty;
        private SessionEndReason _pendingTearDownReason;
        private bool _isTearDownPending;
        private bool _hasWarnedOverrideIgnored;
        private bool _isLocalServerStarting;
        private bool _isShuttingDown;
        private int _adoptedLocalPeerId = ClientClaims.FreeHolderPeerId;

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
        /// its app root entirely from code has no inspector to drag it onto, so it hands the asset over
        /// here instead, alongside the session config, before anything asks for a world. Passing null
        /// leaves whatever the field already holds alone: a caller with no registry to offer should not
        /// be able to unassign an authored one by accident.
        /// </remarks>
        public void ConfigureGeometry(SceneGeometryRegistry registry) {
            if (registry == null) {
                Debug.LogWarning("SessionService::ConfigureGeometry->No scene geometry registry; scenes without one stand on flat ground at y = 0.");
                return;
            }

            geometryRegistry = registry;
        }

        /// <summary>
        /// Installs the configuration read when hosting launches a server executable.
        /// </summary>
        /// <remarks>
        /// The counterpart to <see cref="ConfigureGeometry"/>, and null is treated the same way: a
        /// caller with nothing to offer must not be able to unassign what the inspector already holds.
        /// </remarks>
        public void ConfigureLocalServer(LocalServerConfig config) {
            if (config == null) {
                Debug.LogWarning("SessionService::ConfigureLocalServer->No local server config; hosting a server process will fail until one is assigned.");
                return;
            }

            if (config == localServer) return;

            localServer = config;

            // A launcher that is hosting right now is a server somebody is playing on, and dropping one
            // means killing it. The new config is already stored, so the next host picks it up.
            if (IsLocalServerBusy()) {
                Debug.LogWarning("SessionService::ConfigureLocalServer->A local server is already running or starting; keeping it and applying the new config on the next host.");
                return;
            }

            DropLocalServer();
        }

        /// <summary>True while a launcher owns a live server, or is still waiting on one to report in.</summary>
        private bool IsLocalServerBusy() {
            if (_localServer == null) return false;

            return _isLocalServerStarting || _localServer.IsRunning;
        }

        /// <summary>
        /// Drops the launcher, killing whatever it still owns.
        /// </summary>
        /// <remarks>
        /// The launcher captured its config when it was built, so it cannot serve a replacement one:
        /// keeping it would silently launch the previous executable on the previous port.
        /// </remarks>
        private void DropLocalServer() {
            _localServer?.Dispose();
            _localServer = null;
            _launchedServerConfig = null;
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
            if (!IsConfigured()) return DeniedLocally(SessionEndReason.HostClosed, SessionDenial.NoSessionConfig, "No session config.");

            _hostEndpoint = NetEndpoint.None;

            NetEndpoint endpoint = await ResolveHostEndpointAsync();

            if (!endpoint.IsValid) {
                return DeniedLocally(SessionEndReason.TransportLost, ResolveHostFailureDenial(), ResolveHostFailureMessage());
            }

            SessionJoinResult connectResult = await ConnectAsync(endpoint);

            if (!connectResult.IsSuccess) return connectResult;

            // Recorded before the await, not after: a teardown landing mid-create clears it, and writing
            // it back on the way out would hand the menu an address for a session that no longer exists.
            _hostEndpoint = endpoint;

            SessionJoinResult createResult = await _sessionClient.CreateSessionAsync(ResolveProfileId());

            if (!createResult.IsSuccess) {
                _hostEndpoint = NetEndpoint.None;
            }

            return createResult;
        }

        /// <inheritdoc />
        public async Task<SessionJoinResult> JoinSessionAsync(string joinCode) {
            if (!IsConfigured()) return DeniedLocally(SessionEndReason.HostClosed, SessionDenial.NoSessionConfig, "No session config.");

            if (!JoinCodeGenerator.TryNormalize(joinCode, out string normalizedCode)) {
                return DeniedLocally(SessionEndReason.SessionNotFound, SessionDenial.BadJoinCode, "That is not a join code.");
            }

            NetEndpoint endpoint = await ResolveServerEndpointAsync();

            if (!endpoint.IsValid) {
                return DeniedLocally(SessionEndReason.TransportLost, SessionDenial.NoServerEndpoint, "No server endpoint.");
            }

            return await JoinResolvedAsync(endpoint, normalizedCode);
        }

        /// <inheritdoc />
        public async Task<SessionJoinResult> JoinSessionAsync(string joinCode, string serverAddress) {
            if (!IsConfigured()) return DeniedLocally(SessionEndReason.HostClosed, SessionDenial.NoSessionConfig, "No session config.");

            if (!JoinCodeGenerator.TryNormalize(joinCode, out string normalizedCode)) {
                return DeniedLocally(SessionEndReason.SessionNotFound, SessionDenial.BadJoinCode, "That is not a join code.");
            }

            if (!ConfiguredServerLocator.TryParseAddress(serverAddress, out NetEndpoint endpoint)) {
                return DeniedLocally(SessionEndReason.SessionNotFound, SessionDenial.BadServerAddress, "That is not a server address.");
            }

            return await JoinResolvedAsync(endpoint, normalizedCode);
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
            // Set before the teardown, so a session with guests still in it is stopped rather than
            // detached: the application is going away and nothing would be left to reap the server.
            _isShuttingDown = true;

            TearDownSession(SessionEndReason.HostClosed);

            // Disposed rather than stopped: the launcher holds editor and quit hooks that would keep it
            // alive past this service, and there is no session left to reuse a warm server for.
            DropLocalServer();
            _hostStartSource?.Dispose();
            _hostStartSource = null;

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

            SpawnPlacementConfig spawn = _config.spawn;

            _frontDesk = new ListenServerFrontDesk(
                server,
                _config.ToData(),
                _netConfig,
                new AnonymousAuthValidator(_config.defaultDisplayName),
                CurrentCollisionWorld(),
                pawnPrefabId: spawn != null ? spawn.pawnPrefabId : SpawnPlacementConfig.DefaultPawnPrefabId,
                pawnAuthority: spawn != null ? spawn.pawnAuthority : AuthorityMode.Server,
                placement: spawn != null ? spawn.ToPlacement() : new RingSpawnPlacement(),
                isClaimAllowed: ClaimValidator);

            return LoopbackEndpoint();
        }

        /// <summary>
        /// Warns when a claim rule arrives after the front desk that would have read it.
        /// </summary>
        /// <remarks>
        /// The rule is handed to the registry the front desk builds, so a value assigned afterwards
        /// reaches the next host and not this one. Silence here is an authority hole that looks like a
        /// working rule; the same delegate arriving twice is not worth a line.
        /// </remarks>
        private void WarnIfClaimValidatorArrivesLate(Func<ushort, PeerHandle, ServerClaimRegistry, bool> incoming) {
            if (_frontDesk == null || incoming == _claimValidator) {
                return;
            }

            Debug.LogWarning(
                "SessionService::ClaimValidator->A listen host is already up; it keeps the claim rule it was built with. Set this before hosting.");
        }

        private NetEndpoint LoopbackEndpoint() {
            return NetEndpoint.Direct("127.0.0.1", _netConfig.Port);
        }

        /// <summary>
        /// Answers which server a session hosted right now should be created on, standing one up first
        /// when the mode says to.
        /// </summary>
        /// <remarks>
        /// The failure detail travels in <c>_hostEndpointFailure</c> rather than out of this
        /// method, because an invalid endpoint is the only failure the caller has to branch on and a
        /// second return value would be read at exactly one call site.
        /// </remarks>
        private async Task<NetEndpoint> ResolveHostEndpointAsync() {
            _hostEndpointFailure = string.Empty;

            if (hostingMode == SessionHostingMode.ListenHost) return StartListenHost();
            if (hostingMode == SessionHostingMode.LocalServerProcess) return await StartLocalServerAsync();

            return await ResolveServerEndpointAsync();
        }

        /// <summary>
        /// Launches — or reuses — the server executable beside this build and reports where it came up.
        /// </summary>
        /// <remarks>
        /// The launcher outlives a single host attempt so that hosting twice in a row cannot leave two
        /// servers behind: the second start reaps the first server through the same object rather than
        /// racing it for the port. Its failures are reported rather than thrown, because a menu asking
        /// to host wants a denial to show the player, not an exception to catch.
        /// </remarks>
        private async Task<NetEndpoint> StartLocalServerAsync() {
            if (localServer == null) {
                _hostEndpointFailure = "No local server config.";
                Debug.LogError("SessionService::StartLocalServerAsync->No local server config; assign one or call ConfigureLocalServer.");
                return NetEndpoint.None;
            }

            WarnIfServerAddressOverrideIgnored();
            AdoptConfiguredLocalServer();

            _isLocalServerStarting = true;

            try {
                return await _localServer.StartAsync(RenewHostStartToken());
            } catch (OperationCanceledException) {
                _hostEndpointFailure = "Hosting was cancelled.";
                return NetEndpoint.None;
            } catch (Exception exception) {
                _hostEndpointFailure = "The local server did not start.";
                Debug.LogError($"SessionService::StartLocalServerAsync->{exception.Message}");
                return NetEndpoint.None;
            } finally {
                _isLocalServerStarting = false;
            }
        }

        /// <summary>Makes sure the launcher about to be used is the one built for the current config.</summary>
        /// <remarks>
        /// This is where a config swap deferred by <see cref="ConfigureLocalServer"/> actually lands: the
        /// old launcher — and the server it is still holding — is dropped now, between sessions, rather
        /// than out from under a session in progress.
        /// </remarks>
        private void AdoptConfiguredLocalServer() {
            if (!TryDropSupersededLocalServer()) return;

            _localServer ??= new LocalServerLauncher(localServer);
            _launchedServerConfig = localServer;
        }

        /// <summary>
        /// Drops a launcher built for a config that is no longer the current one, and reports whether
        /// the caller may now build a launcher for the current one.
        /// </summary>
        /// <remarks>
        /// The deferral <see cref="ConfigureLocalServer"/> makes has to hold here too. Hosting is
        /// re-callable, so a second Host with a session already live reaches this with the old config
        /// still launched — and dropping the launcher then kills the server that session is on, which is
        /// the very thing the deferral exists to prevent. A busy launcher keeps serving its own config
        /// until it is idle, and says so once rather than swapping silently.
        /// </remarks>
        private bool TryDropSupersededLocalServer() {
            if (_launchedServerConfig == localServer) return true;

            if (!IsLocalServerBusy()) {
                DropLocalServer();
                return true;
            }

            Debug.LogWarning("SessionService::TryDropSupersededLocalServer->A local server is still running on the previous config; reusing it and applying the new config once it is idle.");
            return false;
        }

        /// <summary>
        /// Replaces the token a pending server start is cancelled by, and hands out the new one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Teardown is the signal this exists for. A player who presses Host and then leaves — or exits
        /// play mode, or loses the transport — must not be left watching "starting a train…" until a
        /// readiness budget nobody is waiting for runs out.
        /// </para>
        /// <para>
        /// The old source is cancelled before it is disposed, because a token whose source was disposed
        /// without ever being cancelled is not "finished" — it is permanently uncancellable, silently.
        /// Anything still holding it, such as a superseded start's retry, has to see it end.
        /// </para>
        /// </remarks>
        private CancellationToken RenewHostStartToken() {
            _hostStartSource?.Cancel();
            _hostStartSource?.Dispose();
            _hostStartSource = new CancellationTokenSource();

            return _hostStartSource.Token;
        }

        /// <summary>Cancels a server start still in flight, if there is one.</summary>
        private void CancelHostStart() {
            if (_hostStartSource == null) return;

            _hostStartSource.Cancel();
            _hostStartSource.Dispose();
            _hostStartSource = null;
        }

        /// <summary>True unless the host endpoint belongs to a local server that has since died.</summary>
        private bool IsHostEndpointLive() {
            if (hostingMode != SessionHostingMode.LocalServerProcess) return true;

            return _localServer != null && _localServer.Endpoint.IsValid;
        }

        /// <summary>
        /// Says once per run that an address override is being ignored, because a build hosting its own
        /// server dials the port that server reported and nothing else.
        /// </summary>
        /// <remarks>
        /// Silence here is the trap this exists to avoid: a tester who set the override for a staging
        /// server, then switched to local hosting, would otherwise have no way to tell whether the
        /// override was in force.
        /// </remarks>
        private void WarnIfServerAddressOverrideIgnored() {
            if (_hasWarnedOverrideIgnored) return;
            if (!ServerAddressOverride.TryResolve(out string address, out string source)) return;

            _hasWarnedOverrideIgnored = true;
            Debug.LogWarning($"SessionService::WarnIfServerAddressOverrideIgnored->Hosting a local server process, so '{address}' from {source} is ignored.");
        }

        /// <summary>
        /// A refusal this client decided, tagged so the caller does not have to read the message.
        /// </summary>
        /// <remarks>
        /// The message is kept for the log and for a caller with nothing better to show, but it is a
        /// developer string: the player-facing sentence belongs to whoever is drawing the screen, and it
        /// picks that sentence off <see cref="SessionJoinResult.Denial"/>.
        /// </remarks>
        private static SessionJoinResult DeniedLocally(SessionEndReason reason, SessionDenial denial, string message) {
            return SessionJoinResult.Denied(reason, denial, message);
        }

        /// <summary>
        /// Which of this client's own host steps gave up, worked out from what is configured rather than
        /// from the text of the failure.
        /// </summary>
        /// <remarks>
        /// Coarser than the recorded message on purpose: a launcher that was cancelled and one that
        /// could not start both read as <see cref="SessionDenial.HostStartFailed"/> here, because the
        /// two are only told apart inside the launcher and that distinction is not worth a field the
        /// launch path has to remember to set.
        /// </remarks>
        private SessionDenial ResolveHostFailureDenial() {
            if (hostingMode == SessionHostingMode.LocalServerProcess && localServer == null) {
                return SessionDenial.NoLocalServerConfig;
            }

            if (string.IsNullOrEmpty(_hostEndpointFailure)) return SessionDenial.NoServerEndpoint;

            return SessionDenial.HostStartFailed;
        }

        private string ResolveHostFailureMessage() {
            return string.IsNullOrEmpty(_hostEndpointFailure) ? "No server endpoint." : _hostEndpointFailure;
        }

        /// <summary>Travels the connect and attach half of a join, once the server is known.</summary>
        private async Task<SessionJoinResult> JoinResolvedAsync(NetEndpoint endpoint, string normalizedCode) {
            SessionJoinResult connectResult = await ConnectAsync(endpoint);

            if (!connectResult.IsSuccess) return connectResult;

            return await _sessionClient.JoinSessionAsync(normalizedCode);
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
                return DeniedLocally(SessionEndReason.TransportLost, SessionDenial.NoClientFacade, "No client facade.");
            }

            BuildSessionClient(client);

            SessionJoinResult result = await _sessionClient.ConnectAsync(endpoint);

            if (!result.IsSuccess) {
                RequestTearDown(result.Reason);
            }

            return result;
        }

        /// <summary>
        /// Creates the session client, the client world and the claim view over a connection, once per
        /// connection.
        /// </summary>
        /// <remarks>
        /// All three register handlers on the client's router, so building a second set over the same
        /// connection would either throw or silently steal the first set's messages. The session client
        /// is told not to pump the connection: NetworkService already does that at execution order -100,
        /// so by the time this service's Update runs the inbox is drained and the clock has advanced —
        /// a second pump here would run the clock at twice wall speed and rubber-band everything drawn
        /// from it.
        /// </remarks>
        private void BuildSessionClient(NetClient client) {
            if (_sessionClient != null) return;

            _identity ??= ResolveIdentityStore().Load(_config.defaultDisplayName);

            _sessionClient = new SessionClient(client, new AnonymousAuthProvider(), _identity, pumpsClient: false);
            SubscribeToSessionClient();

            _replication = new ClientReplication(client, _netConfig, CurrentCollisionWorld());
            _claims = new ClientClaims(client);
            _adoptedLocalPeerId = ClientClaims.FreeHolderPeerId;

            RaiseSessionResourcesChanged();
        }

        /// <summary>
        /// Says the session's objects have moved, without letting one bad listener take the rest down.
        /// </summary>
        /// <remarks>
        /// Raised from a build and from a teardown alike, and a teardown is already the unhappy path —
        /// a listener throwing here would leave the service half torn down with no way to finish. The
        /// throw is logged and the remaining listeners still hear it.
        /// </remarks>
        private void RaiseSessionResourcesChanged() {
            Action listeners = OnSessionResourcesChanged;

            if (listeners == null) return;

            foreach (Delegate listener in listeners.GetInvocationList()) {
                InvokeResourcesListener((Action)listener);
            }
        }

        private void InvokeResourcesListener(Action listener) {
            try {
                listener();
            } catch (Exception exception) {
                Debug.LogError($"SessionService::RaiseSessionResourcesChanged->{exception}");
            }
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
        /// lobby, and the owner's pawn is dragged back by a correction on every single tick. A session with
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
        /// Tells the client world and the claim view which peer we are, so they can tell our pawn and
        /// our slots from everybody else's.
        /// </summary>
        /// <remarks>
        /// The peer id is only known once the server has answered the auth request and put us on a
        /// roster, which is why this is re-checked on every state change rather than done once. The
        /// first time it resolves to a real id, <c>OnSessionResourcesChanged</c> is raised a second
        /// time: the three objects a listener took references to on the build are only answerable about
        /// ownership from this moment on, and nothing else announces it.
        /// </remarks>
        private void AdoptLocalPeerId() {
            if (_sessionClient == null) return;

            SessionMember localMember = _sessionClient.LocalMember;

            if (localMember == null) return;

            if (_replication != null) {
                _replication.LocalPeerId = localMember.PeerId;
            }

            if (_claims != null) {
                _claims.LocalPeerId = localMember.PeerId;
            }

            if (localMember.PeerId == _adoptedLocalPeerId) return;

            _adoptedLocalPeerId = localMember.PeerId;
            RaiseSessionResourcesChanged();
        }

        /// <summary>
        /// Drops everything a session owns — the client world, the claim view, the session client, the
        /// in-process server — and returns the process to offline. Safe to call when there is nothing
        /// to drop.
        /// </summary>
        /// <remarks>
        /// A launched server is the one thing that is not always dropped. When the player hosting one
        /// walks out of a session other people are still in, killing it would end their game as well, so
        /// it is detached instead and left to the idle exit it was started with. Everything else — the
        /// application quitting, this service being destroyed, a host who was the last one in — still
        /// stops it, because there is nobody left for it to serve.
        /// </remarks>
        private void TearDownSession(SessionEndReason reason) {
            _isTearDownPending = false;

            // Read before the session client goes: the member list is the only thing that knows whether
            // anyone is left behind, and it is the first casualty of the teardown below.
            bool detachLocalServer = ShouldDetachLocalServer();
            bool hadResources = _sessionClient != null || _replication != null || _claims != null;

            if (_sessionClient != null) {
                // Before the client is dropped, not after: a leave still waiting out its grace is only
                // ever resolved by the client's own pump, and nothing pumps it once this field is null —
                // NetClient.Dispose unsubscribes the transport without raising a disconnect. A caller
                // awaiting LeaveSessionAsync would otherwise wait for the rest of the run.
                _sessionClient.AbandonPendingLeave();
                UnsubscribeFromSessionClient();
                _sessionClient = null;
            }

            // Claims first: disposing them raises OnClaimLost, and a handler that reacts by looking at
            // the world reads Replication.LocalPeerId — which a disposed replication no longer answers.
            _claims?.Dispose();
            _claims = null;

            _replication?.Dispose();
            _replication = null;
            _adoptedLocalPeerId = ClientClaims.FreeHolderPeerId;

            _frontDesk?.Close(reason);
            _frontDesk = null;

            // Dropped rather than kept: a player who left in the middle of a match would otherwise host or
            // join their next session predicting against the match scene they walked out of, until a phase
            // change happened to name something else.
            _collisionWorld = null;
            _currentSceneName = string.Empty;
            _hostEndpoint = NetEndpoint.None;

            // Before the shutdown: a host request still waiting on readiness has to fail now, not when
            // its budget runs out, or the menu sits on "starting a train…" for a session already gone.
            CancelHostStart();

            _networkService?.Shutdown();

            // After the shutdown, not before: the client's disconnect should reach a server that is
            // still listening, so it can retire the session rather than notice a socket going quiet.
            ReleaseLocalServer(detachLocalServer);

            if (!hadResources) return;

            RaiseSessionResourcesChanged();
        }

        /// <summary>
        /// True when the server this build launched must outlive the session being torn down.
        /// </summary>
        /// <remarks>
        /// The host is only the player who happened to press the button; the server is where everyone
        /// else's game lives. Leaving a session that still has other members in it must therefore leave
        /// the process running — the guests carry on, and the server's own idle exit reaps it once the
        /// last of them goes. Shutting the application down is the opposite case and is excluded here:
        /// nothing is left to keep the process company, and the detach would leak it.
        /// </remarks>
        private bool ShouldDetachLocalServer() {
            if (_isShuttingDown) return false;
            if (hostingMode != SessionHostingMode.LocalServerProcess) return false;
            if (_localServer == null || !_localServer.IsRunning) return false;

            return CountOtherMembers() > 0;
        }

        /// <summary>How many people other than this player are still on the session roster.</summary>
        /// <remarks>
        /// Counted rather than read off <c>Members.Count</c>, because a leave can be answered with a
        /// roster the leaver is already off: "one member left, and it is not me" has to read as a
        /// session worth keeping the server alive for, not as an empty one.
        /// </remarks>
        private int CountOtherMembers() {
            SessionMember localMember = _sessionClient?.LocalMember;
            int others = 0;

            foreach (SessionMember member in Members) {
                if (member == localMember) continue;

                others++;
            }

            return others;
        }

        /// <summary>Ends the session's hold on a launched server, by detaching it or by stopping it.</summary>
        /// <remarks>
        /// A leave does not wait for the server to finish shutting down. This runs on the Unity main
        /// thread, and the grace is the server closing its sessions and draining — a few hundred
        /// milliseconds for a healthy one, the whole budget for a wedged one, and either is a freeze the
        /// player sees. Only a shutdown blocks, because the thread that would do the killing is itself
        /// about to stop existing.
        /// </remarks>
        private void ReleaseLocalServer(bool detach) {
            if (_localServer == null) return;

            if (detach) {
                _localServer.Detach();
                return;
            }

            if (_isShuttingDown) {
                _localServer.Stop();
                return;
            }

            _localServer.BeginStop();
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
