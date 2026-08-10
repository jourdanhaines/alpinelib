namespace AlpineLib.Chat.Wire {
    /// <summary>
    /// The first byte of every chat frame. Chat carries its own framing inside the opaque payload of one
    /// netcode envelope message, so this is chat's private message id space and is independent of the
    /// protocol-wide id map.
    /// </summary>
    /// <remarks>
    /// Numbering starts at one so a zeroed buffer decodes as <see cref="None"/> rather than as a valid
    /// frame.
    /// </remarks>
    public enum ChatWireMessageType : byte {
        /// <summary>Not a frame. Only ever seen when something handed the decoder empty bytes.</summary>
        None = 0,

        /// <summary>Client to server: please say this. Carries the nonce the acknowledgement echoes.</summary>
        SendRequest = 1,

        /// <summary>Server to client: the ruling on one <see cref="SendRequest"/>.</summary>
        SendAck = 2,

        /// <summary>Server to client: a line delivered to a channel the receiver is in.</summary>
        Broadcast = 3,

        /// <summary>Client to server: give me older messages from this channel.</summary>
        HistoryRequest = 4,

        /// <summary>Server to client: the messages asked for, oldest first.</summary>
        HistoryResponse = 5,

        /// <summary>Server to client: channel membership changed.</summary>
        ChannelEvent = 6
    }
}
