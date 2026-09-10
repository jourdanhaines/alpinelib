using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;
using DataReceivedEventArgs = System.Diagnostics.DataReceivedEventArgs;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace AlpineLib.Sessions {
    /// <summary>
    /// One try at starting the dedicated server: the process, the readiness promise it either keeps or
    /// breaks, and the tail of what it printed on the way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exists so that a launch attempt owns its own state instead of sharing fields with the launcher.
    /// Hosting is re-callable, and while one attempt is still waiting out its readiness budget a second
    /// can be started; with shared fields the older attempt's timeout reaches the newer attempt's
    /// process and kills a server somebody is already playing on. An attempt can only ever stop itself.
    /// </para>
    /// <para>
    /// <see cref="Stop"/> resolves the readiness promise as well as killing the process, so a host
    /// request abandoned by teardown or by leaving play mode fails immediately rather than sitting out
    /// a budget nobody is waiting for any more.
    /// </para>
    /// <para>
    /// On Linux and macOS the process is started in its own process group and the group is what gets
    /// killed, because a server that spawned helpers of its own leaves them holding the port otherwise.
    /// Windows has no equivalent reachable from netstandard2.1, so there only the launched process dies
    /// and the configuration is documented to name the server binary directly.
    /// </para>
    /// </remarks>
    internal sealed class LocalServerAttempt : IDisposable {
        /// <summary>Reported when the server died, or was stopped, before naming a port.</summary>
        public const int NoPort = -1;

        /// <summary>Stands in for an exit code the runtime would not hand over.</summary>
        /// <remarks>
        /// Distinct from zero on purpose: an unreadable code is not a clean exit, and the launcher's
        /// retry keys off "did not exit cleanly". Reporting it as 0 suppresses the retry in exactly the
        /// case — a contended port — that the retry exists for.
        /// </remarks>
        public const int UnknownExitCode = int.MinValue;

        private const int DiagnosticLineCount = 20;
        private const int StopWaitMilliseconds = 2000;
        private const int ExitDrainMilliseconds = 2000;
        private const int NoProcessGroup = 0;
        private const string UnixShellPath = "/bin/sh";
        private const string KillGroupScript = "kill -9 -- \"-$1\"";

        private static int _ownProcessGroupId = NoProcessGroup;

        private readonly Process _process;
        private readonly TaskCompletionSource<int> _readyPort =
            new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _gate = new object();
        private readonly Queue<string> _recentOutputLines = new Queue<string>();

        private int _processGroupId = NoProcessGroup;
        private int _exitCode = UnknownExitCode;
        private bool _hasExited;
        private bool _isStopped;

        /// <summary>Wraps a not-yet-started process described by <paramref name="startInfo"/>.</summary>
        public LocalServerAttempt(ProcessStartInfo startInfo) {
            if (startInfo == null) throw new ArgumentNullException(nameof(startInfo));

            _process = new Process {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            _process.OutputDataReceived += HandleOutputLine;
            _process.ErrorDataReceived += HandleErrorLine;
            _process.Exited += HandleProcessExited;
        }

        /// <summary>Completes with the port the server reported, or <see cref="NoPort"/> when it died first.</summary>
        /// <remarks>Cancelled instead when <see cref="Stop"/> ends the attempt while it is still waiting.</remarks>
        public Task<int> ReadyPort => _readyPort.Task;

        /// <summary>The exit code of the server, or <see cref="UnknownExitCode"/> while it still runs.</summary>
        public int ExitCode => _exitCode;

        /// <summary>True while this attempt's server is alive.</summary>
        public bool IsRunning {
            get {
                if (_hasExited || _isStopped) return false;

                try {
                    return !_process.HasExited;
                } catch (InvalidOperationException) {
                    return false;
                }
            }
        }

        /// <summary>Spawns the server and starts draining both of its output streams.</summary>
        public void Start() {
            _process.Start();
            _processGroupId = ResolveProcessGroup(_process.Id);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        /// <summary>
        /// Kills this attempt's server and settles anyone waiting on it. Idempotent, because the several
        /// shutdown signals wired to it overlap.
        /// </summary>
        public void Stop() {
            if (_isStopped) return;

            _isStopped = true;
            _process.OutputDataReceived -= HandleOutputLine;
            _process.ErrorDataReceived -= HandleErrorLine;
            _process.Exited -= HandleProcessExited;

            // Before the kill, so an abandoned host request unblocks the moment the decision is made
            // rather than after the process has finished dying.
            _readyPort.TrySetCanceled();

            KillProcessGroup();
            KillProcess();
            WaitForExit();
            _process.Dispose();
        }

        /// <inheritdoc />
        public void Dispose() {
            Stop();
        }

        /// <summary>The last few lines the server printed, for a failure message nobody can otherwise see.</summary>
        /// <remarks>
        /// stdout and stderr are interleaved by arrival, not by the order the server wrote them: two
        /// streams drained by two readers have no shared clock, so read the tail as a set of lines.
        /// </remarks>
        public string RecentOutput() {
            lock (_gate) {
                if (_recentOutputLines.Count == 0) return "(none)";

                return string.Join(Environment.NewLine, _recentOutputLines.ToArray());
            }
        }

        /// <remarks>
        /// Arrives on a thread pool thread. Nothing here touches a Unity object; the readiness result
        /// travels out through a task whose continuation resumes on the caller's context.
        /// </remarks>
        private void HandleOutputLine(object sender, DataReceivedEventArgs eventArgs) {
            if (eventArgs.Data == null) return;

            RecordLine(eventArgs.Data);

            if (!LocalServerLauncher.TryParseReadinessLine(eventArgs.Data, out int port)) return;

            _readyPort.TrySetResult(port);
        }

        private void HandleErrorLine(object sender, DataReceivedEventArgs eventArgs) {
            if (eventArgs.Data == null) return;

            RecordLine(eventArgs.Data);
        }

        private void HandleProcessExited(object sender, EventArgs eventArgs) {
            _hasExited = true;
            _exitCode = ReadExitCode();

            // Resolved rather than faulted: a server that died before readiness is an outcome the
            // launcher retries, not an exception it has to catch to make a decision with.
            _readyPort.TrySetResult(NoPort);
        }

        /// <remarks>
        /// The wait is the documented half of this: the exit event can beat both the exit code and the
        /// last lines of the redirected streams, and those lines are the whole diagnostic.
        /// </remarks>
        private int ReadExitCode() {
            try {
                _process.WaitForExit(ExitDrainMilliseconds);

                return _process.ExitCode;
            } catch (Exception) {
                return UnknownExitCode;
            }
        }

        private void RecordLine(string line) {
            lock (_gate) {
                _recentOutputLines.Enqueue(line);

                while (_recentOutputLines.Count > DiagnosticLineCount) {
                    _recentOutputLines.Dequeue();
                }
            }
        }

        /// <remarks>
        /// Best effort on purpose: the process may have died between the check and the kill, and a
        /// server that is already gone is the outcome this wanted anyway.
        /// </remarks>
        private void KillProcess() {
            try {
                if (_process.HasExited) return;

                _process.Kill();
            } catch (Exception exception) {
                Debug.LogWarning($"LocalServerAttempt::KillProcess->Could not stop the local server cleanly: {exception.Message}");
            }
        }

        private void WaitForExit() {
            try {
                _process.WaitForExit(StopWaitMilliseconds);
            } catch (Exception exception) {
                Debug.LogWarning($"LocalServerAttempt::WaitForExit->Could not confirm the local server stopped: {exception.Message}");
            }
        }

        /// <summary>Signals the whole process group, so helpers the server spawned die with it.</summary>
        /// <remarks>
        /// Routed through <c>sh</c> because <c>kill</c> is a shell builtin on some hosts and a binary on
        /// others, and only the builtin is guaranteed to be there.
        /// </remarks>
        private void KillProcessGroup() {
            if (_processGroupId == NoProcessGroup) return;

            RunSilently(UnixShellPath, "-c", KillGroupScript, "kill-group",
                _processGroupId.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Reads the process group the server landed in, or <see cref="NoProcessGroup"/> when there is
        /// none to kill safely.
        /// </summary>
        /// <remarks>
        /// The group is refused when it matches this process's own, which is what happens on a host
        /// without <c>setsid</c>: signalling that group would take the editor or the game down with the
        /// server, so a single-process kill is the safe answer there.
        /// </remarks>
        private static int ResolveProcessGroup(int processId) {
            if (LocalServerPaths.IsWindows()) return NoProcessGroup;

            int group = ReadProcessGroup(processId);

            if (group == NoProcessGroup) return NoProcessGroup;
            if (group == OwnProcessGroup()) return NoProcessGroup;

            return group;
        }

        /// <summary>This process's own group, read once and remembered — it cannot change.</summary>
        private static int OwnProcessGroup() {
            if (_ownProcessGroupId != NoProcessGroup) return _ownProcessGroupId;

            _ownProcessGroupId = ReadProcessGroup(CurrentProcessId());
            return _ownProcessGroupId;
        }

        private static int ReadProcessGroup(int processId) {
            try {
                return ParseProcessGroup(processId);
            } catch (Exception) {
                return NoProcessGroup;
            }
        }

        private static int ParseProcessGroup(int processId) {
            var startInfo = new ProcessStartInfo {
                FileName = "ps",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            startInfo.ArgumentList.Add("-o");
            startInfo.ArgumentList.Add("pgid=");
            startInfo.ArgumentList.Add("-p");
            startInfo.ArgumentList.Add(processId.ToString(CultureInfo.InvariantCulture));

            using (Process query = Process.Start(startInfo)) {
                string text = query.StandardOutput.ReadToEnd().Trim();
                query.WaitForExit(ExitDrainMilliseconds);

                return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int group) ? group : NoProcessGroup;
            }
        }

        private static int CurrentProcessId() {
            using (Process current = Process.GetCurrentProcess()) {
                return current.Id;
            }
        }

        private static void RunSilently(string fileName, params string[] arguments) {
            try {
                StartSilently(fileName, arguments);
            } catch (Exception exception) {
                Debug.LogWarning($"LocalServerAttempt::RunSilently->Could not run '{fileName}': {exception.Message}");
            }
        }

        private static void StartSilently(string fileName, string[] arguments) {
            var startInfo = new ProcessStartInfo {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (string argument in arguments) {
                startInfo.ArgumentList.Add(argument);
            }

            using (Process command = Process.Start(startInfo)) {
                command.WaitForExit(ExitDrainMilliseconds);
            }
        }
    }
}
