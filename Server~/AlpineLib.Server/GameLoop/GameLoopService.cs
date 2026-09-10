using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Transport;
using AlpineLib.Server.Configuration;
using AlpineLib.Server.Hosting;
using AlpineLib.Server.Sessions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AlpineLib.Server.GameLoop {
    /// <summary>
    /// The simulation. One dedicated thread stepping every session in this process at a fixed rate, for
    /// as long as the host is up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a thread of its own.</b> The loop must step on a rhythm, and a thread-pool timer cannot
    /// promise one: it shares its pool with everything else the process is doing, so a burst of work
    /// elsewhere would show up in the game as jitter. A dedicated thread costs one thread and buys a tick
    /// that nothing else can crowd out.
    /// </para>
    /// <para>
    /// <b>Order within a step matters.</b> Queued work from other threads runs first, so a request that
    /// asked a question is answered against the state the step is about to advance rather than halfway
    /// through it. Then the connection is pumped — which is where the transport delivers, where
    /// authentication verdicts land and where the front desk routes create, join, input and chat — and
    /// only then do the sessions advance over the traffic that just arrived. Snapshots go out on their
    /// own cadence inside each session's world, at the snapshot rate rather than the tick rate.
    /// </para>
    /// <para>
    /// <b>Catch-up is capped.</b> A stalled process — a long garbage collection, a suspended container,
    /// a breakpoint — must not be repaid as a burst of simulation that stalls it further. Beyond
    /// <see cref="MaxCatchUpSteps"/> the missed time is dropped: the world jumps once, which players read
    /// as a hitch, instead of the server spending the next second catching up and hitching for all of it.
    /// </para>
    /// <para>
    /// <b>It announces itself and it lets itself go.</b> One readiness line on stdout, carrying the port
    /// the socket actually bound, is how a launcher learns the server is up — see
    /// <see cref="ReadinessLine"/>. An idle-exit window is how a server nobody dialled stops again
    /// without an operator; both exist for the local-server case and are inert on a dedicated deployment
    /// that leaves the window at zero.
    /// </para>
    /// </remarks>
    public sealed class GameLoopService : BackgroundService {
        /// <summary>Steps one wake-up may run before the remaining backlog is abandoned.</summary>
        public const int MaxCatchUpSteps = 5;

        /// <summary>How long the loop keeps pumping after the sessions are told to close.</summary>
        /// <remarks>
        /// <c>SessionClosing</c> goes out reliably, which means it is queued rather than sent. Tearing
        /// the socket down in the same breath would discard it and leave every client staring at a
        /// timeout instead of a reason, so the loop keeps stepping long enough for the notice to fly.
        /// </remarks>
        public const double ShutdownDrainSeconds = 0.3;

        /// <summary>Name of the loop thread, so a stack dump names the simulation rather than "Thread-4".</summary>
        public const string LoopThreadName = "alpine-game-loop";

        private readonly NetServer _server;
        private readonly SessionRegistry _registry;
        private readonly GameThreadInbox _inbox;
        private readonly GameLoopHeartbeat _heartbeat;
        private readonly ServerConfigBundle _config;
        private readonly ServerRuntimeOptions _options;
        private readonly INetTransport _transport;
        private readonly ILogger<GameLoopService> _logger;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly IdleShutdownTimer _idleTimer;
        private readonly TaskCompletionSource<bool> _loopExited =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private Thread _loopThread;
        private int _readyPort = -1;

        /// <param name="transport">
        /// The socket under <paramref name="server"/>. Held only to read the port it actually bound,
        /// which is the one thing a caller who passed port zero cannot know any other way.
        /// </param>
        /// <param name="lifetime">
        /// Optional on purpose: a test drives this service directly, with no host to shut down.
        /// </param>
        public GameLoopService(
            NetServer server,
            SessionRegistry registry,
            GameThreadInbox inbox,
            GameLoopHeartbeat heartbeat,
            ServerConfigBundle config,
            ServerRuntimeOptions options,
            INetTransport transport,
            IHostApplicationLifetime lifetime,
            ILogger<GameLoopService> logger) {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
            _heartbeat = heartbeat ?? throw new ArgumentNullException(nameof(heartbeat));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _lifetime = lifetime;
            _idleTimer = new IdleShutdownTimer(_options.IdleExitSeconds);
        }

        /// <summary>Seconds per simulation step, as the configuration asked for.</summary>
        public float StepSeconds => _config.Net.ServerTickInterval;

        /// <summary>
        /// The port the readiness line announced, or -1 before the socket is up. The bound port rather
        /// than the configured one, which is the whole point when the configured one was zero.
        /// </summary>
        public int ReadyPort => Volatile.Read(ref _readyPort);

        /// <summary>The idle window this loop stops itself on. Disabled when the option is zero.</summary>
        public IdleShutdownTimer IdleTimer => _idleTimer;

        /// <inheritdoc />
        protected override Task ExecuteAsync(CancellationToken stoppingToken) {
            _inbox.WorkFailed += HandleInboxFailure;

            _loopThread = new Thread(() => RunLoop(stoppingToken)) {
                Name = LoopThreadName,
                IsBackground = false,
                Priority = ThreadPriority.AboveNormal
            };

            _loopThread.Start();
            return _loopExited.Task;
        }

        private void RunLoop(CancellationToken stoppingToken) {
            try {
                StartServer();
                StepUntilCancelled(stoppingToken);
            }
            catch (Exception error) {
                // Peer-scoped faults are already absorbed below this line — a malformed packet cannot get
                // here. Anything that does is a bug in the simulation, and a server that kept stepping
                // through one would be handing every session a world nobody can trust. Stop, and let the
                // orchestrator bring up a process that has not been in that state.
                _logger.LogCritical(error, "The game loop stopped on an unhandled fault.");
                _lifetime?.StopApplication();
            }
            finally {
                ShutDown();
                _loopExited.TrySetResult(true);
            }
        }

        private void StartServer() {
            _server.Start();
            _heartbeat.MarkStarted();
            _logger.LogInformation(
                "Game loop running at {TickRate} Hz, snapshots at {SnapshotRate} Hz, config from '{ConfigPath}'.",
                _config.Net.ServerTickRate, _config.Net.SnapshotRate, _config.SourcePath);
            AnnounceReadiness();
        }

        /// <summary>
        /// Writes the one line a launcher is tailing for, once the socket is accepting traffic.
        /// </summary>
        /// <remarks>
        /// Straight to stdout and flushed rather than through the logger, because a logger's format,
        /// level and destination are a deployment's business and this line is a contract. Flushing
        /// matters: a redirected pipe buffers, and a launcher waiting on a buffered line waits forever.
        /// </remarks>
        private void AnnounceReadiness() {
            int port = ResolveBoundPort();

            if (port <= 0) {
                // Nothing useful to say. Announcing the configured zero would tell a launcher to dial port
                // zero, which is worse than the timeout it gets from silence.
                _logger.LogWarning(
                    "The transport cannot report the port it bound; no readiness line was written. A launcher waiting on one will time out.");
                return;
            }

            // The line first, the port second. A launcher and a test both learn the port from
            // <see cref="ReadyPort"/>, and publishing it before the line was written would let either of
            // them act on a readiness nothing has announced yet.
            Console.Out.WriteLine(ReadinessLine.Format(port));
            Console.Out.Flush();
            Volatile.Write(ref _readyPort, port);
        }

        /// <summary>
        /// The port the socket actually bound.
        /// </summary>
        /// <remarks>
        /// Asked of the transport rather than read off the configuration, because the case that matters is
        /// the one where the configuration said zero: the operating system picked the port and the
        /// transport is the only thing that knows which. The configured value is the fallback for a
        /// transport that cannot answer, and only when it names a real port.
        /// </remarks>
        private int ResolveBoundPort() {
            if (_transport is LiteNetTransport liteNetTransport) {
                return liteNetTransport.LocalPort;
            }

            return _options.PortOverride ?? _config.Net.Port;
        }

        private void StepUntilCancelled(CancellationToken stoppingToken) {
            double stepSeconds = StepSeconds;
            double backlogCeiling = stepSeconds * MaxCatchUpSteps;
            double accumulatedSeconds = 0.0;
            long previousTimestamp = Stopwatch.GetTimestamp();

            while (!stoppingToken.IsCancellationRequested) {
                long now = Stopwatch.GetTimestamp();
                double sliceSeconds = Stopwatch.GetElapsedTime(previousTimestamp, now).TotalSeconds;
                previousTimestamp = now;

                accumulatedSeconds = Math.Min(accumulatedSeconds + sliceSeconds, backlogCeiling);
                accumulatedSeconds = RunDueSteps(accumulatedSeconds, stepSeconds);

                if (HasGoneIdle(sliceSeconds)) {
                    return;
                }

                WaitForNextStep(accumulatedSeconds, stepSeconds, stoppingToken);
            }
        }

        private double RunDueSteps(double accumulatedSeconds, double stepSeconds) {
            double remaining = accumulatedSeconds;

            for (int stepIndex = 0; stepIndex < MaxCatchUpSteps && remaining >= stepSeconds; stepIndex++) {
                remaining -= stepSeconds;
                Step((float)stepSeconds);
            }

            return remaining;
        }

        private void Step(float deltaSeconds) {
            _inbox.Drain();
            _server.Update(deltaSeconds);
            _registry.TickAll(deltaSeconds);
            _registry.SweepLifetimes();
            _heartbeat.Beat(_server.Tick);
        }

        /// <summary>
        /// Folds the wall-clock slice into the idle window and asks the host to stop when it has run out.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The measured slice is counted, not the steps that were due in it: an idle server sleeps a full
        /// step between wake-ups and a stalled one has its backlog capped, so counting steps would make
        /// the window depend on the tick rate and stretch across every freeze.
        /// </para>
        /// <para>
        /// Only connections that have authenticated hold the process open. A socket that completed the
        /// transport handshake and then said nothing — a crashed client's half-open link, a stale client
        /// dialling a recycled ephemeral port — is nobody this server is up for, and counting it would
        /// leave an abandoned local server running until the machine is rebooted. The price is that a
        /// window shorter than a handshake could stop a client mid-authentication, which is why the
        /// window an operator or a launcher sets is measured in tens of seconds.
        /// </para>
        /// </remarks>
        private bool HasGoneIdle(double sliceSeconds) {
            if (!_idleTimer.Observe(_registry.AuthenticatedPeerCount, sliceSeconds)) {
                return false;
            }

            _logger.LogInformation(
                "No connections for {IdleSeconds:0.#} s; stopping the server.", _idleTimer.IdleExitSeconds);
            _lifetime?.StopApplication();
            return true;
        }

        private static void WaitForNextStep(double accumulatedSeconds, double stepSeconds, CancellationToken stoppingToken) {
            double remainingSeconds = stepSeconds - accumulatedSeconds;

            if (remainingSeconds <= 0.0) {
                return;
            }

            // Waiting on the token rather than sleeping means a shutdown is acted on immediately instead
            // of after the rest of this step's slice has been slept away.
            int waitMs = (int)Math.Ceiling(remainingSeconds * 1000.0);
            stoppingToken.WaitHandle.WaitOne(Math.Max(1, waitMs));
        }

        /// <summary>
        /// The polite ending: tell every session it is closing, keep pumping until the notices are off the
        /// wire, then stop accepting work and release the socket.
        /// </summary>
        private void ShutDown() {
            _inbox.WorkFailed -= HandleInboxFailure;
            _logger.LogInformation("Closing {SessionCount} session(s) and draining the wire.", _registry.Sessions.Count);

            SafeCloseSessions();
            DrainAfterClose();

            _inbox.Close();
            _heartbeat.MarkStopped();
            _registry.Dispose();
            _server.Stop();
            _logger.LogInformation("Game loop stopped.");
        }

        private void SafeCloseSessions() {
            try {
                _registry.CloseAll(SessionEndReason.HostClosed);
            }
            catch (Exception error) {
                _logger.LogError(error, "Failed to close sessions cleanly; shutting the socket down anyway.");
            }
        }

        private void DrainAfterClose() {
            double stepSeconds = StepSeconds;
            int drainSteps = (int)Math.Ceiling(ShutdownDrainSeconds / stepSeconds);

            for (int stepIndex = 0; stepIndex < drainSteps; stepIndex++) {
                DrainStep((float)stepSeconds);
                Thread.Sleep((int)Math.Max(1.0, stepSeconds * 1000.0));
            }
        }

        private void DrainStep(float deltaSeconds) {
            try {
                _inbox.Drain();
                _server.Update(deltaSeconds);
                _registry.TickAll(deltaSeconds);
                _registry.SweepLifetimes();
            }
            catch (Exception error) {
                _logger.LogError(error, "A fault during shutdown drain; continuing to stop.");
            }
        }

        private void HandleInboxFailure(Exception error) {
            _logger.LogError(error, "Work posted to the game thread threw.");
        }
    }
}
