using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// An authenticated but unattached connection asking the front desk to mint a session.
    /// </summary>
    /// <remarks>
    /// The profile id is optional — an empty value means "use the server's default profile", which is
    /// the normal case for a player hosting an igloo. Naming one is the seam for a server that offers
    /// several rule sets. The answer is <see cref="SessionCreated"/> followed by
    /// <see cref="JoinAccepted"/>.
    /// </remarks>
    public struct CreateSessionRequest : INetMessage {
        /// <summary>Longest profile id accepted on the wire.</summary>
        public const int MaxProfileIdLength = 64;

        /// <summary>Creates a request for a session under the named profile.</summary>
        public CreateSessionRequest(string profileId) {
            ProfileId = profileId ?? string.Empty;
        }

        /// <summary>Which session profile to run by, or empty for the server default.</summary>
        public string ProfileId { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            string profileId = ProfileId ?? string.Empty;

            if (profileId.Length > MaxProfileIdLength) {
                throw new NetProtocolException("CreateSessionRequest profile id of "
                    + profileId.Length.ToString() + " characters exceeds the cap of "
                    + MaxProfileIdLength.ToString() + ".");
            }

            writer.WriteString(profileId);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            ProfileId = reader.ReadString();

            if (ProfileId.Length > MaxProfileIdLength) {
                throw new NetProtocolException("CreateSessionRequest declared a profile id of "
                    + ProfileId.Length.ToString() + " characters, which exceeds the cap of "
                    + MaxProfileIdLength.ToString() + ".");
            }
        }
    }
}
