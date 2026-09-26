using System;
using System.Text;
using AlpineLib.Procedural;
using Xunit;

namespace AlpineLib.Server.Tests.Procedural {
    public sealed class StableHashTests {
        [Fact]
        public void EmptyStringIsOffsetBasis() {
            Assert.Equal(0xCBF29CE484222325UL, StableHash.Fnv1a64(""));
            Assert.Equal(StableHash.Fnv1a64OffsetBasis, StableHash.Fnv1a64(ReadOnlySpan<byte>.Empty));
        }

        [Fact]
        public void KnownVectors() {
            Assert.Equal(0xAF63DC4C8601EC8CUL, StableHash.Fnv1a64("a"));
            Assert.Equal(0x85944171F73967E8UL, StableHash.Fnv1a64("foobar"));
        }

        [Fact]
        public void StringHashesUtf8Bytes() {
            const string text = "Bahnhof-Straße";

            Assert.Equal(StableHash.Fnv1a64(Encoding.UTF8.GetBytes(text)), StableHash.Fnv1a64(text));
        }

        [Fact]
        public void AppendContinuesTheStream() {
            ulong split = StableHash.Fnv1a64Append(StableHash.Fnv1a64("foo"), "bar");

            Assert.Equal(StableHash.Fnv1a64("foobar"), split);
        }

        [Fact]
        public void AppendUIntIsLittleEndian() {
            ulong viaUInt = StableHash.Fnv1a64Append(StableHash.Fnv1a64OffsetBasis, 0x04030201u);

            Assert.Equal(StableHash.Fnv1a64(new byte[] { 1, 2, 3, 4 }), viaUInt);
        }

        [Fact]
        public void MixIsOrderSensitiveAndPinned() {
            Assert.NotEqual(StableHash.Mix(1, 2), StableHash.Mix(2, 1));
            Assert.NotEqual(0UL, StableHash.Mix(0, 0));
            Assert.Equal(StableHash.Mix(123, 456), StableHash.Mix(123, 456));
        }

        [Fact]
        public void MixAvalanchesSingleBitChanges() {
            ulong baseline = StableHash.Mix(0x1234, 0x5678);
            ulong flipped = StableHash.Mix(0x1235, 0x5678);

            int differing = CountBits(baseline ^ flipped);

            Assert.InRange(differing, 16, 48);
        }

        private static int CountBits(ulong value) {
            int count = 0;
            while (value != 0) {
                value &= value - 1;
                count++;
            }
            return count;
        }
    }
}
