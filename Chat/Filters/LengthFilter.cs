using System;
using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Chat.Filters {
    /// <summary>
    /// Trims surrounding whitespace and refuses lines that are empty or longer than the configured
    /// maximum.
    /// </summary>
    /// <remarks>
    /// Runs first in the chain so nothing downstream has to defend itself against a null, a wall of
    /// spaces, or a megabyte of text. An empty line is refused without counting as a violation — it is a
    /// client bug or a stray Enter key, not abuse.
    /// </remarks>
    public sealed class LengthFilter : IChatFilter {
        private readonly int _maxMessageLength;

        /// <summary>Creates a filter capping lines at the given length.</summary>
        public LengthFilter(int maxMessageLength) {
            _maxMessageLength = maxMessageLength < 1 ? 1 : maxMessageLength;
        }

        /// <summary>The cap this filter enforces.</summary>
        public int MaxMessageLength => _maxMessageLength;

        /// <inheritdoc />
        public string Name => "length";

        /// <inheritdoc />
        public ChatFilterResult Evaluate(in ChatFilterContext context) {
            string trimmed = context.Text.Trim();

            if (trimmed.Length == 0) {
                return ChatFilterResult.RejectWithoutViolation(ChatSendStatus.Empty, "Message was empty.");
            }

            if (trimmed.Length > _maxMessageLength) {
                return ChatFilterResult.Reject(
                    ChatSendStatus.TooLong,
                    "Message of " + trimmed.Length.ToString() + " characters exceeds the limit of "
                    + _maxMessageLength.ToString() + ".");
            }

            if (string.Equals(trimmed, context.Text, StringComparison.Ordinal)) {
                return ChatFilterResult.Allow();
            }

            return ChatFilterResult.Sanitize(trimmed);
        }

        /// <inheritdoc />
        public void Forget(PlayerId player) {
        }
    }
}
