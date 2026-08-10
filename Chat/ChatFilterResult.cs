namespace AlpineLib.Chat {
    /// <summary>
    /// One filter's ruling: let the line through, rewrite it, or refuse it.
    /// </summary>
    /// <remarks>
    /// <see cref="IsViolation"/> is separate from <see cref="IsAllowed"/> because not every refusal is
    /// misbehaviour. Spamming and swearing should count towards an automatic mute; an empty message from
    /// a client with a sloppy input box should not. The pipeline tallies violations, so filters decide
    /// what is worth tallying rather than the pipeline guessing from the status.
    /// </remarks>
    public readonly struct ChatFilterResult {
        private readonly bool _isAllowed;
        private readonly bool _isViolation;
        private readonly ChatSendStatus _status;
        private readonly string _reason;
        private readonly string _replacementText;
        private readonly int _retryAfterMs;

        private ChatFilterResult(
            bool isAllowed,
            bool isViolation,
            ChatSendStatus status,
            string reason,
            string replacementText,
            int retryAfterMs) {
            _isAllowed = isAllowed;
            _isViolation = isViolation;
            _status = status;
            _reason = reason;
            _replacementText = replacementText;
            _retryAfterMs = retryAfterMs < 0 ? 0 : retryAfterMs;
        }

        /// <summary>True when the line may continue down the chain.</summary>
        public bool IsAllowed => _isAllowed;

        /// <summary>True when this ruling should count towards the sender's automatic-mute tally.</summary>
        public bool IsViolation => _isViolation;

        /// <summary>What the sender is told. <see cref="ChatSendStatus.Accepted"/> when allowed.</summary>
        public ChatSendStatus Status => _status;

        /// <summary>Human-readable explanation for logs and the moderation sink. Empty when allowed.</summary>
        public string Reason => _reason ?? string.Empty;

        /// <summary>Rewritten text, or null when the filter left the line alone.</summary>
        public string ReplacementText => _replacementText;

        /// <summary>How long the sender should wait before retrying. Zero when waiting will not help.</summary>
        public int RetryAfterMs => _retryAfterMs;

        /// <summary>True when the filter rewrote the line rather than passing it through untouched.</summary>
        public bool HasReplacement => _replacementText != null;

        /// <summary>The line is fine as it stands.</summary>
        public static ChatFilterResult Allow() {
            return new ChatFilterResult(true, false, ChatSendStatus.Accepted, string.Empty, null, 0);
        }

        /// <summary>The line continues, but with this text instead of what the sender typed.</summary>
        public static ChatFilterResult Sanitize(string replacementText) {
            return new ChatFilterResult(
                true,
                false,
                ChatSendStatus.Accepted,
                string.Empty,
                replacementText ?? string.Empty,
                0);
        }

        /// <summary>The line is refused and the refusal counts against the sender.</summary>
        public static ChatFilterResult Reject(ChatSendStatus status, string reason) {
            return new ChatFilterResult(false, true, status, reason, null, 0);
        }

        /// <summary>The line is refused for now; retrying after the given delay may work.</summary>
        public static ChatFilterResult Reject(ChatSendStatus status, string reason, int retryAfterMs) {
            return new ChatFilterResult(false, true, status, reason, null, retryAfterMs);
        }

        /// <summary>The line is refused, but the sender is not being blamed for it.</summary>
        public static ChatFilterResult RejectWithoutViolation(ChatSendStatus status, string reason) {
            return new ChatFilterResult(false, false, status, reason, null, 0);
        }

        /// <inheritdoc />
        public override string ToString() {
            return _isAllowed ? "Allow" : "Reject(" + _status + ": " + Reason + ")";
        }
    }
}
