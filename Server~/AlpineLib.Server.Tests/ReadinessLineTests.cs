using System;
using AlpineLib.Server.Hosting;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Pins the readiness-line format. The Unity launcher reimplements this parser against the same
    /// contract, so a silent change here would show up only as a launcher that waits forever.
    /// </summary>
    public sealed class ReadinessLineTests {
        [Theory]
        [InlineData(1)]
        [InlineData(7777)]
        [InlineData(65535)]
        public void FormatRoundTripsThroughTryParse(int port) {
            string line = ReadinessLine.Format(port);

            Assert.True(ReadinessLine.TryParse(line, out int parsed));
            Assert.Equal(port, parsed);
        }

        [Fact]
        public void FormatWritesThePinnedShape() {
            Assert.Equal("[ready] port=7777", ReadinessLine.Format(7777));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(65536)]
        public void FormatRejectsPortsOutsideTheUdpRange(int port) {
            Assert.Throws<ArgumentOutOfRangeException>(() => ReadinessLine.Format(port));
        }

        [Fact]
        public void TryParseToleratesSurroundingWhitespace() {
            Assert.True(ReadinessLine.TryParse("  [ready]  port=42  ", out int parsed));
            Assert.Equal(42, parsed);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("info: listening")]
        [InlineData("[ready]")]
        [InlineData("[ready] 7777")]
        [InlineData("[ready] port=")]
        [InlineData("[ready] port=abc")]
        [InlineData("[ready] port=-1")]
        [InlineData("[ready] port=0")]
        [InlineData("[ready] port=65536")]
        [InlineData("not [ready] port=7777")]
        public void TryParseRejectsAnythingElse(string line) {
            Assert.False(ReadinessLine.TryParse(line, out int parsed));
            Assert.Equal(0, parsed);
        }
    }
}
