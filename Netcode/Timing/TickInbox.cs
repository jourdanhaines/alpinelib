using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace AlpineLib.Netcode.Timing {
    /// <summary>
    /// Single-consumer work queue that marshals callbacks onto the tick thread.
    ///
    /// This is the seam that keeps asynchronous work out of the simulation. Auth validation, backend
    /// lookups and any other <c>Task</c>-returning call complete on a thread-pool thread and post their
    /// result here; the session drains the queue at the top of its tick and mutates state from exactly
    /// one thread. Nothing in the session or replication layer ever awaits inline.
    ///
    /// Post is safe from any thread; Drain must only ever be called from the tick thread.
    /// </summary>
    public sealed class TickInbox {
        private readonly ConcurrentQueue<Action> pending = new ConcurrentQueue<Action>();

        /// <summary>Commands waiting to run on the next drain.</summary>
        public int PendingCount => pending.Count;

        /// <summary>
        /// Raised on the tick thread when a queued command throws. With no subscriber the exception
        /// propagates out of <see cref="Drain"/> instead, so a bug is never silently swallowed.
        /// </summary>
        public event Action<Exception> CommandFailed;

        /// <summary>Queues a command to run on the next drain. Safe from any thread.</summary>
        public void Post(Action command) {
            if (command == null) {
                throw new ArgumentNullException(nameof(command));
            }

            pending.Enqueue(command);
        }

        /// <summary>
        /// Queues a function and returns a task that completes with its result once the tick thread runs
        /// it. Continuations are forced asynchronous so an awaiting caller can never hijack the tick
        /// thread and run arbitrary work inside the simulation step.
        /// </summary>
        public Task<TResult> PostAsync<TResult>(Func<TResult> function) {
            if (function == null) {
                throw new ArgumentNullException(nameof(function));
            }

            var call = new PendingCall<TResult>(function);
            pending.Enqueue(call.Execute);
            return call.Completion;
        }

        /// <summary>Action-shaped overload of <see cref="PostAsync{TResult}"/>.</summary>
        public Task PostAsync(Action command) {
            if (command == null) {
                throw new ArgumentNullException(nameof(command));
            }

            var call = new PendingAction(command);
            pending.Enqueue(call.Execute);
            return call.Completion;
        }

        /// <summary>
        /// Runs everything queued at the moment of the call. The budget is snapshotted first so a command
        /// that posts more work defers it to the next tick rather than spinning this one forever.
        /// </summary>
        public void Drain() {
            int budget = pending.Count;
            while (budget > 0 && pending.TryDequeue(out Action command)) {
                budget--;
                RunCommand(command);
            }
        }

        /// <summary>Discards queued commands without running them. For shutdown paths only.</summary>
        public void Clear() {
            while (pending.TryDequeue(out _)) {
            }
        }

        private void RunCommand(Action command) {
            Action<Exception> failureHandler = CommandFailed;
            if (failureHandler == null) {
                command();
                return;
            }

            try {
                command();
            }
            catch (Exception error) {
                failureHandler.Invoke(error);
            }
        }

        /// <summary>
        /// Holds the state a posted function needs so the queued delegate is a plain method group rather
        /// than a multi-statement closure.
        /// </summary>
        private sealed class PendingCall<TResult> {
            private readonly Func<TResult> function;
            private readonly TaskCompletionSource<TResult> completion;

            public PendingCall(Func<TResult> function) {
                this.function = function;
                completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public Task<TResult> Completion => completion.Task;

            public void Execute() {
                try {
                    completion.TrySetResult(function());
                }
                catch (Exception error) {
                    completion.TrySetException(error);
                }
            }
        }

        /// <summary>Void-returning twin of <see cref="PendingCall{TResult}"/>.</summary>
        private sealed class PendingAction {
            private readonly Action command;
            private readonly TaskCompletionSource<bool> completion;

            public PendingAction(Action command) {
                this.command = command;
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public Task Completion => completion.Task;

            public void Execute() {
                try {
                    command();
                    completion.TrySetResult(true);
                }
                catch (Exception error) {
                    completion.TrySetException(error);
                }
            }
        }
    }
}
