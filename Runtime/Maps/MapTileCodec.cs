using System;
using System.IO;
using System.IO.Compression;

namespace AlpineLib.Maps {
    /// <summary>
    /// Tile file format: magic <c>AMT1</c>, little-endian int32 side, then the raw RGBA8 texels
    /// (bottom row first) Deflate-compressed. Pure and thread-safe, meant for worker threads.
    /// </summary>
    public static class MapTileCodec {
        private static readonly byte[] Magic = { (byte)'A', (byte)'M', (byte)'T', (byte)'1' };
        private const int HeaderLength = 8;
        private const int MaxSide = 8192;

        /// <summary>Encodes a <paramref name="side"/>² RGBA8 tile.</summary>
        public static byte[] Encode(int side, byte[] rgba) {
            if (side <= 0 || side > MaxSide) throw new ArgumentOutOfRangeException(nameof(side), side, "Out of range.");
            if (rgba == null || rgba.Length != side * side * 4) throw new ArgumentException("Texel data does not match the side.", nameof(rgba));

            using (var output = new MemoryStream()) {
                output.Write(Magic, 0, Magic.Length);
                output.Write(BitConverter.GetBytes(side), 0, 4);
                using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, true)) {
                    deflate.Write(rgba, 0, rgba.Length);
                }

                return output.ToArray();
            }
        }

        /// <summary>Decodes a tile of side <paramref name="expectedSide"/>; false on a bad magic, side, length or stream.</summary>
        public static bool TryDecode(byte[] data, int expectedSide, out byte[] rgba) {
            rgba = null;
            if (!HasHeader(data, expectedSide)) return false;

            var texels = new byte[expectedSide * expectedSide * 4];
            try {
                using (var input = new MemoryStream(data, HeaderLength, data.Length - HeaderLength))
                using (var deflate = new DeflateStream(input, CompressionMode.Decompress)) {
                    if (!ReadExactly(deflate, texels) || deflate.ReadByte() != -1) return false;
                }
            } catch (InvalidDataException) {
                return false;
            }

            rgba = texels;
            return true;
        }

        private static bool HasHeader(byte[] data, int expectedSide) {
            if (data == null || data.Length < HeaderLength) return false;

            for (int index = 0; index < Magic.Length; index++) {
                if (data[index] != Magic[index]) return false;
            }

            return BitConverter.ToInt32(data, Magic.Length) == expectedSide;
        }

        private static bool ReadExactly(Stream stream, byte[] buffer) {
            int filled = 0;
            while (filled < buffer.Length) {
                int read = stream.Read(buffer, filled, buffer.Length - filled);
                if (read <= 0) return false;

                filled += read;
            }

            return true;
        }
    }
}
