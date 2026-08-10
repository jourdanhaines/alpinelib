namespace AlpineLib.Chat {
    /// <summary>
    /// What a delivered chat line actually is, so the UI can style it without parsing the text.
    /// </summary>
    public enum ChatMessageKind : byte {
        /// <summary>A player typed it.</summary>
        Player = 0,

        /// <summary>The server said it: a rule reminder, a match countdown, a moderation notice.</summary>
        System = 1,

        /// <summary>Someone arrived in the channel.</summary>
        Join = 2,

        /// <summary>Someone left the channel.</summary>
        Leave = 3,

        /// <summary>An operator broadcast, rendered louder than an ordinary system line.</summary>
        Announcement = 4
    }
}
