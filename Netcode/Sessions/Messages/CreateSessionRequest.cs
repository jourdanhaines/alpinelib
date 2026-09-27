using System;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// An authenticated but unattached connection asking the front desk to mint a session.
    /// </summary>
    /// <remarks>
    /// The profile id is optional — an empty value means "use the server's default profile", which is
    /// the normal case for a player hosting a lobby. Naming one is the seam for a server that offers
    /// several rule sets. The params blob is the game's own (a world seed, say); the session keeps it and
    /// echoes it to every joiner in <see cref="JoinAccepted.SessionParams"/>. The answer is
    /// <see cref="SessionCreated"/> followed by <see cref="JoinAccepted"/>.
    /// </remarks>
    public struct CreateSessionRequest : INetMessage {
        /// <summary>Longest profile id accepted on the wire.</summary>
        public const int MaxProfileIdLength = 64;

        /// <summary>Longest params blob accepted on the wire.</summary>
        public const int MaxParamsLength = SessionParamsCodec.MaxLength;

        /// <summary>Creates a request for a session under the named profile.</summary>
        public CreateSessionRequest(string profileId) : this(profileId, null) {
        }

        /// <summary>Creates a request for a session under the named profile, carrying the game's params.</summary>
        public CreateSessionRequest(string profileId, byte[] sessionParams) {
            ProfileId = profileId ?? string.Empty;
            Params = sessionParams ?? Array.Empty<byte>();
        }

        /// <summary>Which session profile to run by, or empty for the server default.</summary>
        public string ProfileId { get; set; }

        /// <summary>Opaque game params for the new session; empty when the game sends none.</summary>
        public byte[] Params { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            string profileId = ProfileId ?? string.Empty;

            if (profileId.Length > MaxProfileIdLength) {
                throw new NetProtocolException("CreateSessionRequest profile id of "
                    + profileId.Length.ToString() + " characters exceeds the cap of "
                    + MaxProfileIdLength.ToString() + ".");
            }

            writer.WriteString(profileId);
            SessionParamsCodec.Write(ref writer, Params, nameof(CreateSessionRequest));
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            ProfileId = reader.ReadString();

            if (ProfileId.Length > MaxProfileIdLength) {
                throw new NetProtocolException("CreateSessionRequest declared a profile id of "
                    + ProfileId.Length.ToString() + " characters, which exceeds the cap of "
                    + MaxProfileIdLength.ToString() + ".");
            }

            Params = SessionParamsCodec.Read(ref reader, nameof(CreateSessionRequest));
        }
    }
}
