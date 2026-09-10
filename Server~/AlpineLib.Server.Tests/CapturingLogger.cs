using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// A logger that keeps what it was told, so a test can assert on a line the server writes for an
    /// operator rather than only on the behaviour behind it.
    /// </summary>
    /// <remarks>
    /// Some faults are only ever visible in the log — a spawn export whose points went missing still
    /// seats everybody, just in the wrong place — so the line is part of the contract and is worth
    /// pinning. The lines are guarded because the loop thread is what writes most of them while the test
    /// thread reads.
    /// </remarks>
    internal sealed class CapturingLogger<TCategoryName> : ILogger<TCategoryName> {
        private readonly object _gate = new object();
        private readonly List<string> _lines = new List<string>();

        /// <summary>Every line written so far, formatted, in order.</summary>
        public IReadOnlyList<string> Lines {
            get {
                lock (_gate) {
                    return _lines.ToArray();
                }
            }
        }

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state) where TState : notnull {
            return NullLogger.Instance.BeginScope(state);
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) {
            return true;
        }

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter) {
            if (formatter == null) {
                return;
            }

            lock (_gate) {
                _lines.Add(logLevel.ToString() + ": " + formatter(state, exception));
            }
        }

        /// <summary>How many lines carry this fragment. Ordinal, because a log line is not prose.</summary>
        public int CountContaining(string fragment) {
            IReadOnlyList<string> lines = Lines;
            int matches = 0;

            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++) {
                if (lines[lineIndex].Contains(fragment, StringComparison.Ordinal)) {
                    matches++;
                }
            }

            return matches;
        }
    }
}
