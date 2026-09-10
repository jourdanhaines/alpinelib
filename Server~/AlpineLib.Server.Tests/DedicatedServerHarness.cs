using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Sessions.Spawning;
using AlpineLib.Netcode.Transport;
using AlpineLib.Server.Configuration;
using AlpineLib.Server.GameLoop;
using AlpineLib.Server.Hosting;
using AlpineLib.Server.Sessions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// A real dedicated server — socket, registry, fixed-step loop thread — on an ephemeral port, plus
    /// however many clients a test wants to point at it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The loop runs on its own thread here exactly as it does in production, so these tests are the
    /// only place the threading contract is actually exercised: the clients are pumped from the test
    /// thread while the server steps on another, and everything the test wants to know about server
    /// state is asked for through the inbox rather than read across the boundary.
    /// </para>
    /// <para>
    /// A real socket rather than the in-memory fake, because the fake's queues are single-threaded by
    /// design and the loop thread is exactly what would race them. Port zero keeps the runs from
    /// colliding with each other or with a real server.
    /// </para>
    /// </remarks>
    internal sealed class DedicatedServerHarness : IDisposable {
        private const int StartTimeoutMs = 10_000;
        private const int PumpTimeoutMs = 10_000;
        private const float ClientStepSeconds = 0.005f;

        private readonly ServerConfigBundle _config;
        private readonly LiteNetTransport _serverTransport;
        private readonly NetServer _server;
        private readonly SessionRegistry _registry;
        private readonly GameThreadInbox _inbox = new GameThreadInbox();
        private readonly GameLoopHeartbeat _heartbeat = new GameLoopHeartbeat();
        private readonly GameLoopService _loop;
        private readonly StubHostLifetime _lifetime = new StubHostLifetime();
        private readonly List<HarnessClient> _clients = new List<HarnessClient>();

        private bool _isStopped;

        private DedicatedServerHarness(
            ServerConfigBundle config,
            ServerRuntimeOptions options,
            ISessionModuleFactory moduleFactory,
            Func<ServerConfigBundle, ISpawnPlacement> placementFactory,
            ILogger<SessionRegistry> registryLogger) {
            _config = config;
            _serverTransport = new LiteNetTransport(config.Net.DisconnectTimeoutMs);
            _server = new NetServer(_serverTransport, config.Net);

            _registry = new SessionRegistry(
                _server,
                config,
                new AnonymousAuthValidator(config.Session.DefaultDisplayName),
                SceneGeometryLibrary.Empty,
                moduleFactory,
                placementFactory,
                options.MaxSessions,
                ReadWallClockUnixMs,
                registryLogger ?? NullLogger<SessionRegistry>.Instance);

            _loop = new GameLoopService(
                _server,
                _registry,
                _inbox,
                _heartbeat,
                config,
                options,
                _serverTransport,
                _lifetime,
                NullLogger<GameLoopService>.Instance);
        }

        /// <summary>The address clients dial, known only once the ephemeral port is bound.</summary>
        public NetEndpoint Endpoint => NetEndpoint.Direct("127.0.0.1", _serverTransport.LocalPort);

        /// <summary>Configuration the server and its clients share.</summary>
        public ServerConfigBundle Config => _config;

        /// <summary>The front desk, for a test that wants to read it through <see cref="Query{TResult}"/>.</summary>
        public SessionRegistry Registry => _registry;

        /// <summary>The loop under test, for the readiness port and the idle timer.</summary>
        public GameLoopService Loop => _loop;

        /// <summary>True once the loop asked the host to stop — which is how an idle exit shows up.</summary>
        public bool WasStopRequested => _lifetime.WasStopRequested;

        /// <summary>Starts a server with the shipped session cap and no game module.</summary>
        public static DedicatedServerHarness Start() {
            return Start(BuildConfig(), new ServerRuntimeOptions(), null);
        }

        /// <summary>Starts a server that will host at most the given number of sessions.</summary>
        public static DedicatedServerHarness Start(int maxSessions) {
            return Start(BuildConfig(), new ServerRuntimeOptions { MaxSessions = maxSessions }, null);
        }

        /// <summary>Starts a server hosting a game's own module.</summary>
        public static DedicatedServerHarness Start(ISessionModuleFactory moduleFactory) {
            return Start(BuildConfig(), new ServerRuntimeOptions(), moduleFactory);
        }

        /// <summary>Starts a server over a hand-built configuration and option set.</summary>
        /// <param name="placementFactory">
        /// Where arrivals are seated, or null for the desk's own default — which is also the only path
        /// that reports an export whose spawn points went missing.
        /// </param>
        /// <param name="registryLogger">Where the desk writes, or null to throw its lines away.</param>
        public static DedicatedServerHarness Start(
            ServerConfigBundle config,
            ServerRuntimeOptions options,
            ISessionModuleFactory moduleFactory,
            Func<ServerConfigBundle, ISpawnPlacement> placementFactory = null,
            ILogger<SessionRegistry> registryLogger = null) {
            DedicatedServerHarness harness =
                new DedicatedServerHarness(config, options, moduleFactory, placementFactory, registryLogger);
            harness.StartLoop();
            return harness;
        }

        /// <summary>The configuration every harness runs by unless a test hands one in.</summary>
        /// <param name="spawn">Spawn rules, or null for the shipped ring of server-simulated pawns.</param>
        public static ServerConfigBundle BuildConfig(SpawnSettingsDocument spawn = null) {
            ServerConfigDocument document = new ServerConfigDocument();

            // Port zero asks the operating system for a free port, so tests never collide with each other
            // or with a real server on 9050.
            document.Net.Port = 0;
            document.Net.GameProtocolName = "alpine-host-test";
            document.Net.MovementProfiles.Add(new MovementProfileDocument { DisplayName = "Pawn" });
            document.Session.Lobby.LobbySceneName = "Game";

            if (spawn != null) {
                document.Spawn = spawn;
            }

            return document.ToBundle("harness");
        }

        /// <summary>Stands up a client that will authenticate under a fresh identity.</summary>
        public HarnessClient AddClient(string displayName) {
            return AddClient(displayName, PlayerId.NewId());
        }

        /// <summary>Stands up a client under a specific player id, which is what makes a rejoin possible.</summary>
        public HarnessClient AddClient(string displayName, PlayerId playerId) {
            HarnessClient client = new HarnessClient(displayName, playerId, _config.Net, _config.Chat);
            _clients.Add(client);
            return client;
        }

        /// <summary>Pumps the clients until the request settles, then hands back its result.</summary>
        /// <remarks>
        /// Reading the result synchronously is safe only because the pump is what completes it: by the
        /// time this returns the task is already settled. Awaiting instead would deadlock, since the
        /// thread that must keep pumping is the one that would be waiting.
        /// </remarks>
#pragma warning disable xUnit1031
        public TResult Complete<TResult>(Task<TResult> request) {
            Assert.True(PumpUntil(() => request.IsCompleted), "A client request never completed.");
            return request.GetAwaiter().GetResult();
        }
#pragma warning restore xUnit1031

        /// <summary>Pumps the clients until a result-less request settles.</summary>
        public void Complete(Task request) {
            Assert.True(PumpUntil(() => request.IsCompleted), "A client request never completed.");
        }

        /// <summary>Pumps every client until the condition holds or the timeout expires.</summary>
        public bool PumpUntil(Func<bool> condition) {
            return PumpUntil(condition, PumpTimeoutMs);
        }

        /// <summary>Pumps every client until the condition holds or the given window expires.</summary>
        public bool PumpUntil(Func<bool> condition, int timeoutMs) {
            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.ElapsedMilliseconds < timeoutMs) {
                PumpOnce();

                if (condition()) {
                    return true;
                }

                Thread.Sleep(1);
            }

            PumpOnce();
            return condition();
        }

        /// <summary>Pumps a fixed number of client steps, for asserting that nothing further arrives.</summary>
        public void Pump(int steps) {
            for (int step = 0; step < steps; step++) {
                PumpOnce();
                Thread.Sleep(2);
            }
        }

        /// <summary>
        /// Runs a question on the game thread and pumps the clients until it is answered — the only
        /// legitimate way for a test to read server state.
        /// </summary>
