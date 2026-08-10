using System;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Chat {
    /// <summary>
    /// Addresses a chat channel: a <see cref="ChatChannelKind"/> plus the key that picks one channel out
    /// of that kind (a session id for a room, a player id for a whisper, nothing for global).
    /// </summary>
    /// <remarks>
    /// Deliberately a value type with no server-side registry behind it. Channels are not objects that
    /// have to be created before they can be addressed — the server decides membership, and an id that
    /// nobody is subscribed to simply delivers to nobody. Keys compare case-insensitively so a channel
    /// survives a round trip through anything that upper-cases identifiers.
    /// </remarks>
    public readonly struct ChatChannelId : IEquatable<ChatChannelId> {
        /// <summary>Longest key accepted on the wire, so a hostile client cannot buy memory with a string.</summary>
        public const int MaxKeyLength = 64;

        private readonly ChatChannelKind _kind;
        private readonly string _key;

        /// <summary>Builds an id directly. Prefer the named factories.</summary>
        public ChatChannelId(ChatChannelKind kind, string key) {
            _kind = kind;
            _key = key ?? string.Empty;
        }

        /// <summary>Which family of channel this addresses.</summary>
        public ChatChannelKind Kind => _kind;

        /// <summary>Which instance within the kind. Empty for kinds that have exactly one channel.</summary>
        public string Key => _key ?? string.Empty;

        /// <summary>False only for the default value, which addresses nothing.</summary>
        public bool IsValid => _kind != ChatChannelKind.Room || !string.IsNullOrEmpty(_key);

        /// <summary>The channel every member of a session shares.</summary>
        public static ChatChannelId Room(string sessionKey) {
            return new ChatChannelId(ChatChannelKind.Room, sessionKey);
        }

        /// <summary>Reserved: the channel a launched party shares.</summary>
        public static ChatChannelId Party(string partyKey) {
            return new ChatChannelId(ChatChannelKind.Party, partyKey);
        }

        /// <summary>Reserved: the channel between the local player and one other, keyed by their id.</summary>
        public static ChatChannelId Whisper(string playerKey) {
            return new ChatChannelId(ChatChannelKind.Whisper, playerKey);
        }

        /// <summary>Reserved: the single server-wide channel.</summary>
        public static ChatChannelId Global() {
            return new ChatChannelId(ChatChannelKind.Global, string.Empty);
        }

        /// <summary>The single server-notice channel.</summary>
        public static ChatChannelId System() {
            return new ChatChannelId(ChatChannelKind.System, string.Empty);
        }

        /// <summary>Writes the id as a kind byte followed by the key.</summary>
        public void Serialize(ref NetWriter writer) {
            string key = Key;

            if (key.Length > MaxKeyLength) {
                throw new NetProtocolException("Chat channel key of " + key.Length.ToString()
                    + " characters exceeds the cap of " + MaxKeyLength.ToString() + ".");
            }

            writer.WriteByte((byte)_kind);
            writer.WriteString(key);
        }

        /// <summary>Reads an id written by <see cref="Serialize"/>.</summary>
        public static ChatChannelId Deserialize(ref NetReader reader) {
            ChatChannelKind kind = (ChatChannelKind)reader.ReadByte();
            string key = reader.ReadString();

            if (key != null && key.Length > MaxKeyLength) {
                throw new NetProtocolException("Chat channel key declared " + key.Length.ToString()
                    + " characters, which exceeds the cap of " + MaxKeyLength.ToString() + ".");
            }

            return new ChatChannelId(kind, key);
        }

        /// <inheritdoc />
        public bool Equals(ChatChannelId other) {
            return _kind == other._kind && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public override bool Equals(object obj) {
            return obj is ChatChannelId other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() {
            return ((int)_kind * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Key);
        }

        /// <summary>Diagnostic form, for example <c>room:AB12CD</c>.</summary>
        public override string ToString() {
            return _kind.ToString().ToLowerInvariant() + ":" + Key;
        }

        /// <summary>Value equality.</summary>
        public static bool operator ==(ChatChannelId left, ChatChannelId right) {
            return left.Equals(right);
        }

        /// <summary>Value inequality.</summary>
        public static bool operator !=(ChatChannelId left, ChatChannelId right) {
            return !left.Equals(right);
        }
    }
}
