using System;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// Wire form of the opaque blob a host attaches to a session when creating it (a world seed, a rule
    /// variant) and every joiner is handed back verbatim.
    /// </summary>
    /// <remarks>
    /// The library never looks inside: the game owns the layout and any version byte it needs. Null and
    /// empty are the same thing on the wire and both read back as an empty array.
    /// </remarks>
    public static class SessionParamsCodec {
        /// <summary>Largest blob accepted on the wire or by a front desk.</summary>
        public const int MaxLength = 256;

        /// <summary>True when the blob fits under <see cref="MaxLength"/>; null counts as empty.</summary>
        public static bool IsWithinCap(byte[] sessionParams) {
            return sessionParams == null || sessionParams.Length <= MaxLength;
        }

        /// <summary>A private copy of the blob, never null.</summary>
        public static byte[] Copy(byte[] sessionParams) {
            if (sessionParams == null || sessionParams.Length == 0) {
                return Array.Empty<byte>();
            }

            return (byte[])sessionParams.Clone();
        }

        /// <summary>Writes the blob with a length prefix, refusing one over the cap.</summary>
        public static void Write(ref NetWriter writer, byte[] sessionParams, string messageName) {
            if (!IsWithinCap(sessionParams)) {
                throw new NetProtocolException(messageName + " session params of " + sessionParams.Length.ToString()
                    + " bytes exceed the cap of " + MaxLength.ToString() + ".");
            }

            writer.WriteBytes(sessionParams ?? Array.Empty<byte>());
        }

        /// <summary>Reads a blob written by <see cref="Write"/>, refusing one that declares more than the cap.</summary>
        public static byte[] Read(ref NetReader reader, string messageName) {
            ReadOnlySpan<byte> window = reader.ReadBytesSpan();

            if (window.Length > MaxLength) {
                throw new NetProtocolException(messageName + " declared session params of " + window.Length.ToString()
                    + " bytes, which exceeds the cap of " + MaxLength.ToString() + ".");
            }

            return window.Length == 0 ? Array.Empty<byte>() : window.ToArray();
        }
    }
}
