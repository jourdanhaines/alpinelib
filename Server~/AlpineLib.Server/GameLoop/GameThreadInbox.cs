using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace AlpineLib.Server.GameLoop {
    /// <summary>
    /// The one door into the game thread. Anything running off it — an admin surface, a health probe, a
    /// shutdown hook — hands its work through here and waits for the answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sessions, the registry and the replication worlds are single-threaded by design and hold no
    /// locks anywhere. That is only safe because nothing else ever touches them: a request handler that
    /// read a roster directly would be racing a tick that rewrites it mid-read. So every read the web
    /// surface performs is posted here, executed between two ticks, and returned as an immutable copy.
    /// </para>
    /// <para>
    /// <b>It refuses work once the loop is gone.</b> A queue that keeps accepting posts after the last
    /// drain leaves its callers awaiting a task nothing will ever complete, which during shutdown means
    /// requests that hang until the socket is torn out from under them. Posting after
    /// <see cref="Close"/> fails immediately instead, and whatever was still queued is failed the same
    /// way.
    /// </para>
    /// </remarks>
    public sealed class GameThreadInbox {
        private readonly ConcurrentQueue<IGameThreadWorkItem> _pending = new ConcurrentQueue<IGameThreadWorkItem>();

        private volatile bool _isClosed;

        /// <summary>Raised on the game thread when queued work throws, so the host can log it.</summary>
        public event Action<Exception> WorkFailed;

        /// <summary>Work waiting for the next drain.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>True once the loop has stopped accepting work.</summary>
        public bool IsClosed => _isClosed;

        /// <summary>Queues a command to run on the next tick. Safe from any thread.</summary>
        public Task PostAsync(Action command) {
            if (command == null) {
                throw new ArgumentNullException(nameof(command));
            }

            GameThreadCommand workItem = new GameThreadCommand(command);
            Enqueue(workItem);
            return workItem.Completion;
        }

        /// <summary>
        /// Queues a function and hands back its result once the game thread has run it. Continuations are
        /// forced asynchronous, so an awaiting request handler can never resume inside the tick.
        /// </summary>
        public Task<TResult> PostAsync<TResult>(Func<TResult> function) {
            if (function == null) {
                throw new ArgumentNullException(nameof(function));
            }

            GameThreadCall<TResult> workItem = new GameThreadCall<TResult>(function);
            Enqueue(workItem);
            return workItem.Completion;
        }

        /// <summary>
        /// Runs everything queued at the moment of the call. Must only ever be called from the game
        /// thread.
        /// </summary>
        /// <remarks>
        /// The budget is taken before the first item runs, so work that posts more work defers it to the
        /// next tick instead of holding this one open indefinitely.
        /// </remarks>
        public void Drain() {
            int budget = _pending.Count;

            while (budget > 0 && _pending.TryDequeue(out IGameThreadWorkItem workItem)) {
                budget--;
                RunWorkItem(workItem);
            }
        }

        /// <summary>
        /// Stops accepting work and fails whatever is still queued. Called once the loop's last drain has
        /// run, so nobody is left awaiting a tick that will not come.
        /// </summary>
        public void Close() {
            _isClosed = true;

            while (_pending.TryDequeue(out IGameThreadWorkItem workItem)) {
                workItem.Cancel();
            }
        }

        private void Enqueue(IGameThreadWorkItem workItem) {
            if (_isClosed) {
                workItem.Cancel();
                return;
            }

            _pending.Enqueue(workItem);

            // Closing races an enqueue: whoever set the flag has already drained the queue, so anything
            // that slipped in behind them has to be failed here or it waits forever.
            if (_isClosed && _pending.TryDequeue(out IGameThreadWorkItem stranded)) {
                stranded.Cancel();
            }
        }

        private void RunWorkItem(IGameThreadWorkItem workItem) {
            try {
                workItem.Execute();
            }
            catch (Exception error) {
                WorkFailed?.Invoke(error);
            }
        }

        /// <summary>One posted unit of work, whatever shape its result takes.</summary>
        private interface IGameThreadWorkItem {
            /// <summary>Runs the work on the game thread and settles its task.</summary>
            void Execute();

            /// <summary>Fails the work because the loop will never run it.</summary>
            void Cancel();
        }

        /// <summary>A posted action. Holds its state so the queued item is never a closure.</summary>
        private sealed class GameThreadCommand : IGameThreadWorkItem {
            private readonly Action _command;
            private readonly TaskCompletionSource<bool> _completion =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public GameThreadCommand(Action command) {
                _command = command;
            }

            public Task Completion => _completion.Task;

            public void Execute() {
                try {
                    _command();
                    _completion.TrySetResult(true);
                }
                catch (Exception error) {
                    _completion.TrySetException(error);
                }
            }

            public void Cancel() {
                _completion.TrySetException(new InvalidOperationException("The game loop is not running."));
            }
        }

        /// <summary>A posted function, twin of <see cref="GameThreadCommand"/>.</summary>
        private sealed class GameThreadCall<TResult> : IGameThreadWorkItem {
            private readonly Func<TResult> _function;
            private readonly TaskCompletionSource<TResult> _completion =
                new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            public GameThreadCall(Func<TResult> function) {
                _function = function;
            }

            public Task<TResult> Completion => _completion.Task;

            public void Execute() {
                try {
                    _completion.TrySetResult(_function());
                }
                catch (Exception error) {
                    _completion.TrySetException(error);
                }
            }

            public void Cancel() {
                _completion.TrySetException(new InvalidOperationException("The game loop is not running."));
            }
        }
    }
}
