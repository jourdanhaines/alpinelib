namespace AlpineLib.Chat {
    /// <summary>
    /// What became of a send attempt. The server returns exactly one of these per attempt, so the client
    /// can tell the player why nothing appeared instead of dropping the line silently.
    /// </summary>
    public enum ChatSendStatus : byte {
        /// <summary>The line was accepted and broadcast. The result carries its message id.</summary>
        Accepted = 0,

        /// <summary>Too many lines too quickly. The result carries how long to wait.</summary>
        RateLimited = 1,

        /// <summary>Longer than the configured maximum.</summary>
        TooLong = 2,

        /// <summary>Nothing but whitespace.</summary>
        Empty = 3,

        /// <summary>Identical to something the same player just said.</summary>
        Duplicate = 4,

        /// <summary>Rejected by a content filter.</summary>
        Filtered = 5,

        /// <summary>The sender is muted right now.</summary>
        Muted = 6,

        /// <summary>No chat transport is connected, so the line never left the machine.</summary>
        NotConnected = 7,

        /// <summary>The sender is not a member of the channel they addressed.</summary>
        UnknownChannel = 8,

        /// <summary>Anything else went wrong, including a timed-out acknowledgement.</summary>
        Failed = 9
    }
}
