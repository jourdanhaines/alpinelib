using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Netcode.Transport;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace AlpineLib.Sessions {
    /// <summary>
    /// Starts the dedicated server as a child process of this one and reports the endpoint it came up
    /// on, so a single player can host a real session without a separate terminal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole point is that the host plays against the same server a guest does. A listen host
    /// short-circuits half the stack; this does not, so a bug that only appears across a socket appears
    /// for the host too, on the machine of whoever is most likely to be able to debug it.
    /// </para>
    /// <para>
    /// Startup is a handshake, not a spawn. The server prints one readiness line — <c>[ready] port=N</c>
    /// — once its socket is accepting, and only then is the endpoint known: a server asked for an
    /// ephemeral port is the whole reason the port travels back rather than being assumed. The parser
    /// here is a deliberate duplicate of the server's own <c>ReadinessLine</c>, which lives in an
    /// assembly the editor cannot reference; the two must stay each other's inverse.
    /// </para>
    /// <para>
    /// Everything about the lifetime is defensive, because an orphaned server holds the port and the
    /// next launch inherits the failure. It dies with the process, with a domain reload and with play
    /// mode, and the server is additionally told to exit on its own after an idle stretch in case all
    /// three are skipped by a crash. Each try lives in its own <see cref="LocalServerAttempt"/>, so a
    /// superseded attempt can only ever reap the process it started itself.
    /// </para>
    /// <para>
    /// The one ending that is not a kill is <see cref="Detach"/>, for a host who walks out of a session
    /// other players are still in. It hands the server over to its own idle exit and forgets it, so
    /// every hook above finds nothing to reap and the guests keep the game they were in.
    /// </para>
    /// </remarks>
    public sealed class LocalServerLauncher : IDisposable {
        /// <summary>Marker opening the server's readiness line. Mirrors the server's own contract.</summary>
        public const string ReadyLinePrefix = "[ready]";

        private const string ReadyPortToken = "port=";
        private const string LoopbackHost = "127.0.0.1";
        private const string SetsidFileName = "setsid";

        private const int MinPort = 1;
        private const int MaxPort = 65535;
        private const int EphemeralPort = 0;

        /// <summary><c>X_OK</c>: the mode bit <c>access(2)</c> is asked about.</summary>
        private const int ExecutePermission = 1;

        private static volatile bool _isAccessApiUsable = true;

        private readonly LocalServerConfig _config;
        private readonly object _gate = new object();

        private LocalServerAttempt _attempt;
        private NetEndpoint _endpoint;
        private bool _areHooksInstalled;
        private bool _isDisposed;

        /// <summary>Creates a launcher for one authored server configuration.</summary>
        public LocalServerLauncher(LocalServerConfig config) {
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
        }

        /// <summary>True while a server started by this launcher is still alive.</summary>
        public bool IsRunning => CurrentAttempt()?.IsRunning == true;

        /// <summary>The endpoint the running server is listening on, or none while there is no server.</summary>
        /// <remarks>
        /// Gated on the process still being alive: a server that exits on its own — the idle timeout, or
        /// a crash — leaves an address nothing answers on, and a menu reading that address out to
        /// friends has to stop offering it the moment it happens.
        /// </remarks>
        public NetEndpoint Endpoint => IsRunning ? _endpoint : NetEndpoint.None;

        /// <summary>
        /// Starts the server and completes once it has reported the port it is listening on.
        /// </summary>
        /// <remarks>
        /// A server that dies before reporting readiness is retried once on an ephemeral port, because
        /// by far the likeliest cause is the preferred port already being held — by a second editor, by
        /// a server this process failed to reap, or by a socket still in <c>TIME_WAIT</c>. A server that
        /// simply never answers is a different failure and is not retried: it is killed and the last of
        /// its output travels out in the exception, which is the only place anyone will look.
        /// </remarks>
        public async Task<NetEndpoint> StartAsync(CancellationToken cancellationToken) {
            if (_isDisposed) throw new ObjectDisposedException(nameof(LocalServerLauncher));

            NetEndpoint running = Endpoint;

            if (running.IsValid) return running;

            string executablePath = RequireExecutablePath();

            InstallLifecycleHooks();

            int preferredPort = _config.ClampedPreferredPort();
            LocalServerAttempt attempt = await RunAttemptAsync(executablePath, preferredPort, cancellationToken);
            int port = ReportedPort(attempt);

            if (port == LocalServerAttempt.NoPort && attempt.ExitCode != 0) {
                Debug.LogWarning($"LocalServerLauncher::StartAsync->The local server {DescribeExit(attempt.ExitCode)} before it was ready on port {preferredPort}; retrying on an ephemeral port.");
                StopAttempt(attempt);
                attempt = await RunAttemptAsync(executablePath, EphemeralPort, cancellationToken);
                port = ReportedPort(attempt);
            }

            if (port == LocalServerAttempt.NoPort) {
                throw FailAndReap(attempt, DescribeExit(attempt.ExitCode) + " before reporting readiness");
            }

            if (!TryAdoptEndpoint(attempt, port, out NetEndpoint endpoint)) {
                throw FailAndReap(attempt, "was replaced by a newer launch before it was ready");
            }

            return endpoint;
        }

        /// <summary>
        /// Kills the server and forgets it. Safe to call when there is nothing running, and safe to
        /// call twice — both happen, because it is wired to several shutdown signals that overlap.
        /// </summary>
        /// <remarks>
        /// A start still waiting on readiness ends here too, and ends promptly: the attempt settles its
        /// own promise as it dies, so a host request abandoned by teardown or by leaving play mode
        /// reports failure at once rather than after the whole readiness budget.
        /// <para>
        /// Holds the calling thread while the server closes its sessions, so it is for the endings that
        /// have to finish before this process does: quitting, a domain reload, leaving play mode and
        /// <see cref="Dispose"/>. A leave wants <see cref="BeginStop"/> instead.
        /// </para>
        /// </remarks>
        public void Stop() {
            TakeAttempt()?.Stop();
        }

        /// <summary>
        /// Ends the server the way <see cref="Stop"/> does, but without waiting for it to finish.
        /// </summary>
        /// <remarks>
        /// The launcher lets go of the attempt here, so a host started straight afterwards is a new
        /// attempt with its own process and the reap still running behind it cannot reach it. The old
        /// server may not have released its port by then; <see cref="StartAsync"/>'s ephemeral retry is
        /// what covers that.
        /// </remarks>
        public void BeginStop() {
            TakeAttempt()?.BeginStop();
        }

        /// <summary>
        /// Lets go of the running server without killing it, and forgets it for good.
        /// </summary>
        /// <remarks>
        /// For the host who leaves a session other players are still in: the server process <em>is</em>
        /// their session, so stopping it ends their game. Nothing reaps a detached server afterwards —
        /// not this launcher, not the quit and play-mode hooks, which now have nothing to reach — so the
        /// server's own idle-exit budget is what ends it once the last guest goes. A launcher that has
        /// detached is reusable: the next start simply spawns a new server.
        /// </remarks>
        public void Detach() {
            TakeAttempt()?.Detach();
        }

        /// <summary>Hands the live attempt over to one ending, leaving the launcher with nothing.</summary>
        /// <remarks>
        /// The endpoint goes with it. A launcher that has given up its attempt reports no address at
        /// all, rather than one belonging to a server that is being killed or has been let go.
        /// </remarks>
        private LocalServerAttempt TakeAttempt() {
            lock (_gate) {
                LocalServerAttempt attempt = _attempt;

                _attempt = null;
                _endpoint = NetEndpoint.None;

                return attempt;
            }
        }

        /// <inheritdoc />
        public void Dispose() {
            if (_isDisposed) return;

            _isDisposed = true;
            RemoveLifecycleHooks();
            Stop();
        }

        /// <summary>
        /// Reads the port out of one line of server output, ignoring every line that is not the
        /// readiness line.
        /// </summary>
        /// <remarks>
        /// Deliberately duplicates <c>AlpineLib.Server.Hosting.ReadinessLine.TryParse</c>: the server
        /// assembly targets a framework the editor never loads, so the wire format is shared as a
        /// contract rather than as a type. Change one and change the other.
        /// </remarks>
        public static bool TryParseReadinessLine(string line, out int port) {
            port = 0;

            if (string.IsNullOrEmpty(line)) return false;

            string trimmed = line.Trim();

            if (!trimmed.StartsWith(ReadyLinePrefix, StringComparison.Ordinal)) return false;

            string remainder = trimmed.Substring(ReadyLinePrefix.Length).TrimStart();

            if (!remainder.StartsWith(ReadyPortToken, StringComparison.Ordinal)) return false;

            return TryParsePort(remainder.Substring(ReadyPortToken.Length).TrimEnd(), out port);
        }

        /// <remarks>
        /// Zero is rejected as hard as a malformed line. A bound socket always knows its port, so
        /// <c>port=0</c> means the server echoed the request instead of the result, and accepting it
        /// hands the caller an endpoint nothing is listening on — reported as success.
        /// </remarks>
        private static bool TryParsePort(string value, out int port) {
            port = 0;

            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)) return false;
            if (parsed < MinPort || parsed > MaxPort) return false;

            port = parsed;
            return true;
        }

        /// <summary>
        /// Runs one launch attempt: starts the process and waits for readiness, the process dying, the
        /// timeout, or the caller giving up.
        /// </summary>
        private async Task<LocalServerAttempt> RunAttemptAsync(string executablePath, int port, CancellationToken cancellationToken) {
            LocalServerAttempt attempt = StartAttempt(executablePath, port);

            using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
                Task first = await Task.WhenAny(attempt.ReadyPort, Task.Delay(ReadyTimeoutMilliseconds(), timeoutSource.Token));
                timeoutSource.Cancel();

                if (ReferenceEquals(first, attempt.ReadyPort)) return attempt;
            }

            StopAttempt(attempt);
            cancellationToken.ThrowIfCancellationRequested();

            throw new InvalidOperationException(DescribeFailure(attempt,
                $"did not report readiness within {DescribeReadyTimeout()} s"));
        }

        /// <summary>Reads a settled attempt's port, turning a stop mid-wait into a launcher failure.</summary>
        private int ReportedPort(LocalServerAttempt attempt) {
            if (!attempt.ReadyPort.IsCanceled) return attempt.ReadyPort.Result;

            throw new InvalidOperationException(
                DescribeFailure(attempt, "was stopped before it reported readiness"));
        }

        /// <summary>Spawns the server with stdout and stderr redirected, replacing anything still running.</summary>
        /// <remarks>
        /// A <c>setsid</c> that will not exec — the wrong architecture, a shadowing file in a
        /// project-local <c>bin</c> — costs group reaping and must not cost the host, so the spawn is
        /// retried directly rather than predicted. Predicting it cannot be done: the execute bit is
        /// checked before this, and an executable file can still fail to run.
        /// </remarks>
        private LocalServerAttempt StartAttempt(string executablePath, int port) {
            Stop();

            ProcessStartInfo startInfo = BuildStartInfo(executablePath, port, out bool leadsOwnProcessGroup);

            if (!leadsOwnProcessGroup) return PublishAndStart(startInfo, false);

            try {
                return PublishAndStart(startInfo, true);
            } catch (Win32Exception exception) {
                Debug.LogWarning($"LocalServerLauncher::StartAttempt->Could not run the server through '{startInfo.FileName}' ({exception.Message}); launching it directly and giving up process-group reaping.");
            }

            return PublishAndStart(BuildDirectStartInfo(executablePath, port), false);
        }

        /// <summary>Publishes an attempt so a concurrent stop can reach it, then starts it.</summary>
        /// <remarks>
        /// The publish comes first on purpose; the attempt makes its own start a no-op if that stop
        /// wins the race.
        /// </remarks>
        private LocalServerAttempt PublishAndStart(ProcessStartInfo startInfo, bool leadsOwnProcessGroup) {
            var attempt = new LocalServerAttempt(startInfo, leadsOwnProcessGroup, _config.ClampedStopGraceMilliseconds());

            lock (_gate) {
                _attempt = attempt;
            }

            StartOrReap(attempt);
            return attempt;
        }

        /// <summary>Starts a published attempt, reaping it in place when the spawn itself throws.</summary>
        private void StartOrReap(LocalServerAttempt attempt) {
            try {
                attempt.Start();
            } catch (Exception) {
                StopAttempt(attempt);
                throw;
            }
        }

        /// <summary>Reaps a failed attempt before its failure travels out as an exception.</summary>
        /// <remarks>
        /// The tracked process being gone is not the same as the launch being cleaned up: a server that
        /// forked a helper and then exited leaves that helper holding the port, and nothing else reaps
        /// an attempt that failed on its own terms rather than on a timeout.
        /// </remarks>
        private InvalidOperationException FailAndReap(LocalServerAttempt attempt, string what) {
            var failure = new InvalidOperationException(DescribeFailure(attempt, what));

            StopAttempt(attempt);

            return failure;
        }

        /// <summary>Stops one attempt, and forgets it only if it is still the live one.</summary>
        /// <remarks>
        /// The guard is the point. A superseded attempt reaches its timeout long after a newer one has
        /// taken over, and without this it would kill a server somebody is already playing on.
        /// </remarks>
        private void StopAttempt(LocalServerAttempt attempt) {
            lock (_gate) {
                if (ReferenceEquals(_attempt, attempt)) {
                    _attempt = null;
                    _endpoint = NetEndpoint.None;
                }
            }

            attempt.Stop();
        }

        /// <summary>Publishes a ready attempt's endpoint, unless a newer attempt has already taken over.</summary>
        private bool TryAdoptEndpoint(LocalServerAttempt attempt, int port, out NetEndpoint endpoint) {
            endpoint = NetEndpoint.Direct(LoopbackHost, port);

            lock (_gate) {
                if (!ReferenceEquals(_attempt, attempt)) return false;

                _endpoint = endpoint;
                return true;
            }
        }

        /// <remarks>
        /// Checked before the lifecycle hooks are installed, so a launcher that never had a server to
        /// lose does not leave itself subscribed to a static event.
        /// </remarks>
        private string RequireExecutablePath() {
            string executablePath = LocalServerPaths.ResolveExecutablePath(_config);

            if (File.Exists(executablePath)) return executablePath;

            throw new InvalidOperationException(
                $"LocalServerLauncher::RequireExecutablePath->No server executable at '{executablePath}'; publish the dedicated server before hosting one locally.");
        }

        private ProcessStartInfo BuildStartInfo(string executablePath, int port, out bool leadsOwnProcessGroup) {
            ProcessStartInfo startInfo = BuildBaseStartInfo();

            leadsOwnProcessGroup = AppendCommand(startInfo, executablePath);
            AppendArguments(startInfo, port);

            return startInfo;
        }

        /// <summary>The same launch with no <c>setsid</c> in front of it, for when that one will not run.</summary>
        private ProcessStartInfo BuildDirectStartInfo(string executablePath, int port) {
            ProcessStartInfo startInfo = BuildBaseStartInfo();

            startInfo.FileName = executablePath;
            AppendArguments(startInfo, port);

            return startInfo;
        }

        private ProcessStartInfo BuildBaseStartInfo() {
            return new ProcessStartInfo {
                WorkingDirectory = LocalServerPaths.ResolveServerDirectory(_config),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
        }

        /// <summary>
        /// Names the program to run, and reports whether the launched pid will lead a process group of
        /// its own.
        /// </summary>
        /// <remarks>
        /// <c>setsid</c> is the executable, not a line of shell: a shell wrapper would have to be asked
        /// afterwards what it decided to do, and the answer arrives too late to be trusted. Run this way
        /// it execs in place — it forks only when its caller already leads a group, and a process .NET
        /// spawned never does — so the pid <c>Process.Start</c> hands back is the session leader itself
        /// and the group to reap is that same pid. A host without <c>setsid</c> — macOS ships without it
        /// — runs the server directly and gives up group reaping rather than guessing at a group.
        /// </remarks>
        private static bool AppendCommand(ProcessStartInfo startInfo, string executablePath) {
            if (LocalServerPaths.IsWindows()) {
                startInfo.FileName = executablePath;
                return false;
            }

            string setsidPath = ResolveSetsidPath();

            if (setsidPath == null) {
                startInfo.FileName = executablePath;
                return false;
            }

            startInfo.FileName = setsidPath;
            startInfo.ArgumentList.Add(executablePath);
            return true;
        }

        /// <summary>The full path to a runnable <c>setsid</c>, or null when this host does not ship one.</summary>
        /// <remarks>
        /// Resolved to a path rather than left to <c>Process.Start</c>'s own PATH search so that "no
        /// <c>setsid</c> here" is answered before the spawn, while there is still a choice to make.
        /// </remarks>
        private static string ResolveSetsidPath() {
            string searchPath = Environment.GetEnvironmentVariable("PATH");

            if (string.IsNullOrEmpty(searchPath)) return null;

            foreach (string directory in searchPath.Split(Path.PathSeparator)) {
                string candidate = SetsidCandidate(directory);

                if (candidate != null) return candidate;
            }

            return null;
        }

        private static string SetsidCandidate(string directory) {
            if (string.IsNullOrEmpty(directory)) return null;

            string candidate = Path.Combine(directory, SetsidFileName);

            if (!File.Exists(candidate)) return null;

            return IsExecutable(candidate) ? candidate : null;
        }

        /// <summary>True when this host can actually run the file, not merely see it.</summary>
        /// <remarks>
        /// "There is a file called <c>setsid</c> on PATH" is not the question. One without an execute
        /// bit, or one the player has no permission for, would otherwise turn an unrelated file into a
        /// failed host — where answering "no" here costs only group reaping. A libc that will not bind
        /// answers "no" for the rest of the run for the same reason: the safe reading of an unknown is
        /// the one that still launches the server.
        /// </remarks>
        private static bool IsExecutable(string path) {
            if (!_isAccessApiUsable) return false;

            try {
                return CheckAccessNative(path, ExecutePermission) == 0;
            } catch (DllNotFoundException) {
                _isAccessApiUsable = false;
            } catch (EntryPointNotFoundException) {
                _isAccessApiUsable = false;
            }

            return false;
        }

        /// <remarks>
        /// Built as a list rather than one command line: the config directory is an authored path that
        /// can hold spaces or quotes, and hand-quoting it is a seam with nothing on the other side to
        /// catch a mistake.
        /// </remarks>
        private void AppendArguments(ProcessStartInfo startInfo, int port) {
            startInfo.ArgumentList.Add("--port");
            startInfo.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--config");
            startInfo.ArgumentList.Add(LocalServerPaths.ResolveConfigDirectory(_config));
            startInfo.ArgumentList.Add("--idle-exit-seconds");
            startInfo.ArgumentList.Add(_config.ClampedIdleExitSeconds().ToString(CultureInfo.InvariantCulture));
        }

        private static string DescribeExit(int exitCode) {
            if (exitCode == LocalServerAttempt.UnknownExitCode) return "exited with an unreadable exit code";

            return "exited with code " + exitCode.ToString(CultureInfo.InvariantCulture);
        }

        /// <remarks>
        /// Quotes the budget that was actually waited out, floor included: a message naming an authored
        /// 0.2 s after a one-second wait sends the reader looking for the wrong problem.
        /// </remarks>
        private string DescribeReadyTimeout() {
            return _config.ClampedReadyTimeoutSeconds().ToString("0.#", CultureInfo.InvariantCulture);
        }

        private string DescribeFailure(LocalServerAttempt attempt, string what) {
            string executablePath = LocalServerPaths.ResolveExecutablePath(_config);

            return $"LocalServerLauncher->The local server at '{executablePath}' {what}."
                + Environment.NewLine
                + "Last server output (stdout and stderr, interleaved by arrival):"
                + Environment.NewLine + attempt.RecentOutput();
        }

        private int ReadyTimeoutMilliseconds() {
            return Mathf.RoundToInt(_config.ClampedReadyTimeoutSeconds() * 1000f);
        }

        private LocalServerAttempt CurrentAttempt() {
            lock (_gate) {
                return _attempt;
            }
        }

        /// <summary>
        /// Wires the server's death to every shutdown this process can see: quitting, a domain reload
        /// and leaving play mode.
        /// </summary>
        private void InstallLifecycleHooks() {
            if (_areHooksInstalled) return;

            _areHooksInstalled = true;
            Application.quitting += Stop;
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
#endif
        }

        private void RemoveLifecycleHooks() {
            if (!_areHooksInstalled) return;

            _areHooksInstalled = false;
            Application.quitting -= Stop;
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= Stop;
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
#endif
        }

#if UNITY_EDITOR
        private void HandlePlayModeStateChanged(PlayModeStateChange change) {
            if (change != PlayModeStateChange.ExitingPlayMode) return;

            Stop();
        }
#endif

        /// <remarks>
        /// Only ever reached on POSIX: the Windows branch of <see cref="AppendCommand"/> returns before
        /// <c>setsid</c> is looked for at all, so the binding is never evaluated there.
        /// </remarks>
        [DllImport("libc", EntryPoint = "access", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern int CheckAccessNative(string path, int mode);
    }
}
