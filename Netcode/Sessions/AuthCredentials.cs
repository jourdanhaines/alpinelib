using System;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// The proof a client offers for the identity it claims.
    /// </summary>
    /// <remarks>
    /// The token stays opaque to everything except the validator that understands the matching
    /// <see cref="AuthMethod"/>: anonymous auth carries no bytes, and Steam auth will carry a session
    /// ticket. That opacity is the whole point of the seam — adding a provider must not touch the
    /// handshake.
    /// </remarks>
    public sealed class AuthCredentials {
        private const int MaxTokenLength = 4096;

        /// <summary>Creates empty anonymous credentials.</summary>
        public AuthCredentials() {
            Method = AuthMethod.Anonymous;
            Token = Array.Empty<byte>();
        }

        /// <summary>Creates credentials for a given method.</summary>
        public AuthCredentials(AuthMethod method, byte[] token) {
            Method = method;
            Token = token ?? Array.Empty<byte>();
        }

        /// <summary>Which validator is expected to understand <see cref="Token"/>.</summary>
        public AuthMethod Method { get; set; }

        /// <summary>Opaque proof bytes. Empty for anonymous auth.</summary>
        public byte[] Token { get; set; }

        /// <summary>Credentials that prove nothing, for the anonymous path.</summary>
        public static AuthCredentials Anonymous() {
            return new AuthCredentials(AuthMethod.Anonymous, Array.Empty<byte>());
        }

        /// <summary>Writes the credentials to the wire.</summary>
        public void Serialize(ref NetWriter writer) {
            writer.WriteByte((byte)Method);

            byte[] token = Token ?? Array.Empty<byte>();

            if (token.Length > MaxTokenLength) {
                throw new NetProtocolException("Auth token of " + token.Length.ToString()
                    + " bytes exceeds the cap of " + MaxTokenLength.ToString() + ".");
            }

            writer.WriteBytes(token);
        }

        /// <summary>Reads credentials written by <see cref="Serialize"/>.</summary>
        public void Deserialize(ref NetReader reader) {
            Method = (AuthMethod)reader.ReadByte();

            byte[] token = reader.ReadBytes();

            if (token.Length > MaxTokenLength) {
                throw new NetProtocolException("Auth token declared " + token.Length.ToString()
                    + " bytes, which exceeds the cap of " + MaxTokenLength.ToString() + ".");
            }

            Token = token;
        }
    }
}
