using System;
using System.Text;

namespace AlpineLib.Procedural {
    /// <summary>
    /// Hashes that give the same answer on every runtime, process and machine, for seeding and for
    /// fingerprinting generated output.
    /// </summary>
    /// <remarks>
    /// <c>string.GetHashCode</c> is randomised per process on .NET Core and differs between Mono and
    /// CoreCLR, so nothing procedural may ever depend on it; everything goes through FNV-1a over UTF-8.
    /// </remarks>
    public static class StableHash {
        /// <summary>The FNV-1a 64-bit offset basis: the hash of zero bytes.</summary>
        public const ulong Fnv1a64OffsetBasis = 0xCBF29CE484222325UL;

        private const ulong Fnv1a64Prime = 0x100000001B3UL;
        private const ulong MixGamma = 0x9E3779B97F4A7C15UL;

        /// <summary>FNV-1a 64 of the string's UTF-8 bytes.</summary>
        public static ulong Fnv1a64(string text) {
            return Fnv1a64Append(Fnv1a64OffsetBasis, text);
        }

        /// <summary>FNV-1a 64 of raw bytes.</summary>
        public static ulong Fnv1a64(ReadOnlySpan<byte> bytes) {
            return Fnv1a64Append(Fnv1a64OffsetBasis, bytes);
        }

        /// <summary>Continues an FNV-1a 64 hash over more bytes, so several fields hash as one stream.</summary>
        public static ulong Fnv1a64Append(ulong hash, ReadOnlySpan<byte> bytes) {
            for (int index = 0; index < bytes.Length; index++) {
                hash ^= bytes[index];
                hash *= Fnv1a64Prime;
            }
            return hash;
        }

        /// <summary>Continues an FNV-1a 64 hash over a string's UTF-8 bytes (no length prefix).</summary>
        public static ulong Fnv1a64Append(ulong hash, string text) {
            if (text == null) {
                throw new ArgumentNullException(nameof(text));
            }
            return Fnv1a64Append(hash, Encoding.UTF8.GetBytes(text));
        }

        /// <summary>Continues an FNV-1a 64 hash over a 32-bit value, little-endian.</summary>
        public static ulong Fnv1a64Append(ulong hash, uint value) {
            for (int shift = 0; shift < 32; shift += 8) {
                hash ^= (value >> shift) & 0xFFu;
                hash *= Fnv1a64Prime;
            }
            return hash;
        }

        /// <summary>
        /// Combines two 64-bit values into one well-avalanched value. Order matters:
        /// <c>Mix(a, b) != Mix(b, a)</c> in general.
        /// </summary>
        public static ulong Mix(ulong a, ulong b) {
            ulong combined = a ^ (RotateLeft(b, 27) + MixGamma);
            return Finalize(combined);
        }

        private static ulong RotateLeft(ulong value, int bits) {
            return (value << bits) | (value >> (64 - bits));
        }

        // MurmurHash3 fmix64: every input bit flips each output bit with probability close to one half.
        private static ulong Finalize(ulong value) {
            value ^= value >> 33;
            value *= 0xFF51AFD7ED558CCDUL;
            value ^= value >> 33;
            value *= 0xC4CEB9FE1A85EC53UL;
            value ^= value >> 33;
            return value;
        }
    }
}
