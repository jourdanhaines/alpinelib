using System.Threading;
using Microsoft.Extensions.Hosting;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Stands in for the generic host's lifetime so a test can watch the loop ask to be shut down without
    /// building a host to be shut down.
    /// </summary>
    /// <remarks>
    /// The idle exit is only observable as a call to <c>StopApplication</c>; a null lifetime — which is
    /// what the harness passed before — swallows it. This records the ask instead.
    /// </remarks>
    internal sealed class StubHostLifetime : IHostApplicationLifetime {
        private readonly CancellationTokenSource _started = new CancellationTokenSource();
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
        private readonly CancellationTokenSource _stopped = new CancellationTokenSource();

        /// <inheritdoc />
        public CancellationToken ApplicationStarted => _started.Token;

        /// <inheritdoc />
        public CancellationToken ApplicationStopping => _stopping.Token;

        /// <inheritdoc />
        public CancellationToken ApplicationStopped => _stopped.Token;

        /// <summary>True once the loop has asked the host to stop.</summary>
        public bool WasStopRequested => _stopping.IsCancellationRequested;

        /// <inheritdoc />
        public void StopApplication() {
            if (_stopping.IsCancellationRequested) {
                return;
            }

            _stopping.Cancel();
        }
    }
}
