using System;
using System.Globalization;
using System.IO;
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
    /// </remarks>
    public sealed class LocalServerLauncher : IDisposable {
        /// <summary>Marker opening the server's readiness line. Mirrors the server's own contract.</summary>
        public const string ReadyLinePrefix = "[ready]";

        private const string ReadyPortToken = "port=";
        private const string LoopbackHost = "127.0.0.1";
        private const string UnixShellPath = "/bin/sh";

        /// <remarks>
        /// <c>setsid</c> puts the server in a process group of its own, so the launcher can reap that
        /// whole group rather than one pid and a server's helper processes cannot outlive it. It is not
        /// on every host — macOS ships without it — hence the plain exec behind it; a server that ends
        /// up sharing this process's group is killed by pid instead.
        /// </remarks>
        private const string UnixLaunchScript =
            "command -v setsid >/dev/null 2>&1 && exec setsid \"$0\" \"$@\"; exec \"$0\" \"$@\"";

        private const int MinPort = 1;
        private const int MaxPort = 65535;
        private const int EphemeralPort = 0;

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
                attempt = await RunAttemptAsync(executablePath, EphemeralPort, cancellationToken);
                port = ReportedPort(attempt);
            }

            if (port == LocalServerAttempt.NoPort) {
                throw new InvalidOperationException(
                    DescribeFailure(attempt, DescribeExit(attempt.ExitCode) + " before reporting readiness"));
            }

            if (!TryAdoptEndpoint(attempt, port, out NetEndpoint endpoint)) {
                throw new InvalidOperationException(
                    DescribeFailure(attempt, "was replaced by a newer launch before it was ready"));
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
        /// </remarks>
        public void Stop() {
            LocalServerAttempt attempt;

            lock (_gate) {
                attempt = _attempt;
                _attempt = null;
                _endpoint = NetEndpoint.None;
            }

            attempt?.Stop();
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
        private LocalServerAttempt StartAttempt(string executablePath, int port) {
            Stop();

            var attempt = new LocalServerAttempt(BuildStartInfo(executablePath, port));

            lock (_gate) {
                _attempt = attempt;
            }

            attempt.Start();
            return attempt;
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

        private ProcessStartInfo BuildStartInfo(string executablePath, int port) {
            var startInfo = new ProcessStartInfo {
                WorkingDirectory = LocalServerPaths.ResolveServerDirectory(_config),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            AppendCommand(startInfo, executablePath);
            AppendArguments(startInfo, port);

            return startInfo;
        }

        /// <summary>Names the program to run, through a shell on POSIX so the server leads its own group.</summary>
        private static void AppendCommand(ProcessStartInfo startInfo, string executablePath) {
            if (LocalServerPaths.IsWindows()) {
                startInfo.FileName = executablePath;
                return;
            }

            startInfo.FileName = UnixShellPath;
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(UnixLaunchScript);
            startInfo.ArgumentList.Add(executablePath);
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
    }
}
