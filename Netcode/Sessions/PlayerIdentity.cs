using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// The one identity type in the stack: who a player claims to be, plus how they intend to prove it.
    /// </summary>
    /// <remarks>
    /// The client loads this from its identity store and sends it in <c>AuthRequest</c>; the server
    /// hands the same object to <see cref="IAuthValidator"/>, which may return a corrected copy (a
    /// Steam validator resolves the real display name from the ticket, for example).
    /// </remarks>
    public sealed class PlayerIdentity {
        /// <summary>Longest display name accepted on the wire.</summary>
        public const int MaxDisplayNameLength = 32;

        /// <summary>Creates an empty identity, ready to be deserialised into.</summary>
        public PlayerIdentity() {
            PlayerId = PlayerId.None;
            DisplayName = string.Empty;
            Method = AuthMethod.Anonymous;
        }

        /// <summary>Creates a fully populated identity.</summary>
        public PlayerIdentity(PlayerId playerId, string displayName, AuthMethod method) {
            PlayerId = playerId;
            DisplayName = Sanitize(displayName);
            Method = method;
        }

        /// <summary>Stable identity, reused across reconnects.</summary>
        public PlayerId PlayerId { get; set; }

        /// <summary>Name shown to other players. Trimmed and length-capped on assignment.</summary>
        public string DisplayName { get; set; }

        /// <summary>How this identity is proven.</summary>
        public AuthMethod Method { get; set; }

        /// <summary>Trims, collapses empties to a fallback, and caps length.</summary>
        public static string Sanitize(string displayName) {
            if (string.IsNullOrWhiteSpace(displayName)) {
                return string.Empty;
            }

            string trimmed = displayName.Trim();

            if (trimmed.Length <= MaxDisplayNameLength) {
                return trimmed;
            }

            return trimmed.Substring(0, MaxDisplayNameLength);
        }

        /// <summary>Writes the identity to the wire.</summary>
        public void Serialize(ref NetWriter writer) {
            PlayerId.Serialize(ref writer);
            writer.WriteString(DisplayName ?? string.Empty);
            writer.WriteByte((byte)Method);
        }

        /// <summary>Reads an identity written by <see cref="Serialize"/>.</summary>
        public void Deserialize(ref NetReader reader) {
            PlayerId = PlayerId.Deserialize(ref reader);
            DisplayName = Sanitize(reader.ReadString());
            Method = (AuthMethod)reader.ReadByte();
        }

        /// <inheritdoc />
        public override string ToString() {
            return DisplayName + " (" + PlayerId.ToString() + ")";
        }
    }
}
