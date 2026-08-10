using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Chat {
    /// <summary>
    /// Everything a filter is allowed to look at when ruling on one send attempt.
    /// </summary>
    /// <remarks>
    /// Time arrives as a field rather than being read from the clock inside a filter: the pipeline
    /// stamps one instant for the whole chain, so a rate limiter and a duplicate detector cannot
    /// disagree about when the message happened, and a test can advance time by hand.
    /// </remarks>
    public readonly struct ChatFilterContext {
        private readonly PlayerId _senderId;
        private readonly string _senderDisplayName;
        private readonly ChatChannelId _channel;
        private readonly string _text;
        private readonly long _nowUnixMs;

        /// <summary>Creates a context for one send attempt.</summary>
        public ChatFilterContext(
            PlayerId senderId,
            string senderDisplayName,
            ChatChannelId channel,
            string text,
            long nowUnixMs) {
            _senderId = senderId;
            _senderDisplayName = senderDisplayName ?? string.Empty;
            _channel = channel;
            _text = text ?? string.Empty;
            _nowUnixMs = nowUnixMs;
        }

        /// <summary>Who is trying to send.</summary>
        public PlayerId SenderId => _senderId;

        /// <summary>What they are called.</summary>
        public string SenderDisplayName => _senderDisplayName ?? string.Empty;

        /// <summary>Where they are trying to send.</summary>
        public ChatChannelId Channel => _channel;

        /// <summary>
        /// The text as it stands at this point in the chain — already rewritten by any earlier filter
        /// that sanitised it.
        /// </summary>
        public string Text => _text ?? string.Empty;

        /// <summary>Server clock for this attempt, Unix milliseconds.</summary>
        public long NowUnixMs => _nowUnixMs;

        /// <summary>The same instant expressed in seconds, which is what the token bucket works in.</summary>
        public double NowSeconds => _nowUnixMs / 1000.0;

        /// <summary>Copies the context with the text replaced, for handing down the chain after a sanitise.</summary>
        public ChatFilterContext WithText(string text) {
            return new ChatFilterContext(_senderId, _senderDisplayName, _channel, text, _nowUnixMs);
        }
    }
}
