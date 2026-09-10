using System;
using System.Collections.Generic;
using AlpineLib.Chat;
using AlpineLib.Chat.Transport;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Claims;
using AlpineLib.Netcode.Sessions.Spawning;
using AlpineLib.Netcode.Transport;
using AlpineLib.Server.Sessions.Spawning;
using Microsoft.Extensions.Logging;

namespace AlpineLib.Server.Sessions {
    /// <summary>
    /// One live session as this process holds it: the session itself, the authoritative world its members
    /// move around in, the slots they can take, the chat room they talk in, and whatever the game hung
    /// off all of that.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The pieces are bound together here rather than inside <see cref="SessionHost"/> because the host is
    /// shared with the Unity listen-host build, where the world and the chat room are wired by the
    /// engine-side services instead. What is common to both is the session; what differs is who owns the
    /// pieces around it, and on a dedicated server that is this class.
    /// </para>
    /// <para>
    /// <b>Nothing here claims a message id.</b> Several entries share one socket, and a router allows one
    /// handler per id, so <see cref="SessionRegistry"/> claims the ids once and forwards to the right
    /// entry. That is why the replication world and the claim registry are never attached to the router
    /// and the chat transport is never started: all three are driven through their pump-it-in entry
    /// points instead.
    /// </para>
    /// <para>
    /// <b>Collision geometry follows the phase.</b> A session is a peer set that changes scene rather
    /// than a scene that changes peers, so the collision world under it has to be swapped as the phase
    /// moves — lobby geometry in the lobby, the match scene's geometry from the moment it starts loading.
    /// The swap happens here and not in the registry because the entry is the only thing that owns both
    /// the session and the world it simulates in. A scene with no export resolves to flat ground at
    /// y = 0 and says so once, which keeps a forgotten export a diagnosable oddity rather than a crash.
    /// </para>
    /// </remarks>
    public sealed class SessionEntry : IDisposable {
        private readonly SessionHost _host;
        private readonly ServerReplication _replication;
        private readonly ServerClaimRegistry _claims;
        private readonly SessionPawnSpawner _spawner;
        private readonly SessionHostChatAdapter _chatHost;
        private readonly ChatServerEnvelopeTransport _chatTransport;
        private readonly ChatServerService _chatService;
        private readonly SceneGeometryLibrary _geometry;
        private readonly ILogger _logger;
        private readonly ISessionModule _module;

        private string _currentSceneName = string.Empty;
        private bool _disposed;

        /// <summary>Stands up the world, the slots and the chat room around an already-open session.</summary>
        /// <param name="geometry">Exported collision geometry, keyed by scene. Never null; use <see cref="SceneGeometryLibrary.Empty"/>.</param>
        /// <param name="spawn">What an arriving member is given a body as.</param>
        /// <param name="placement">
        /// Where this session's arrivals appear. One instance per entry: a placement carries the seat
        /// counter of the session it belongs to, so sharing one would seat two sessions off one ring.
        /// </param>
        /// <param name="moduleFactory">The game's own simulation, or null for a server that adds nothing.</param>
        /// <param name="logger">Where an unresolved scene is reported. Never null; use a null logger.</param>
        public SessionEntry(
            SessionHost host,
            NetServer server,
            MovementValidator validator,
            ChatSettings chatSettings,
            SceneGeometryLibrary geometry,
            SpawnSettings spawn,
            ISpawnPlacement placement,
            ISessionModuleFactory moduleFactory,
            Func<long> clock,
            ILogger logger) {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (server == null) {
                throw new ArgumentNullException(nameof(server));
            }

            if (spawn == null) {
                throw new ArgumentNullException(nameof(spawn));
            }

            _replication = new ServerReplication(server, ResolveBroadcastPeers, validator);

            // The game's authority rule over slots is asked for before the registry exists, not fitted
            // afterwards: a registry that is live and unfiltered for even one message has already
            // answered a request it should have refused.
            _claims = new ServerClaimRegistry(server, ResolveBroadcastPeers, moduleFactory?.BuildClaimValidator(host));
            _spawner = new SessionPawnSpawner(host, _replication, spawn.PawnPrefabId, spawn.PawnAuthority, placement);
            _chatHost = new SessionHostChatAdapter(host, clock);
            _chatTransport = new ChatServerEnvelopeTransport(server, ResolvePlayerForPeer, ResolvePeerForPlayer);
            _chatService = new ChatServerService(_chatHost, _chatTransport, chatSettings);

            _host.OnMemberNeedsKeyframe += HandleMemberNeedsKeyframe;
            _host.OnPhaseChanged += HandlePhaseChanged;

            // The lobby is where a session starts and where it spends most of its life, so its geometry is
            // resolved now rather than waiting for a phase change that may never come. A lobby with no
            // scene name asks for nothing and gets nothing: the replication world is already flat ground,
            // and that is the honest answer to a config that never named a scene to look up.
            UseSceneGeometry(ResolveLobbySceneName());

            _chatHost.Start();
            _chatService.Start();

            // Last, and with everything above already readable: a module is handed the entry it belongs to
            // so it can reach the world and the slots it is about to simulate over.
            _module = BuildModule(moduleFactory);
            AttachModule();
        }

