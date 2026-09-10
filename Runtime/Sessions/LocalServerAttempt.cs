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
    /// <para>
    /// Stopping asks before it insists. A dedicated server has a shutdown of its own — it tells every
    /// session it is closing and pumps until those notices are off the wire — and only <c>SIGTERM</c>
    /// reaches it; <c>SIGKILL</c> leaves the guests waiting out a transport timeout instead. So a stop
    /// signals termination, waits out the configured grace, and kills only what is still standing.
    /// </para>
    /// <para>
    /// <see cref="Detach"/> is the opposite ending: the attempt lets go of a server that is still
    /// serving other players and never signals it at all.
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
        private const int SigTerm = 15;

        /// <summary>Delivers nothing; asks only whether the target still exists.</summary>
        private const int NoSignal = 0;

        /// <summary>Argument to <c>getpgid</c> meaning "whoever is asking".</summary>
        private const int CallingProcess = 0;

        /// <remarks>
        /// Wildly generous: the move takes a fraction of a millisecond in practice, because all that has
        /// to happen is <c>setsid</c> reaching its own <c>setsid(2)</c> call. The budget is here so a
        /// host on a loaded machine still resolves rather than silently dropping to a single-pid kill.
        /// It is only ever waited out on a thread pool thread draining the server's output.
        /// </remarks>
        private const int ProcessGroupSettleMilliseconds = 250;
        private const int ProcessGroupPollMilliseconds = 1;

        private static readonly object ProcessGroupGate = new object();

        private static int _ownProcessGroupId = NoProcessGroup;
        private static bool _isOwnProcessGroupRead;
        private static bool _isProcessGroupApiUsable = true;

        private readonly Process _process;
        private readonly bool _leadsOwnProcessGroup;
        private readonly int _stopGraceMilliseconds;
        private readonly TaskCompletionSource<int> _readyPort =
            new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _outputGate = new object();
        private readonly object _stateGate = new object();
        private readonly Queue<string> _recentOutputLines = new Queue<string>();

        private volatile int _processGroupId = NoProcessGroup;
        private int _isProcessGroupSettleClaimed;
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
        /// <param name="stopGraceMilliseconds">
        /// How long a stop waits after asking the server to shut down before killing it. Zero skips
        /// asking, which is the only behaviour Windows can offer.
        /// </param>
        public LocalServerAttempt(ProcessStartInfo startInfo, bool leadsOwnProcessGroup, int stopGraceMilliseconds) {
            if (startInfo == null) throw new ArgumentNullException(nameof(startInfo));

            _leadsOwnProcessGroup = leadsOwnProcessGroup;
            _stopGraceMilliseconds = stopGraceMilliseconds > 0 ? stopGraceMilliseconds : 0;
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
        /// Resolves the group this attempt may signal, or <see cref="NoProcessGroup"/> when signalling
        /// one would not be provably safe. Caches the first usable answer.
        /// </summary>
        /// <remarks>
        /// A method rather than a property because it costs syscalls and writes a cache — it is called
        /// from a poll loop, and a reader of that loop has to be able to see what a turn of it costs.
        /// <para>
        /// Fail-closed on every unknown. No <c>setsid</c>, an unreadable own group, an unreadable child
        /// group, a child that turns out not to lead its own group, or a libc that will not bind — each
        /// answers "no group", and the attempt falls back to killing the one pid it tracks. The failure
        /// this guards against is not a leaked helper process; it is <c>kill -9</c> on the group the
        /// editor itself is sitting in.
        /// </para>
        /// </remarks>
        private int ResolveProcessGroup() {
            int cached = _processGroupId;

            if (cached != NoProcessGroup) return cached;
            if (!_leadsOwnProcessGroup) return NoProcessGroup;

            int resolved = VerifiedProcessGroup(CurrentProcessId());
            _processGroupId = resolved;

            return resolved;
        }

        /// <summary>Spawns the server and starts draining both of its output streams.</summary>
        /// <remarks>
        /// Nothing here waits on anything. This runs inside the synchronous prefix of the launcher's
        /// <c>StartAsync</c>, which on a Unity build is the main thread, so the process-group settle it
        /// used to do lives on the output-drain thread instead — a frozen editor is a worse failure than
        /// a server whose group is read a few milliseconds later.
        /// <para>
        /// A no-op once <see cref="Stop"/> has run: the launcher publishes an attempt before starting it
        /// so a stop can reach it, and a stop that lands in that window has already disposed the process.
        /// </para>
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
        }

        /// <summary>
        /// Ends this attempt's server and settles anyone waiting on it. Idempotent, because the several
        /// shutdown signals wired to it overlap.
        /// </summary>
        /// <remarks>
        /// The server is asked to shut down before it is killed, so the sessions it is holding are told
        /// they are closing. See <see cref="StopProcess"/> for what "asked" means per platform.
        /// </remarks>
        public void Stop() {
            if (!ClaimEnding(out bool hadProcess)) return;

            ReleaseProcess();

            if (hadProcess) StopProcess();

            _process.Dispose();
        }

        /// <summary>
        /// Lets go of the server without signalling it, leaving it running for whoever is still on it.
        /// </summary>
        /// <remarks>
        /// For the host who leaves a session other players are still in. Everything a stop does happens
        /// except the killing: the handlers come off, the readiness promise settles, and the attempt is
        /// finished — so a later <see cref="Stop"/>, from the quit hook or from teardown, is a no-op and
        /// cannot reach back for a process this attempt has already given up. Nothing else will reap it
        /// either, which is why the server's own idle exit is what ends a detached server.
        /// </remarks>
        public void Detach() {
            if (!ClaimEnding(out bool _)) return;

            ReleaseProcess();
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
            PublishReadyPort(eventArgs.Data);
            SettleProcessGroupOnce();
        }

        private void HandleErrorLine(object sender, DataReceivedEventArgs eventArgs) {
            if (eventArgs.Data == null) return;

            RecordLine(eventArgs.Data);
            SettleProcessGroupOnce();
        }

        /// <remarks>
        /// Published before the group settle below it, not after: the settle can wait out its budget,
        /// and a readiness line the launcher is already waiting on must not queue behind it.
        /// </remarks>
        private void PublishReadyPort(string line) {
            if (!LocalServerLauncher.TryParseReadinessLine(line, out int port)) return;

            _readyPort.TrySetResult(port);
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

        private bool IsStopped() {
            lock (_stateGate) {
                return _isStopped;
            }
        }

        /// <summary>Claims the one ending this attempt gets, reporting whether there is a process to end.</summary>
        private bool ClaimEnding(out bool hadProcess) {
            lock (_stateGate) {
                hadProcess = _hasStarted;

                if (_isStopped) return false;

                _isStopped = true;
                return true;
            }
        }

        /// <summary>Unhooks the handlers and settles the readiness promise — the half a detach shares.</summary>
        /// <remarks>
        /// The promise settles before any killing, so a host request abandoned by teardown or by leaving
        /// play mode unblocks the moment the decision is made rather than after the process has died.
        /// </remarks>
        private void ReleaseProcess() {
            _process.OutputDataReceived -= HandleOutputLine;
            _process.ErrorDataReceived -= HandleErrorLine;
            _process.Exited -= HandleProcessExited;

            _readyPort.TrySetCanceled();
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

        /// <summary>Asks the server to end itself, then kills whatever is left when the grace runs out.</summary>
        /// <remarks>
        /// The kill runs even for a process that has already exited, because "the server is gone" and
        /// "the launch is cleaned up" are different things: a server that forked a helper and exited
        /// leaves that helper holding the port, and the group is the only handle left on it.
        /// </remarks>
        private void StopProcess() {
            if (RequestGracefulStop()) return;

            KillProcessGroup();
            KillProcess();
            WaitForExit(StopWaitMilliseconds);
        }

        /// <summary>
        /// Sends <c>SIGTERM</c> and waits out the grace, and reports whether the server took the hint.
        /// </summary>
        /// <remarks>
        /// The signal is what reaches the dedicated server's own shutdown — closing every session it
        /// holds and pumping until those notices are off the wire — and <c>SIGKILL</c> reaches none of
        /// it. Windows has no portable equivalent for a child with redirected pipes and no shared
        /// console, so it always answers false and the kill below is the whole story there.
        /// </remarks>
        private bool RequestGracefulStop() {
            if (_stopGraceMilliseconds <= 0) return false;
            if (LocalServerPaths.IsWindows()) return false;
            if (!SignalTermination()) return false;

            return WaitForExit(_stopGraceMilliseconds);
        }

        /// <summary>Terminates the whole group when one is known, and the tracked pid alone otherwise.</summary>
        private bool SignalTermination() {
            // The pid may already have been reaped and handed to a stranger, and so may its group.
            if (HasProcessExited()) return false;

            int group = ResolveProcessGroup();

            if (group != NoProcessGroup) return SignalProcessGroup(group, SigTerm);

            return SignalProcess(CurrentProcessId(), SigTerm);
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

        private bool WaitForExit(int milliseconds) {
            try {
                return _process.WaitForExit(milliseconds);
            } catch (Exception exception) {
                Debug.LogWarning($"LocalServerAttempt::WaitForExit->Could not confirm the local server stopped: {exception.Message}");
                return false;
            }
        }

        /// <summary>Signals the whole process group, so helpers the server spawned die with it.</summary>
        private void KillProcessGroup() {
            int group = ReapableProcessGroup();

            if (group == NoProcessGroup) return;

            SignalProcessGroup(group, SigKill);
        }

        /// <summary>The group it is safe to signal right now, or <see cref="NoProcessGroup"/>.</summary>
        /// <remarks>
        /// The existence probe is signal zero, which delivers nothing and only answers "is anybody still
        /// in there". Without it a stop reaches for a group that emptied minutes ago, and the pid that
        /// group is named after is exactly the sort of thing a busy machine hands to somebody else.
        /// </remarks>
        private int ReapableProcessGroup() {
            int candidate = CandidateProcessGroup();

            if (candidate == NoProcessGroup) return NoProcessGroup;

            return SignalProcessGroup(candidate, NoSignal) ? candidate : NoProcessGroup;
        }

        /// <summary>The group name this attempt may still claim, before asking whether it exists.</summary>
        /// <remarks>
        /// A group is named after its leader's pid and the runtime reaps the child the instant it exits,
        /// so the name outlives the process on purpose: reaping a server that forked a helper and exited
        /// at once is the whole reason the group is signalled rather than the pid, and the kernel keeps
        /// the pid reserved for as long as the group has members. Claiming it after the leader is gone
        /// therefore takes two answers, not one. The process handle says the child this attempt started
        /// really has ended — a libc that merely will not talk about it does not count — and a pid that
        /// no longer resolves to any group says the name has not since been handed to a stranger.
        /// </remarks>
        private int CandidateProcessGroup() {
            int verified = ResolveProcessGroup();

            if (verified != NoProcessGroup) return verified;
            if (!_leadsOwnProcessGroup) return NoProcessGroup;
            if (!HasProcessExited()) return NoProcessGroup;

            int processId = CurrentProcessId();

            if (processId <= 0) return NoProcessGroup;
            if (ReadProcessGroup(processId) != NoProcessGroup) return NoProcessGroup;

            return processId;
        }

        /// <summary>Runs the settle at most once, on the first line of output the server produces.</summary>
        /// <remarks>
        /// The first line is the earliest moment this side can know the exec succeeded, and it arrives
        /// on a thread pool thread — which is the whole point. A server that never prints leaves the
        /// group unresolved until a stop asks for it, and a stop resolves it lazily.
        /// </remarks>
        private void SettleProcessGroupOnce() {
            if (Interlocked.Exchange(ref _isProcessGroupSettleClaimed, 1) != 0) return;

            SettleProcessGroup();
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
        /// still shows this process's group. Reading it while the server is certainly alive is the
        /// cheapest way to be sure, which is why it happens here and not only at kill time.
        /// <para>
        /// Every exit condition is here because the alternative is waiting out the budget for an answer
        /// that can no longer arrive: a dead process, a stopped attempt and an unusable libc are all
        /// permanent, and polling them costs a quarter of a second per launch that fails to exec.
        /// </para>
        /// </remarks>
        private void SettleProcessGroup() {
            if (!_leadsOwnProcessGroup) return;

            var watch = Stopwatch.StartNew();

            while (ResolveProcessGroup() == NoProcessGroup && watch.ElapsedMilliseconds < ProcessGroupSettleMilliseconds) {
                if (!IsProcessGroupApiUsable()) return;
                if (HasProcessExited()) return;
                if (IsStopped()) return;

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

        /// <summary>This process's own group, read once and remembered.</summary>
        /// <remarks>
        /// Cached because nothing in a Unity player or editor ever calls <c>setpgid</c> on itself — not
        /// because a group is immutable; it is not. A failed read is remembered too. Retrying it every
        /// launch would keep re-asking a question the host has already answered, and leaving the failure
        /// looking like "group 0" is what turns the comparison below it into a guard that passes
        /// everything.
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

        private static bool SignalProcessGroup(int processGroupId, int signal) {
            if (!IsProcessGroupApiUsable()) return false;

            try {
                return SignalProcessGroupNative(processGroupId, signal) == 0;
            } catch (DllNotFoundException) {
                DisableProcessGroupApi();
            } catch (EntryPointNotFoundException) {
                DisableProcessGroupApi();
            }

            return false;
        }

        /// <summary>Signals one pid, for the hosts and platforms where no group could be proved.</summary>
        /// <remarks>
        /// Shares the group path's latch: the two entry points live in the same libc, so a host where
        /// one name will not bind is a host where neither will, and one failure turns both off.
        /// </remarks>
        private static bool SignalProcess(int processId, int signal) {
            if (processId <= 0) return false;
            if (!IsProcessGroupApiUsable()) return false;

            try {
                return SignalProcessNative(processId, signal) == 0;
            } catch (DllNotFoundException) {
                DisableProcessGroupApi();
            } catch (EntryPointNotFoundException) {
                DisableProcessGroupApi();
            }

            return false;
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

        /// <remarks>
        /// The single-pid counterpart of <c>killpg</c>. <c>Process.Kill</c> only ever sends
        /// <c>SIGKILL</c>, and a server killed that way never runs the shutdown its guests are waiting
        /// on, so the polite half of a stop has to go through libc as well.
        /// </remarks>
        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        private static extern int SignalProcessNative(int processId, int signal);
    }
}
