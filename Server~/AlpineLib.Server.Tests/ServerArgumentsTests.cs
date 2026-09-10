using System;
using AlpineLib.Server.Hosting;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Pins the command line a launcher writes and an operator types.
    /// </summary>
    /// <remarks>
    /// The failure worth preventing is quiet: a flag that parses to the wrong thing, or a typo that is
    /// ignored, produces a server that runs happily on a port nobody is dialling. So the assertions are
    /// about the mapping flag by flag, and about refusing anything that was not meant.
    /// </remarks>
    public sealed class ServerArgumentsTests {
        [Fact]
        public void AnEmptyCommandLineDecidesNothing() {
            ServerArguments arguments = ServerArguments.Parse(Array.Empty<string>());

            Assert.Null(arguments.Port);
            Assert.Null(arguments.ConfigDirectory);
            Assert.Null(arguments.IdleExitSeconds);
            Assert.Null(arguments.MaxSessions);
        }

        [Fact]
        public void ANullCommandLineIsTheSameAsAnEmptyOne() {
            ServerArguments arguments = ServerArguments.Parse(null);

            Assert.Null(arguments.Port);
            Assert.Null(arguments.ConfigDirectory);
        }

        [Fact]
        public void EveryFlagReachesTheValueItNames() {
            ServerArguments arguments = ServerArguments.Parse(new[] {
                "--port", "9051",
                "--config", "/srv/alpine/config",
                "--idle-exit-seconds", "30",
                "--max-sessions", "4"
            });

            Assert.Equal(9051, arguments.Port);
            Assert.Equal("/srv/alpine/config", arguments.ConfigDirectory);
            Assert.Equal(30, arguments.IdleExitSeconds);
            Assert.Equal(4, arguments.MaxSessions);
        }

        [Fact]
        public void PortZeroIsAnInstructionRatherThanAnAbsentFlag() {
            // The whole point of the readiness line: the launcher asks for an ephemeral port and is told
            // which one it got. "Absent" and "zero" must not collapse into each other.
            ServerArguments arguments = ServerArguments.Parse(new[] { "--port", "0" });

            Assert.True(arguments.Port.HasValue);
            Assert.Equal(0, arguments.Port.Value);
        }

        [Fact]
        public void IdleExitZeroTurnsTheTimerOffRatherThanLeavingItUnsaid() {
            ServerArguments arguments = ServerArguments.Parse(new[] { "--idle-exit-seconds", "0" });

            Assert.True(arguments.IdleExitSeconds.HasValue);
            Assert.Equal(0, arguments.IdleExitSeconds.Value);
        }

        [Fact]
        public void TheLastMentionOfAFlagWins() {
            ServerArguments arguments = ServerArguments.Parse(new[] { "--port", "9051", "--port", "9052" });

            Assert.Equal(9052, arguments.Port);
        }

        [Theory]
        [InlineData("-p")]
        [InlineData("--Port")]
        [InlineData("--prot")]
        [InlineData("9051")]
        public void AnArgumentTheServerDoesNotKnowIsRefusedRatherThanIgnored(string flag) {
            ArgumentException error = Assert.Throws<ArgumentException>(() => ServerArguments.Parse(new[] { flag, "9051" }));

            Assert.Contains(ServerArguments.Usage, error.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("--port")]
        [InlineData("--config")]
        [InlineData("--idle-exit-seconds")]
        [InlineData("--max-sessions")]
        public void AFlagWithNothingAfterItIsRefused(string flag) {
            Assert.Throws<ArgumentException>(() => ServerArguments.Parse(new[] { flag }));
        }

        [Theory]
        [InlineData("-1")]
        [InlineData("65536")]
        [InlineData("nine")]
        public void APortOutsideTheUdpRangeIsRefused(string value) {
            Assert.Throws<ArgumentException>(() => ServerArguments.Parse(new[] { "--port", value }));
        }

        [Fact]
        public void ANegativeIdleWindowIsRefused() {
            Assert.Throws<ArgumentException>(() => ServerArguments.Parse(new[] { "--idle-exit-seconds", "-5" }));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        public void ASessionCapBelowOneIsRefused(string value) {
            // A cap of zero would be a server that binds a socket and refuses everybody, which is never
            // what somebody typing this meant.
            Assert.Throws<ArgumentException>(() => ServerArguments.Parse(new[] { "--max-sessions", value }));
        }

        [Fact]
        public void AnEmptyConfigDirectoryIsRefused() {
            Assert.Throws<ArgumentException>(() => ServerArguments.Parse(new[] { "--config", "   " }));
        }
    }
}
