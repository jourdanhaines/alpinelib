using System.Collections.Generic;
using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Chat.Filters {
    /// <summary>
    /// Holds each player to a burst of lines plus a steady refill, using one <see cref="TokenBucket"/>
    /// per player.
    /// </summary>
    /// <remarks>
    /// The refusal carries a retry delay taken from the bucket itself, so the client can grey the send
    /// button for exactly as long as it needs to be grey rather than guessing.
    /// </remarks>
    public sealed class RateLimitFilter : IChatFilter {
        private readonly Dictionary<PlayerId, TokenBucket> _buckets = new Dictionary<PlayerId, TokenBucket>();
        private readonly int _burst;
        private readonly double _refillSeconds;

        /// <summary>Creates a limiter with the given burst size and refill rate.</summary>
        public RateLimitFilter(int burst, double refillSeconds) {
            _burst = burst < 1 ? 1 : burst;
            _refillSeconds = refillSeconds <= 0.0 ? 0.001 : refillSeconds;
        }

        /// <summary>How many lines a player may send back to back.</summary>
        public int Burst => _burst;

        /// <summary>Seconds a player waits to regain one line of allowance.</summary>
        public double RefillSeconds => _refillSeconds;

        /// <inheritdoc />
        public string Name => "rate-limit";

        /// <inheritdoc />
        public ChatFilterResult Evaluate(in ChatFilterContext context) {
            TokenBucket bucket = GetBucket(context.SenderId, context.NowSeconds);

            if (bucket.TryConsume(context.NowSeconds)) {
                return ChatFilterResult.Allow();
            }

            int retryAfterMs = bucket.MillisecondsUntilNextToken(context.NowSeconds);

            return ChatFilterResult.Reject(
                ChatSendStatus.RateLimited,
                "Sending faster than " + _burst.ToString() + " messages per burst allows.",
                retryAfterMs);
        }

        /// <inheritdoc />
        public void Forget(PlayerId player) {
            _buckets.Remove(player);
        }

        private TokenBucket GetBucket(PlayerId player, double nowSeconds) {
            if (_buckets.TryGetValue(player, out TokenBucket existing)) {
                return existing;
            }

            TokenBucket created = new TokenBucket(_burst, _refillSeconds, nowSeconds);
            _buckets.Add(player, created);
            return created;
        }
    }
}
