using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Chat.Filters {
    /// <summary>
    /// Refuses a line identical to the one the same player just sent, within a short window.
    /// </summary>
    /// <remarks>
    /// Catches the two things the rate limiter misses: the panicked double-send of an impatient player,
    /// and the slow drip of the same taunt every few seconds that stays under the burst allowance.
    /// Comparison ignores case and surrounding whitespace, because "HI" and "hi " are the same spam.
    /// <para>
    /// Only the most recent line per player is remembered — one entry, not a history — so a server that
    /// runs for weeks does not accumulate text. A repeat refreshes the window, so hammering the same
    /// line keeps it blocked rather than letting it through once the original ages out.
    /// </para>
    /// </remarks>
    public sealed class DuplicateFilter : IChatFilter {
        private readonly Dictionary<PlayerId, RecentChatLine> _recentLines =
            new Dictionary<PlayerId, RecentChatLine>();

        private readonly long _windowMs;

        /// <summary>Creates a filter with the given repeat window.</summary>
        public DuplicateFilter(double windowSeconds) {
            double clamped = windowSeconds < 0.0 ? 0.0 : windowSeconds;
            _windowMs = (long)(clamped * 1000.0);
        }

        /// <summary>How long a line stays "just said", in milliseconds.</summary>
        public long WindowMs => _windowMs;

        /// <inheritdoc />
        public string Name => "duplicate";

        /// <inheritdoc />
        public ChatFilterResult Evaluate(in ChatFilterContext context) {
            string normalized = context.Text.Trim();
            bool isRepeat = IsRepeat(context.SenderId, normalized, context.NowUnixMs);

            _recentLines[context.SenderId] = new RecentChatLine(normalized, context.NowUnixMs);

            if (!isRepeat) {
                return ChatFilterResult.Allow();
            }

            return ChatFilterResult.Reject(
                ChatSendStatus.Duplicate,
                "Repeated the same message within " + _windowMs.ToString() + " ms.");
        }

        /// <inheritdoc />
        public void Forget(PlayerId player) {
            _recentLines.Remove(player);
        }

        private bool IsRepeat(PlayerId player, string normalized, long nowUnixMs) {
            if (!_recentLines.TryGetValue(player, out RecentChatLine previous)) {
                return false;
            }

            if (nowUnixMs - previous.AtUnixMs > _windowMs) {
                return false;
            }

            return string.Equals(previous.Text, normalized, StringComparison.OrdinalIgnoreCase);
        }
    }
}
