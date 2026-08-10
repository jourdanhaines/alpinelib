namespace AlpineLib.Chat {
    /// <summary>
    /// The family a chat channel belongs to. The kind decides routing and permissions; the key inside
    /// <see cref="ChatChannelId"/> decides which instance of that family.
    /// </summary>
    /// <remarks>
    /// Only <see cref="Room"/> and <see cref="System"/> carry traffic in v1. The remaining values are
    /// reserved so their byte encodings are fixed now — adding them later must not renumber the wire.
    /// </remarks>
    public enum ChatChannelKind : byte {
        /// <summary>Everyone in one session (the igloo and the matches it launches). The v1 workhorse.</summary>
        Room = 0,

        /// <summary>Reserved: the launched party once parties stop being "everyone in the lobby".</summary>
        Party = 1,

        /// <summary>Reserved: one-to-one messages between two players.</summary>
        Whisper = 2,

        /// <summary>Reserved: server-wide chat across every session in a process.</summary>
        Global = 3,

        /// <summary>Server-authored notices. Nothing a client sends is ever accepted on this kind.</summary>
        System = 4
    }
}
