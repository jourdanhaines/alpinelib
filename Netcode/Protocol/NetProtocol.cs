using System;
using System.Globalization;

namespace AlpineLib.Netcode.Protocol {
    /// <summary>
    /// The single version constant for the whole protocol, plus the connect key derived from it.
    ///
    /// The connect key is the ONLY transport-level handshake: LiteNetLib rejects a connection whose key
    /// does not match byte for byte, so a client built against a different protocol version is turned
    /// away before it can send a single malformed message. Everything else — identity, session, config —
    /// happens above the transport in the auth exchange.
    /// </summary>
    public static class NetProtocol {
        /// <summary>
        /// Bump this whenever the wire layout of any message changes in a way old builds cannot read.
        /// There is exactly one version number in the system; nothing else carries its own.
        /// </summary>
        public const ushort Version = 2;

        private const uint Fnv1aOffsetBasis = 2166136261u;
        private const uint Fnv1aPrime = 16777619u;

        /// <summary>
        /// Builds the LiteNetLib connect key: <c>"{gameName}/{fnv1a-hex}"</c> where the hash covers the
        /// protocol version. Hashing rather than printing the version keeps the key opaque and fixed
        /// width, and both ends compute it from the same constant so they agree by construction.
        /// </summary>
        public static string BuildConnectKey(string gameName) {
            if (string.IsNullOrEmpty(gameName)) {
                throw new ArgumentException("Connect key needs a non-empty game name.", nameof(gameName));
            }

            return gameName + "/" + VersionHash.ToString("x8", CultureInfo.InvariantCulture);
        }

        /// <summary>FNV-1a hash of <see cref="Version"/>, exposed for diagnostics and tests.</summary>
        public static uint VersionHash => Fnv1a(Version);

        /// <summary>FNV-1a over the two little-endian bytes of a ushort.</summary>
        public static uint Fnv1a(ushort value) {
            uint hash = Fnv1aOffsetBasis;
            hash = MixByte(hash, (byte)(value & 0xFF));
            hash = MixByte(hash, (byte)((value >> 8) & 0xFF));
            return hash;
        }

        private static uint MixByte(uint hash, byte value) {
            return (hash ^ value) * Fnv1aPrime;
        }
    }
}
