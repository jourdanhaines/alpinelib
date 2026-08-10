using System;

namespace AlpineLib.Chat {
    /// <summary>
    /// The server's answer to one send attempt: whether it landed, what id it got, and — when it was
    /// throttled — how long to wait before trying again.
    /// </summary>
    public readonly struct ChatSendResult : IEquatable<ChatSendResult> {
        private readonly ChatSendStatus _status;
        private readonly ulong _messageId;
        private readonly int _retryAfterMs;

        /// <summary>Builds a result directly. Prefer the named factories.</summary>
        public ChatSendResult(ChatSendStatus status, ulong messageId, int retryAfterMs) {
            _status = status;
            _messageId = messageId;
            _retryAfterMs = retryAfterMs < 0 ? 0 : retryAfterMs;
        }

        /// <summary>What became of the attempt.</summary>
        public ChatSendStatus Status => _status;

        /// <summary>The id the accepted line was given. Zero for every rejection.</summary>
        public ulong MessageId => _messageId;

        /// <summary>How long to wait before retrying, milliseconds. Zero when retrying will not help.</summary>
        public int RetryAfterMs => _retryAfterMs;

        /// <summary>True when the line was broadcast.</summary>
        public bool IsAccepted => _status == ChatSendStatus.Accepted;

        /// <summary>The line landed and was given an id.</summary>
        public static ChatSendResult Accepted(ulong messageId) {
            return new ChatSendResult(ChatSendStatus.Accepted, messageId, 0);
        }

        /// <summary>The line was refused for a reason waiting will not fix.</summary>
        public static ChatSendResult Rejected(ChatSendStatus status) {
            return new ChatSendResult(status, 0uL, 0);
        }

        /// <summary>The line was refused for now; retrying after the given delay may work.</summary>
        public static ChatSendResult Throttled(ChatSendStatus status, int retryAfterMs) {
            return new ChatSendResult(status, 0uL, retryAfterMs);
        }

        /// <inheritdoc />
        public bool Equals(ChatSendResult other) {
            return _status == other._status && _messageId == other._messageId && _retryAfterMs == other._retryAfterMs;
        }

        /// <inheritdoc />
        public override bool Equals(object obj) {
            return obj is ChatSendResult other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() {
            return (((int)_status * 397) ^ _messageId.GetHashCode()) * 397 ^ _retryAfterMs;
        }

        /// <inheritdoc />
        public override string ToString() {
            return _status + "(id=" + _messageId.ToString() + ", retryAfterMs=" + _retryAfterMs.ToString() + ")";
        }

        /// <summary>Value equality.</summary>
        public static bool operator ==(ChatSendResult left, ChatSendResult right) {
            return left.Equals(right);
        }

        /// <summary>Value inequality.</summary>
        public static bool operator !=(ChatSendResult left, ChatSendResult right) {
            return !left.Equals(right);
        }
    }
}
