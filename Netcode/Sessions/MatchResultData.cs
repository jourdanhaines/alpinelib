using System;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// The outcome of one match, broadcast with <c>MatchEnd</c>.
    /// </summary>
    /// <remarks>
    /// The session layer deliberately knows nothing about scoring. <see cref="Payload"/> is an opaque
    /// blob the game writes and reads with its own codec, which keeps minigame-specific result shapes
    /// out of the shared session protocol.
    /// </remarks>
    public sealed class MatchResultData {
        private const int MaxPayloadLength = 8192;

        /// <summary>Creates an empty result, ready to be deserialised into.</summary>
        public MatchResultData() {
            MatchId = string.Empty;
            Payload = Array.Empty<byte>();
        }

        /// <summary>Wire id of the match that ended.</summary>
        public string MatchId { get; set; }

        /// <summary>Which run of the match this result belongs to.</summary>
        public int MatchSequence { get; set; }

        /// <summary>Game-defined result blob. Never interpreted by the session layer.</summary>
        public byte[] Payload { get; set; }

        /// <summary>Writes the result to the wire.</summary>
        public void Serialize(ref NetWriter writer) {
            writer.WriteString(MatchId ?? string.Empty);
            writer.WriteInt(MatchSequence);

            byte[] payload = Payload ?? Array.Empty<byte>();

            if (payload.Length > MaxPayloadLength) {
                throw new NetProtocolException("MatchResultData payload of " + payload.Length.ToString()
                    + " bytes exceeds the cap of " + MaxPayloadLength.ToString() + ".");
            }

            writer.WriteBytes(payload);
        }

        /// <summary>Reads a result written by <see cref="Serialize"/>.</summary>
        public void Deserialize(ref NetReader reader) {
            MatchId = reader.ReadString();
            MatchSequence = reader.ReadInt();

            byte[] payload = reader.ReadBytes();

            if (payload.Length > MaxPayloadLength) {
                throw new NetProtocolException("MatchResultData declared a payload of "
                    + payload.Length.ToString() + " bytes, which exceeds the cap of "
                    + MaxPayloadLength.ToString() + ".");
            }

            Payload = payload;
        }
    }
}
