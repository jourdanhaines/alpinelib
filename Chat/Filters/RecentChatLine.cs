namespace AlpineLib.Chat.Filters {
    /// <summary>
    /// The last thing one player said and when they said it — the single entry
    /// <see cref="DuplicateFilter"/> keeps per player.
    /// </summary>
    /// <remarks>
    /// A value type so the filter's dictionary holds no references to text it has finished with beyond
    /// the one line it is still comparing against.
    /// </remarks>
    public readonly struct RecentChatLine {
        private readonly string _text;
        private readonly long _atUnixMs;

        /// <summary>Records a line and its instant.</summary>
        public RecentChatLine(string text, long atUnixMs) {
            _text = text ?? string.Empty;
            _atUnixMs = atUnixMs;
        }

        /// <summary>The line, already normalised by whoever recorded it.</summary>
        public string Text => _text ?? string.Empty;

        /// <summary>When it was said, Unix milliseconds on the server clock.</summary>
        public long AtUnixMs => _atUnixMs;
    }
}
