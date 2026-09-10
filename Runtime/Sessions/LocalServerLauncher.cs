using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Netcode.Transport;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using DataReceivedEventArgs = System.Diagnostics.DataReceivedEventArgs;
using Process = System.Diagnostics.Process;
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
    /// three are skipped by a crash.
    /// </para>
    /// </remarks>
    public sealed class LocalServerLauncher : IDisposable {
        /// <summary>Marker opening the server's readiness line. Mirrors the server's own contract.</summary>
        public const string ReadyLinePrefix = "[ready]";

        private const string ReadyPortToken = "port=";
        private const string LoopbackHost = "127.0.0.1";
        private const int MaxPort = 65535;
        private const int EphemeralPort = 0;
        private const int NoPort = -1;
        private const int DiagnosticLineCount = 20;
        private const int StopWaitMilliseconds = 2000;
        private const float MinimumReadyTimeoutSeconds = 1f;

        private readonly LocalServerConfig _config;
        private readonly object _gate = new object();
        private readonly Queue<string> _recentOutputLines = new Queue<string>();

        private Process _process;
        private TaskCompletionSource<int> _readyPort;
        private NetEndpoint _endpoint;
        private int _lastExitCode;
        private bool _areHooksInstalled;
        private bool _isDisposed;

        /// <summary>Creates a launcher for one authored server configuration.</summary>
        public LocalServerLauncher(LocalServerConfig config) {
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
        }

        /// <summary>True while a server started by this launcher is still alive.</summary>
        public bool IsRunning {
            get {
                Process process = CurrentProcess();

                return process != null && IsAlive(process);
            }
        }

        /// <summary>The endpoint the running server is listening on, or none while there is no server.</summary>
        public NetEndpoint Endpoint => _endpoint;

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
            if (IsRunning && _endpoint.IsValid) return _endpoint;

            InstallLifecycleHooks();

            int port = await RunAttemptAsync(_config.preferredPort, cancellationToken);

            if (port == NoPort && _lastExitCode != 0) {
                Debug.LogWarning($"LocalServerLauncher::StartAsync->The local server exited with code {_lastExitCode} before it was ready on port {_config.preferredPort}; retrying on an ephemeral port.");
                port = await RunAttemptAsync(EphemeralPort, cancellationToken);
            }

            if (port == NoPort) {
                throw new InvalidOperationException(
                    DescribeFailure($"exited with code {_lastExitCode} before reporting readiness"));
            }

            _endpoint = NetEndpoint.Direct(LoopbackHost, port);
            return _endpoint;
        }

        /// <summary>
        /// Kills the server and forgets it. Safe to call when there is nothing running, and safe to
        /// call twice — both happen, because it is wired to several shutdown signals that overlap.
        /// </summary>
        public void Stop() {
            Process process;

            lock (_gate) {
                process = _process;
                _process = null;
            }

            _endpoint = NetEndpoint.None;

            if (process == null) return;

            process.OutputDataReceived -= HandleOutputLine;
            process.ErrorDataReceived -= HandleErrorLine;
            process.Exited -= HandleProcessExited;

            KillAndWait(process);
            process.Dispose();
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

        private static bool TryParsePort(string value, out int port) {
            port = 0;

            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)) return false;
            if (parsed > MaxPort) return false;

            port = parsed;
            return true;
        }

        /// <summary>
        /// Runs one launch attempt: starts the process and waits for readiness, the process dying, the
        /// timeout, or the caller giving up.
        /// </summary>
        /// <returns>The port the server reported, or <see cref="NoPort"/> when it exited first.</returns>
        private async Task<int> RunAttemptAsync(int port, CancellationToken cancellationToken) {
            StartProcess(port);

            Task<int> readySignal = _readyPort.Task;

            using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
                Task first = await Task.WhenAny(readySignal, Task.Delay(ReadyTimeoutMilliseconds(), timeoutSource.Token));
                timeoutSource.Cancel();

                if (ReferenceEquals(first, readySignal)) return await readySignal;
            }

            Stop();
            cancellationToken.ThrowIfCancellationRequested();

            throw new InvalidOperationException(
                DescribeFailure($"did not report readiness within {_config.readyTimeoutSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s"));
        }

        /// <summary>Spawns the server with stdout and stderr redirected, replacing anything still running.</summary>
        private void StartProcess(int port) {
            Stop();

            string executablePath = LocalServerPaths.ResolveExecutablePath(_config);

            if (!File.Exists(executablePath)) {
                throw new InvalidOperationException(
                    $"LocalServerLauncher::StartProcess->No server executable at '{executablePath}'; publish the dedicated server before hosting one locally.");
            }

            lock (_gate) {
                _recentOutputLines.Clear();
            }

            _lastExitCode = 0;
            _readyPort = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            var process = new Process {
                StartInfo = BuildStartInfo(executablePath, port),
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += HandleOutputLine;
            process.ErrorDataReceived += HandleErrorLine;
            process.Exited += HandleProcessExited;

            process.Start();
            _process = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        private ProcessStartInfo BuildStartInfo(string executablePath, int port) {
            return new ProcessStartInfo {
                FileName = executablePath,
                Arguments = BuildArguments(port),
                WorkingDirectory = LocalServerPaths.ResolveServerDirectory(_config),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
        }

        private string BuildArguments(int port) {
            string configDirectory = LocalServerPaths.ResolveConfigDirectory(_config);

            return "--port " + port.ToString(CultureInfo.InvariantCulture)
                + " --config \"" + configDirectory + "\""
                + " --idle-exit-seconds " + _config.idleExitSeconds.ToString(CultureInfo.InvariantCulture);
        }

        /// <remarks>
        /// Both of these arrive on a thread pool thread. Nothing here touches Unity objects, and the
        /// completion is handed back through a task whose continuation the caller's synchronisation
        /// context — the main thread, for a service awaiting this from <c>Update</c> — resumes.
        /// </remarks>
        private void HandleOutputLine(object sender, DataReceivedEventArgs eventArgs) {
            if (eventArgs.Data == null) return;

            RecordLine(eventArgs.Data);

            if (!TryParseReadinessLine(eventArgs.Data, out int port)) return;

            _readyPort?.TrySetResult(port);
        }

        private void HandleErrorLine(object sender, DataReceivedEventArgs eventArgs) {
            if (eventArgs.Data == null) return;

            RecordLine(eventArgs.Data);
        }

        private void HandleProcessExited(object sender, EventArgs eventArgs) {
            _lastExitCode = ReadExitCode(sender as Process);

            // Resolving rather than faulting: a server that died before readiness is an outcome the
            // caller retries, not an exception it has to catch to make a decision with.
            _readyPort?.TrySetResult(NoPort);
        }

        private static int ReadExitCode(Process process) {
            if (process == null) return 0;

            try {
                return process.ExitCode;
            } catch (InvalidOperationException) {
                return 0;
            }
        }

        /// <summary>Keeps the tail of the server's output for a failure message nobody can otherwise see.</summary>
        private void RecordLine(string line) {
            lock (_gate) {
                _recentOutputLines.Enqueue(line);

                while (_recentOutputLines.Count > DiagnosticLineCount) {
                    _recentOutputLines.Dequeue();
                }
            }
        }

        private string DescribeFailure(string what) {
            string executablePath = LocalServerPaths.ResolveExecutablePath(_config);

            return $"LocalServerLauncher->The local server at '{executablePath}' {what}."
                + Environment.NewLine
                + "Last server output:" + Environment.NewLine + RecentOutput();
        }

        private string RecentOutput() {
            lock (_gate) {
                if (_recentOutputLines.Count == 0) return "(none)";

                return string.Join(Environment.NewLine, _recentOutputLines.ToArray());
            }
        }

        private int ReadyTimeoutMilliseconds() {
            float seconds = Mathf.Max(_config.readyTimeoutSeconds, MinimumReadyTimeoutSeconds);

            return Mathf.RoundToInt(seconds * 1000f);
        }

        private Process CurrentProcess() {
            lock (_gate) {
                return _process;
            }
        }

        private static bool IsAlive(Process process) {
            try {
                return !process.HasExited;
            } catch (InvalidOperationException) {
                return false;
            }
        }

        /// <remarks>
        /// Best effort on purpose: the process may have died between the check and the kill, and a
        /// server that is already gone is the outcome this method wanted anyway.
        /// </remarks>
        private static void KillAndWait(Process process) {
            try {
                if (!process.HasExited) {
                    process.Kill();
                }

                process.WaitForExit(StopWaitMilliseconds);
            } catch (Exception exception) {
                Debug.LogWarning($"LocalServerLauncher::KillAndWait->Could not stop the local server cleanly: {exception.Message}");
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
