using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Transport;
using AlpineLib.Server.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The exit code a server process ends with is what its launcher decides on: a taken port is told
    /// apart from a clean stop, so the launcher can pick another port or give up.
    /// </summary>
    public sealed class ServerExitCodeTests {
        private const string MinimalConfig = @"{
  ""net"": { ""gameProtocolName"": ""alpine-exit-code"", ""port"": 9050 },
  ""session"": { ""lobby"": { ""lobbySceneName"": ""Game"" } }
}";

        private const int RunTimeoutMs = 20_000;

        [Fact]
        public async Task ATakenPortEndsTheProcessWithThePortInUseCode() {
            using TempConfigDirectory config = TempConfigDirectory.Create(MinimalConfig);
            using var holder = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            int takenPort = ((IPEndPoint)holder.Client.LocalEndPoint).Port;

            using IHost host = AlpineServerHost.Build(
                new[] { "--port", takenPort.ToString(), "--config", config.Path },
                builder => builder.ConfigureLogging = QuietLogging);

            using var timeout = new CancellationTokenSource(RunTimeoutMs);
            int exitCode = await AlpineServerHost.RunToExitCodeAsync(host, timeout.Token);

            Assert.False(timeout.IsCancellationRequested, "The server never stopped on the taken port.");
            Assert.Equal(ServerExitCodes.PortInUse, exitCode);
        }

        [Fact]
        public async Task ACleanStopEndsWithTheCleanCode() {
            using TempConfigDirectory config = TempConfigDirectory.Create(MinimalConfig);

            using IHost host = AlpineServerHost.Build(
                new[] { "--port", "0", "--config", config.Path, "--idle-exit-seconds", "1" },
                builder => builder.ConfigureLogging = QuietLogging);

            using var timeout = new CancellationTokenSource(RunTimeoutMs);
            int exitCode = await AlpineServerHost.RunToExitCodeAsync(host, timeout.Token);

            Assert.False(timeout.IsCancellationRequested, "The idle server never stopped.");
            Assert.Equal(ServerExitCodes.Clean, exitCode);
        }

        [Fact]
        public void ABindFailureNamesThePortInTheLauncherReadableLine() {
            var error = new TransportBindException(9050);

            Assert.Equal(9050, error.Port);
            Assert.StartsWith(TransportBindException.MessagePrefix, error.Message, StringComparison.Ordinal);
            Assert.Contains("9050", error.Message, StringComparison.Ordinal);
            Assert.IsAssignableFrom<InvalidOperationException>(error);
        }

        private static void QuietLogging(ILoggingBuilder logging) {
            logging.ClearProviders();
        }
    }
}
