using System;

namespace AlpineLib.Chat {
    /// <summary>
    /// A refilling allowance of actions: hold <see cref="Capacity"/> tokens, spend one per action, gain
    /// one back every <see cref="RefillSeconds"/>.
    /// </summary>
    /// <remarks>
    /// Chosen over a fixed window because it matches how people actually chat — a short burst of four
    /// lines while answering a question is fine, a sustained line every half second is not. A fixed
    /// window would either forbid the burst or permit the sustained spam, depending on where the window
    /// boundary happened to fall.
    /// <para>
    /// Time is supplied by the caller rather than read from a clock, so the pipeline can stamp one
    /// instant across every filter and tests can advance time by hand. A clock that goes backwards is
    /// ignored rather than rewinding the bucket: crediting elapsed time from a rewound stamp would hand
    /// a player free tokens for every backwards step the clock took.
    /// </para>
    /// </remarks>
    public sealed class TokenBucket {
        private readonly double _capacity;
        private readonly double _refillSeconds;
        private double _tokens;
        private double _lastUpdatedSeconds;

        /// <summary>Creates a full bucket.</summary>
        /// <param name="capacity">How many actions may happen back to back. Clamped to at least one.</param>
        /// <param name="refillSeconds">Seconds to regain one token. Clamped to a positive value.</param>
        /// <param name="startSeconds">The instant the bucket starts from.</param>
        public TokenBucket(int capacity, double refillSeconds, double startSeconds) {
            _capacity = capacity < 1 ? 1.0 : capacity;
            _refillSeconds = refillSeconds <= 0.0 ? 0.001 : refillSeconds;
            _tokens = _capacity;
            _lastUpdatedSeconds = startSeconds;
        }

        /// <summary>Maximum tokens the bucket holds, i.e. the allowed burst.</summary>
        public double Capacity => _capacity;

        /// <summary>Seconds needed to regain one token.</summary>
        public double RefillSeconds => _refillSeconds;

        /// <summary>Tokens available as of the last call. Diagnostic only — refill lazily on use.</summary>
        public double AvailableTokens => _tokens;

        /// <summary>Spends one token if there is one, refilling for elapsed time first.</summary>
        public bool TryConsume(double nowSeconds) {
            Refill(nowSeconds);

            if (_tokens < 1.0) {
                return false;
            }

            _tokens -= 1.0;
            return true;
        }

        /// <summary>
        /// How long until the next token arrives, in seconds. Zero when one is already available.
        /// </summary>
        public double SecondsUntilNextToken(double nowSeconds) {
            Refill(nowSeconds);

            if (_tokens >= 1.0) {
                return 0.0;
            }

            return (1.0 - _tokens) * _refillSeconds;
        }

        /// <summary>
        /// How long until the next token arrives, rounded up to whole milliseconds — the unit the wire
        /// and the send result use.
        /// </summary>
        public int MillisecondsUntilNextToken(double nowSeconds) {
            double seconds = SecondsUntilNextToken(nowSeconds);
            return (int)Math.Ceiling(seconds * 1000.0);
        }

        /// <summary>Refills the bucket to full as of the given instant.</summary>
        public void Reset(double nowSeconds) {
            _tokens = _capacity;
            _lastUpdatedSeconds = nowSeconds;
        }

        private void Refill(double nowSeconds) {
            double elapsed = nowSeconds - _lastUpdatedSeconds;

            if (elapsed <= 0.0) {
                return;
            }

            _lastUpdatedSeconds = nowSeconds;
            _tokens += elapsed / _refillSeconds;

            if (_tokens > _capacity) {
                _tokens = _capacity;
            }
        }
    }
}