        /// <summary>The session this entry is built around.</summary>
        public SessionHost Host => _host;

        /// <summary>The authoritative world of this session, and of no other.</summary>
        public ServerReplication Replication => _replication;

        /// <summary>This session's claimable slots — one lever, one seat, one station apiece.</summary>
        public ServerClaimRegistry Claims => _claims;

        /// <summary>Gives this session's arrivals a body and takes it away again when they leave.</summary>
        public SessionPawnSpawner Spawner => _spawner;

        /// <summary>The game's own simulation for this session, or null when the server hosts none.</summary>
        public ISessionModule Module => _module;

        /// <summary>This session's chat pipeline.</summary>
        public ChatServerService ChatService => _chatService;

        /// <summary>Key of this session's chat room.</summary>
        public string ChatRoomKey => _chatHost.RoomKey;

        /// <summary>
        /// Scene the world was last resolved for. Names the ask, not the outcome: a scene with no export
        /// still appears here while the world under it is flat ground.
        /// </summary>
        public string CurrentSceneName => _currentSceneName;

        /// <summary>One step of the session, its world and whatever the game hung off it.</summary>
        public void Tick(uint serverTick, float deltaSeconds) {
            _host.Tick(deltaSeconds);

            if (!_host.IsOpen) {
                return;
            }

            _replication.Tick(serverTick, deltaSeconds);
            RunModule(() => _module?.Tick(serverTick, deltaSeconds), "step");
        }

        /// <summary>A connection was accepted into this session. Told to the game after the roster knows.</summary>
        public void OnPeerJoined(PeerHandle peer) {
            RunModule(() => _module?.OnPeerJoined(peer), "seat an arrival in");
        }

        /// <summary>
        /// A connection is leaving this session, gracefully or otherwise.
        /// </summary>
        /// <remarks>
        /// Called while the peer is still on the roster and before the host retires the member, because
        /// that is the last moment the peer handle means anything: the departure announcement carries no
        /// peer id, so a slot released from it would be released on behalf of nobody.
        /// </remarks>
        public void OnPeerLeft(PeerHandle peer) {
            _replication.OnPeerLeft(peer);
            _claims.ReleaseAllHeldBy(peer);
            RunModule(() => _module?.OnPeerLeft(peer), "retire a departure from");
        }

        /// <summary>Hands a chat frame that arrived on one of this session's connections to the pipeline.</summary>
        public void ReceiveChatPayload(PeerHandle sender, ArraySegment<byte> payload) {
            _chatTransport.Receive(sender, payload);
        }

        /// <inheritdoc />
        public void Dispose() {
            if (_disposed) {
                return;
            }

            _disposed = true;

            UnhookHostEvents();
            _module?.Dispose();
            DisposeOwnedParts();
        }

        /// <summary>
        /// Asks the game for this session's module, unwinding the entry if the game refuses.
        /// </summary>
        /// <remarks>
        /// A factory that throws leaves an entry nobody will ever hold, so nothing else can dispose the
        /// chat pipeline it has already started or the host events it has already subscribed to. It
        /// cleans up after itself and lets the throw travel on, where the front desk turns it into a
        /// refused create rather than a stopped process.
        /// </remarks>
        private ISessionModule BuildModule(ISessionModuleFactory moduleFactory) {
            try {
                return moduleFactory?.Create(this);
            }
            catch (Exception) {
                _disposed = true;
                UnhookHostEvents();
                DisposeOwnedParts();
                throw;
            }
        }

        /// <summary>
        /// Runs one of the game's callbacks at this session's expense rather than the process's.
        /// </summary>
        /// <remarks>
        /// A module is the game's own code on the loop thread, and the loop's catch-all above this
        /// stops the whole box: without this guard one player's join throwing on a bad car index would
        /// take every other session on the server down with it. A throw closes this session and nothing
        /// else, and the sweep retires it on the next step.
        /// </remarks>
        private void RunModule(Action call, string what) {
            try {
                call();
            }
            catch (Exception error) {
                _logger.LogError(error, "The game could not {What} session {SessionId}; the session is closing.",
                    what, _host.SessionId);
                _host.Close(SessionEndReason.HostClosed);
            }
        }

