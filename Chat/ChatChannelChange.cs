namespace AlpineLib.Chat {
    /// <summary>
    /// What happened to a channel's membership. Distinguishes the local player's own subscription
    /// changing from somebody else's, because the two drive different UI.
    /// </summary>
    public enum ChatChannelChange : byte {
        /// <summary>The local player is now subscribed to the channel.</summary>
        Joined = 0,

        /// <summary>The local player is no longer subscribed.</summary>
        Left = 1,

        /// <summary>Another player joined a channel the local player is in.</summary>
        MemberJoined = 2,

        /// <summary>Another player left a channel the local player is in.</summary>
        MemberLeft = 3,

        /// <summary>The channel stopped existing; everything addressed to it now goes nowhere.</summary>
        Closed = 4
    }
}
