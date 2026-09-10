using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using DataReceivedEventArgs = System.Diagnostics.DataReceivedEventArgs;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using Stopwatch = System.Diagnostics.Stopwatch;

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
    /// On Linux and macOS the launcher runs the server through <c>setsid</c>, so the tracked pid leads a
    /// process group of its own and that whole group is what gets killed — a server that spawned helpers
    /// leaves them holding the port otherwise. The group is only ever signalled once it has been read
    /// back and proved to be the child's own and not this process's; every unknown answers "no group"
    /// and degrades to killing the single pid, because the alternative is signalling the editor.
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
        private const int SigKill = 9;

        /// <summary>Argument to <c>getpgid</c> meaning "whoever is asking".</summary>
        private const int CallingProcess = 0;

        /// <remarks>
        /// Wildly generous: the move takes a fraction of a millisecond in practice, because all that has
        /// to happen is <c>setsid</c> reaching its own <c>setsid(2)</c> call. The budget is here so a
        /// host on a loaded machine still resolves rather than silently dropping to a single-pid kill.
        /// </remarks>
        private const int ProcessGroupSettleMilliseconds = 250;
        private const int ProcessGroupPollMilliseconds = 1;

        private static readonly object ProcessGroupGate = new object();

        private static int _ownProcessGroupId = NoProcessGroup;
        private static bool _isOwnProcessGroupRead;
        private static bool _isProcessGroupApiUsable = true;

        private readonly Process _process;
        private readonly bool _leadsOwnProcessGroup;
        private readonly TaskCompletionSource<int> _readyPort =
            new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _outputGate = new object();
        private readonly object _stateGate = new object();
        private readonly Queue<string> _recentOutputLines = new Queue<string>();

        private volatile int _processGroupId = NoProcessGroup;
        private int _processId;
        private int _exitCode = UnknownExitCode;
        private bool _hasStarted;
        private bool _hasExited;
        private bool _isStopped;

        /// <summary>Wraps a not-yet-started process described by <paramref name="startInfo"/>.</summary>
        /// <param name="leadsOwnProcessGroup">
        /// True only when the command is <c>setsid</c>, which is the one launch shape that puts the
        /// tracked pid at the head of a group of its own. False disables group signalling outright.
        /// </param>
        public LocalServerAttempt(ProcessStartInfo startInfo, bool leadsOwnProcessGroup) {
            if (startInfo == null) throw new ArgumentNullException(nameof(startInfo));

            _leadsOwnProcessGroup = leadsOwnProcessGroup;
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
        public int ExitCode {
            get {
                lock (_stateGate) {
                    return _exitCode;
                }
            }
        }

        /// <summary>True while this attempt's server is alive.</summary>
        public bool IsRunning {
            get {
                lock (_stateGate) {
                    if (!_hasStarted || _hasExited || _isStopped) return false;
                }

                return !HasProcessExited();
            }
        }

        /// <summary>
        /// The group this attempt may signal, or <see cref="NoProcessGroup"/> when signalling one would
        /// not be provably safe. Cached once a usable answer is read.
        /// </summary>
        /// <remarks>
        /// Fail-closed on every unknown. No <c>setsid</c>, an unreadable own group, an unreadable child
        /// group, a child that turns out not to lead its own group, or a libc that will not bind — each
        /// answers "no group", and the attempt falls back to killing the one pid it tracks. The failure
        /// this guards against is not a leaked helper process; it is <c>kill -9</c> on the group the
        /// editor itself is sitting in.
        /// </remarks>
        private int ProcessGroupId {
            get {
                int cached = _processGroupId;

                if (cached != NoProcessGroup) return cached;
                if (!_leadsOwnProcessGroup) return NoProcessGroup;

                int resolved = VerifiedProcessGroup(CurrentProcessId());
                _processGroupId = resolved;

                return resolved;
            }
        }

        /// <summary>Spawns the server and starts draining both of its output streams.</summary>
        /// <remarks>
        /// A no-op once <see cref="Stop"/> has run: the launcher publishes an attempt before starting it
        /// so a stop can reach it, and a stop that lands in that window has already disposed the process.
        /// </remarks>
        public void Start() {
            lock (_stateGate) {
                if (_isStopped) return;

                _process.Start();
                _hasStarted = true;
                _processId = _process.Id;
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }

            SettleProcessGroup();
        }

        /// <summary>
        /// Kills this attempt's server and settles anyone waiting on it. Idempotent, because the several
        /// shutdown signals wired to it overlap.
        /// </summary>
        public void Stop() {
            bool hadProcess;

            lock (_stateGate) {
                if (_isStopped) return;

                _isStopped = true;
                hadProcess = _hasStarted;
            }

            _process.OutputDataReceived -= HandleOutputLine;
            _process.ErrorDataReceived -= HandleErrorLine;
            _process.Exited -= HandleProcessExited;

            // Before the kill, so an abandoned host request unblocks the moment the decision is made
            // rather than after the process has finished dying.
            _readyPort.TrySetCanceled();

            if (hadProcess) StopProcess();

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
            lock (_outputGate) {
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
            int exitCode = ReadExitCode();

            lock (_stateGate) {
                _hasExited = true;
                _exitCode = exitCode;
            }

            // Resolved rather than faulted: a server that died before readiness is an outcome the
            // launcher retries, not an exception it has to catch to make a decision with.
            _readyPort.TrySetResult(NoPort);
        }

        /// <remarks>
        /// The bounded wait is for the exit code alone: the exit event can beat the runtime's own record
        /// of how the process ended. It does not drain the redirected streams — only the parameterless
        /// overload does that — so the diagnostic tail is whatever arrived by the time it is read.
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
            lock (_outputGate) {
                _recentOutputLines.Enqueue(line);

                while (_recentOutputLines.Count > DiagnosticLineCount) {
                    _recentOutputLines.Dequeue();
                }
            }
        }

        private int CurrentProcessId() {
            lock (_stateGate) {
                return _processId;
            }
        }

        /// <remarks>
        /// A process nobody started, and one this attempt has already disposed, both answer through the
        /// same <see cref="InvalidOperationException"/>, and both mean "not running".
        /// </remarks>
        private bool HasProcessExited() {
            try {
                return _process.HasExited;
            } catch (InvalidOperationException) {
                return true;
            }
        }

        private void StopProcess() {
            KillProcessGroup();
            KillProcess();
            WaitForExit();
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
        private void KillProcessGroup() {
            int group = ProcessGroupId;

            if (group == NoProcessGroup) return;

            SignalProcessGroup(group);
        }

        /// <summary>
        /// Reads the group back until it is the child's own, so the answer is cached while the server is
        /// certainly alive rather than looked up at kill time.
        /// </summary>
        /// <remarks>
        /// <c>setsid</c> execs in place here — it forks only when its caller already leads a group, and
        /// a process .NET spawned never does — so the pid handed back by <c>Process.Start</c> is the one
        /// that becomes the session leader. What it is not is instantaneous: the move happens a fraction
        /// of a millisecond later, inside the freshly exec'd <c>setsid</c>, so the first read usually
        /// still shows this process's group. Resolving here rather than at kill time also keeps a server
        /// that forks a helper and exits at once reapable, because by then its pid is gone.
        /// </remarks>
        private void SettleProcessGroup() {
            if (!_leadsOwnProcessGroup) return;

            var watch = Stopwatch.StartNew();

            while (ProcessGroupId == NoProcessGroup && watch.ElapsedMilliseconds < ProcessGroupSettleMilliseconds) {
                if (!IsProcessGroupApiUsable()) return;

                Thread.Sleep(ProcessGroupPollMilliseconds);
            }
        }

        /// <summary>The child's group, or <see cref="NoProcessGroup"/> unless it is provably safe to signal.</summary>
        private static int VerifiedProcessGroup(int processId) {
            if (processId <= 0) return NoProcessGroup;

            int ownGroup = OwnProcessGroup();

            if (ownGroup == NoProcessGroup) return NoProcessGroup;

            int group = ReadProcessGroup(processId);

            if (group == NoProcessGroup) return NoProcessGroup;
            if (group == ownGroup) return NoProcessGroup;
            if (group != processId) return NoProcessGroup;

            return group;
        }

        /// <summary>This process's own group, read once and remembered — it cannot change.</summary>
        /// <remarks>
        /// A failed read is remembered too. Retrying it every launch would keep re-asking a question the
        /// host has already answered, and leaving the failure looking like "group 0" is what turns the
        /// comparison below it into a guard that passes everything.
        /// </remarks>
        private static int OwnProcessGroup() {
            lock (ProcessGroupGate) {
                if (_isOwnProcessGroupRead) return _ownProcessGroupId;

                _isOwnProcessGroupRead = true;
                _ownProcessGroupId = ReadProcessGroup(CallingProcess);

                return _ownProcessGroupId;
            }
        }

        private static int ReadProcessGroup(int processId) {
            if (!IsProcessGroupApiUsable()) return NoProcessGroup;

            try {
                int group = ReadProcessGroupNative(processId);

                return group > 0 ? group : NoProcessGroup;
            } catch (DllNotFoundException) {
                return DisableProcessGroupApi();
            } catch (EntryPointNotFoundException) {
                return DisableProcessGroupApi();
            }
        }

        private static void SignalProcessGroup(int processGroupId) {
            if (!IsProcessGroupApiUsable()) return;

            try {
                SignalProcessGroupNative(processGroupId, SigKill);
            } catch (DllNotFoundException) {
                DisableProcessGroupApi();
            } catch (EntryPointNotFoundException) {
                DisableProcessGroupApi();
            }
        }

        private static bool IsProcessGroupApiUsable() {
            lock (ProcessGroupGate) {
                return _isProcessGroupApiUsable;
            }
        }

        /// <summary>Remembers that libc is out of reach, so the group path stays off for the whole run.</summary>
        private static int DisableProcessGroupApi() {
            lock (ProcessGroupGate) {
                _isProcessGroupApiUsable = false;
            }

            return NoProcessGroup;
        }

        /// <remarks>
        /// Read straight from libc rather than by shelling out to <c>ps</c>: a fork+exec answering a
        /// question about a process that was itself only just forked is a race, and the answer decides
        /// whether a <c>kill</c> reaches the server's group or this process's.
        /// </remarks>
        [DllImport("libc", EntryPoint = "getpgid", SetLastError = true)]
        private static extern int ReadProcessGroupNative(int processId);

        [DllImport("libc", EntryPoint = "killpg", SetLastError = true)]
        private static extern int SignalProcessGroupNative(int processGroupId, int signal);
    }
}