        /// <summary>
        /// Tells the module its entry is finished, unwinding the entry the same way a refused
        /// <c>Create</c> does.
        /// </summary>
        private void AttachModule() {
            if (_module == null) {
                return;
            }

            try {
                _module.Attached();
            }
            catch (Exception) {
                _disposed = true;
                UnhookHostEvents();
                _module.Dispose();
                DisposeOwnedParts();
                throw;
            }
        }

        private void UnhookHostEvents() {
            _host.OnMemberNeedsKeyframe -= HandleMemberNeedsKeyframe;
            _host.OnPhaseChanged -= HandlePhaseChanged;
        }

        private void DisposeOwnedParts() {
            _spawner.Dispose();
            _chatService.Stop();
            _chatHost.Stop();
            _chatTransport.Dispose();
        }

        private IReadOnlyList<PeerHandle> ResolveBroadcastPeers() {
            return _host.ConnectedPeers;
        }

        private PlayerId ResolvePlayerForPeer(PeerHandle peer) {
            SessionMember member = _host.FindMemberByPeer(peer);
            return member == null ? PlayerId.None : member.PlayerId;
        }

        private PeerHandle ResolvePeerForPlayer(PlayerId player) {
            SessionMember member = _host.FindMember(player);

            if (member == null || !member.IsConnected) {
                return PeerHandle.None;
            }

            return new PeerHandle(member.PeerId);
        }

        /// <summary>
        /// Brings a newcomer up to date on which slots are already taken.
        /// </summary>
        /// <remarks>
        /// The world half of the same catch-up lives in <see cref="SessionPawnSpawner"/>, which listens to
        /// this event on its own. Only the slots are sent here.
        /// </remarks>
        private void HandleMemberNeedsKeyframe(SessionMember member) {
            _claims.SendKeyframeTo(new PeerHandle(member.PeerId));
        }

        /// <summary>
        /// Moves the collision world to whichever scene the new phase puts the members in.
        /// </summary>
        /// <remarks>
        /// Loading is where the swap happens, not activation: clients start loading the match scene on
        /// <c>MatchLoad</c> and the movers in it are entities the ready barrier expects to already exist,
        /// so waiting for <see cref="SessionPhase.MatchActive"/> would spawn a scene's platforms into a
        /// world the players are already standing in. Results holds the match geometry, because the
        /// players are still standing on it while the scoreboard is up.
        /// </remarks>
        private void HandlePhaseChanged(SessionPhase phase) {
            if (phase == SessionPhase.MatchLoading || phase == SessionPhase.MatchActive) {
                UseSceneGeometry(ResolveMatchSceneName());
                return;
            }

            if (phase != SessionPhase.Lobby) {
                return;
            }

            UseSceneGeometry(ResolveLobbySceneName());
        }

        /// <summary>
        /// Points the replication world at a scene's exported geometry, or at flat ground when the scene
        /// has no export.
        /// </summary>
        /// <remarks>
        /// Re-entry on the same scene is skipped, and that is load-bearing rather than an optimisation:
        /// <c>MatchLoading</c> and <c>MatchActive</c> both name the same scene, and swapping twice would
        /// despawn every mover and respawn it under fresh entity ids halfway through the ready barrier.
        /// </remarks>
        private void UseSceneGeometry(string sceneName) {
            string requested = sceneName ?? string.Empty;

            if (string.Equals(requested, _currentSceneName, StringComparison.Ordinal)) {
                return;
            }

            _currentSceneName = requested;

            if (_geometry.TryResolve(requested, out CollisionWorld world)) {
                _replication.UseWorld(world);
                _logger.LogInformation("Session {SessionId} is simulating scene '{SceneName}' ({MoverCount} mover(s)).",
                    _host.SessionId, requested, world.Movers.Count);
                return;
            }

            _replication.UseWorld(CollisionWorld.Flat(0f));
            _logger.LogWarning("No collision geometry was exported for scene '{SceneName}'; session {SessionId} falls back to flat ground at y = 0.",
                requested, _host.SessionId);
        }

        /// <summary>Scene the session itself lives in between matches.</summary>
        private string ResolveLobbySceneName() {
            LobbyConfigData lobby = _host.Config == null ? null : _host.Config.Lobby;
            return lobby == null ? string.Empty : lobby.LobbySceneName ?? string.Empty;
        }

        /// <summary>
        /// Scene of the match being loaded or played. Falls back to the lobby scene, because a phase
        /// change with no match context behind it means the launch is already unwinding.
        /// </summary>
        private string ResolveMatchSceneName() {
            MatchContextData match = _host.CurrentMatch;

            if (match == null || string.IsNullOrEmpty(match.SceneName)) {
                return ResolveLobbySceneName();
            }

            return match.SceneName;
        }
    }
}
