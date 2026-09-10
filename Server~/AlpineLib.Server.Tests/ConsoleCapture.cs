using System;
using System.Text;
using System.IO;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Takes over <see cref="Console.Out"/> for as long as it is held, and hands back what was written.
    /// </summary>
    /// <remarks>
    /// The readiness line goes to stdout rather than through a logger, because a launcher tails stdout;
    /// so a test that wants to see it has to stand where the launcher stands. Every write and every read
    /// takes the same lock: the line is written by the loop thread while the test thread is asking
    /// whether it arrived yet, and a <see cref="StringBuilder"/> read across that boundary is not safe.
    /// </remarks>
    internal sealed class ConsoleCapture : TextWriter {
        private readonly object _gate = new object();
        private readonly StringBuilder _written = new StringBuilder();
        private readonly TextWriter _previous;

        private bool _restored;

        private ConsoleCapture(TextWriter previous) {
            _previous = previous;
        }

        /// <summary>Redirects the console until the returned capture is disposed.</summary>
        public static ConsoleCapture Start() {
            TextWriter previous = Console.Out;
            ConsoleCapture capture = new ConsoleCapture(previous);
            Console.SetOut(capture);
            return capture;
        }

        /// <inheritdoc />
        public override Encoding Encoding => Encoding.UTF8;

        /// <summary>Everything written so far, read under the lock the writers take.</summary>
        public string Text {
            get {
                lock (_gate) {
                    return _written.ToString();
                }
            }
        }

        /// <inheritdoc />
        public override void Write(char value) {
            lock (_gate) {
                _written.Append(value);
            }
        }

        /// <inheritdoc />
        public override void Write(string value) {
            lock (_gate) {
                _written.Append(value);
            }
        }

        /// <inheritdoc />
        public override void WriteLine(string value) {
            lock (_gate) {
                _written.Append(value).Append('\n');
            }
        }

        /// <summary>True when this fragment has been written. Ordinal: this is a wire contract, not prose.</summary>
        public bool Contains(string fragment) {
            return Text.Contains(fragment, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing) {
            if (disposing && !_restored) {
                _restored = true;
                Console.SetOut(_previous);
            }

            base.Dispose(disposing);
        }
    }
}
