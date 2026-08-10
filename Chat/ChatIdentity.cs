using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Chat {
    /// <summary>
    /// Who a provider connects as. Handed to <see cref="IChatProvider.ConnectAsync"/> once the session
    /// handshake has already established the identity — chat never authenticates anybody itself.
    /// </summary>
    /// <remarks>
    /// Carries an opaque token for the external-provider case, where a hosted chat service wants its own
    /// proof rather than trusting whatever the client claims. The built-in provider ignores it.
    /// </remarks>
    public readonly struct ChatIdentity {
        private readonly PlayerId _playerId;
        private readonly string _displayName;
        private readonly string _roomKey;
        private readonly string _token;

        /// <summary>Creates an identity for the built-in provider, which needs no token.</summary>
        public ChatIdentity(PlayerId playerId, string displayName, string roomKey)
            : this(playerId, displayName, roomKey, string.Empty) { }

        /// <summary>Creates an identity carrying a provider-specific token.</summary>
        public ChatIdentity(PlayerId playerId, string displayName, string roomKey, string token) {
            _playerId = playerId;
            _displayName = displayName ?? string.Empty;
            _roomKey = roomKey ?? string.Empty;
            _token = token ?? string.Empty;
        }

        /// <summary>The identity established by the session handshake.</summary>
        public PlayerId PlayerId => _playerId;

        /// <summary>The name other players will see on this player's lines.</summary>
        public string DisplayName => _displayName ?? string.Empty;

        /// <summary>Key of the room channel to join on connect. Empty joins nothing.</summary>
        public string RoomKey => _roomKey ?? string.Empty;

        /// <summary>Opaque proof for an external provider. Empty for the built-in one.</summary>
        public string Token => _token ?? string.Empty;

        /// <summary>The room channel this identity belongs in.</summary>
        public ChatChannelId RoomChannel => ChatChannelId.Room(RoomKey);
    }
}