#pragma warning disable xUnit1031
        public TResult Query<TResult>(Func<TResult> question) {
            Task<TResult> answer = _inbox.PostAsync(question);
            Assert.True(PumpUntil(() => answer.IsCompleted), "The game thread never answered.");
            return answer.GetAwaiter().GetResult();
        }
#pragma warning restore xUnit1031

        /// <summary>Copies the session directory, exactly as an admin surface would.</summary>
        public DirectorySnapshot CaptureDirectory() {
            return Query(_registry.BuildSnapshot);
        }

        /// <summary>Shuts the loop down the way the host does, and waits for it.</summary>
        public void Stop() {
            if (_isStopped) {
                return;
            }

            _isStopped = true;
            Task stopping = _loop.StopAsync(CancellationToken.None);

            // The shutdown broadcast has to reach clients that are only pumped from this thread, so the
            // wait doubles as a pump rather than blocking on the task outright.
            PumpUntil(() => stopping.IsCompleted);
            Assert.True(stopping.IsCompleted, "The game loop never stopped.");
        }

        /// <inheritdoc />
        public void Dispose() {
            Stop();

            for (int clientIndex = 0; clientIndex < _clients.Count; clientIndex++) {
                _clients[clientIndex].Dispose();
            }

            _server.Dispose();
            _serverTransport.Dispose();
        }

        private static long ReadWallClockUnixMs() {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private void StartLoop() {
            IHostedService hosted = _loop;
            hosted.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.ElapsedMilliseconds < StartTimeoutMs && !IsListening()) {
                Thread.Sleep(1);
            }

            Assert.True(IsListening(), "The game loop never bound its socket.");
        }

        private bool IsListening() {
            return _heartbeat.IsRunning && _serverTransport.LocalPort > 0;
        }

        private void PumpOnce() {
            for (int clientIndex = 0; clientIndex < _clients.Count; clientIndex++) {
                _clients[clientIndex].Tick(ClientStepSeconds);
            }
        }
    }
}
