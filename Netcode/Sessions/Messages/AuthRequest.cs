using System;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// The first message above the transport: who the client claims to be, and the proof it offers.
    /// </summary>
    /// <remarks>
    /// The claim is never trusted as sent — the server hands it to <see cref="IAuthValidator"/>, which
    /// may return a corrected identity. <see cref="Token"/> is opaque here on purpose: Anonymous auth
    /// sends nothing, a future Steam validator reads a session ticket out of the same field, and the
    /// session layer stays ignorant of either format.
    /// </remarks>
    public struct AuthRequest : INetMessage {
        /// <summary>Longest proof blob accepted on the wire. A Steam ticket fits comfortably.</summary>
        public const int MaxTokenLength = 2048;

        /// <summary>Creates a request from a resolved identity and its proof.</summary>
        public AuthRequest(PlayerIdentity identity, byte[] token) {
            PlayerIdentity source = identity ?? new PlayerIdentity();
            PlayerId = source.PlayerId;
            DisplayName = PlayerIdentity.Sanitize(source.DisplayName);
            AuthMethod = source.Method;
            AvatarData = source.AvatarData;
            Token = token ?? Array.Empty<byte>();
        }

        /// <summary>Stable identity the client claims, reused across reconnects to reclaim a rejoin slot.</summary>
        public PlayerId PlayerId { get; set; }

        /// <summary>Name the client would like other members to see.</summary>
        public string DisplayName { get; set; }

        /// <summary>How the claim is meant to be proven.</summary>
        public AuthMethod AuthMethod { get; set; }

        /// <summary>Game-defined appearance code carried with the claim. Zero when unset.</summary>
        public ushort AvatarData { get; set; }

        /// <summary>Proof blob interpreted only by the matching validator. Empty under Anonymous auth.</summary>
        public byte[] Token { get; set; }

        /// <summary>Rebuilds the claimed identity, for handing to a validator.</summary>
        public PlayerIdentity ToIdentity() {
            return new PlayerIdentity(PlayerId, DisplayName, AuthMethod) { AvatarData = AvatarData };
        }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            byte[] token = Token ?? Array.Empty<byte>();

            if (token.Length > MaxTokenLength) {
                throw new NetProtocolException("AuthRequest token of " + token.Length.ToString()
                    + " bytes exceeds the cap of " + MaxTokenLength.ToString() + ".");
            }

            PlayerId.Serialize(ref writer);
            writer.WriteString(PlayerIdentity.Sanitize(DisplayName));
            writer.WriteByte((byte)AuthMethod);
            writer.WriteUShort(AvatarData);
            writer.WriteBytes(token);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            PlayerId = PlayerId.Deserialize(ref reader);
            DisplayName = PlayerIdentity.Sanitize(reader.ReadString());
            AuthMethod = (AuthMethod)reader.ReadByte();
            AvatarData = reader.ReadUShort();

            byte[] token = reader.ReadBytes();

            if (token.Length > MaxTokenLength) {
                throw new NetProtocolException("AuthRequest declared a token of " + token.Length.ToString()
                    + " bytes, which exceeds the cap of " + MaxTokenLength.ToString() + ".");
            }

            Token = token;
        }
    }
}
