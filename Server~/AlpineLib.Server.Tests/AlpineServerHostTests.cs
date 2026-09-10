using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlpineLib.Netcode.Collision;
using AlpineLib.Server.GameLoop;
using AlpineLib.Server.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Stands the whole process up the way a game's <c>Main</c> does — command line, exported config,
    /// socket, loop — and asserts on what a launcher can see from outside it.
    /// </summary>
    /// <remarks>
    /// The case that matters is <c>--port 0</c>. A launcher that asks for an ephemeral port has no other
    /// way to learn which one it got, so the readiness line has to carry the port the socket actually
    /// bound and never the zero that was asked for. The wait is on the captured line rather than on
    /// <c>ReadyPort</c>, because the line is what a launcher tails and the port is only the loop's own
    /// record of it.
    /// </remarks>
    public sealed class AlpineServerHostTests {
        private const string MinimalConfig = @"{
  ""net"": { ""gameProtocolName"": ""alpine-host-boot"", ""port"": 9050 },
  ""session"": { ""lobby"": { ""lobbySceneName"": ""Game"" } }
}";

        [Fact]
        public async Task AnEphemeralPortIsAnnouncedAsThePortTheSocketGot() {
            using TempConfigDirectory config = TempConfigDirectory.Create(MinimalConfig);

            using IHost host = AlpineServerHost.Build(
                new[] { "--port", "0", "--config", config.Path, "--idle-exit-seconds", "3" },
                builder => builder.ConfigureLogging = QuietLogging);

            GameLoopService loop = FindLoop(host);
            string announced;

            using (ConsoleCapture capture = ConsoleCapture.Start()) {
                host.Start();
                Assert.True(
                    WaitFor(() => loop.ReadyPort > 0 && capture.Contains(ReadinessLine.Format(loop.ReadyPort)), 15_000),
                    "The server never announced a port.");
                announced = capture.Text;
            }

            Assert.True(loop.ReadyPort > 0);
            Assert.NotEqual(9050, loop.ReadyPort);
            Assert.Contains(ReadinessLine.Format(loop.ReadyPort), announced, StringComparison.Ordinal);

            await host.StopAsync();
        }

        [Fact]
        public async Task AServerNobodyDialsStopsItself() {
            using TempConfigDirectory config = TempConfigDirectory.Create(MinimalConfig);

            using IHost host = AlpineServerHost.Build(
                new[] { "--port", "0", "--config", config.Path, "--idle-exit-seconds", "1" },
                builder => builder.ConfigureLogging = QuietLogging);

            IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            host.Start();

            Assert.True(
                WaitFor(() => lifetime.ApplicationStopping.IsCancellationRequested, 20_000),
                "The idle server never asked to stop.");

            await host.StopAsync();
        }

        [Fact]
        public void TheCommandLinePortWinsOverTheExportedOne() {
            using TempConfigDirectory config = TempConfigDirectory.Create(MinimalConfig);

            using IHost host = AlpineServerHost.Build(
                new[] { "--port", "0", "--config", config.Path },
                builder => builder.ConfigureLogging = QuietLogging);

            ServerRuntimeOptions options = host.Services.GetRequiredService<ServerRuntimeOptions>();

            Assert.Equal(0, options.PortOverride);
            Assert.Equal(Path.Combine(config.Path, "session-config.json"), options.SessionConfigPath);
        }

        [Fact]
        public void AnArgumentTheServerDoesNotKnowStopsItBeforeItBinds() {
            using TempConfigDirectory config = TempConfigDirectory.Create(MinimalConfig);

            Assert.Throws<ArgumentException>(
                () => AlpineServerHost.Build(new[] { "--config", config.Path, "--verbose" }, builder => { }));
        }

        [Fact]
        public void AMissingExportStopsTheProcessRatherThanInventingRules() {
            string missing = Path.Combine(Path.GetTempPath(), "alpine-absent-" + Guid.NewGuid().ToString("N"));

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => AlpineServerHost.Build(new[] { "--config", missing }, builder => { }));

            Assert.Contains("was not found", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void AGameSOwnServicesAreResolvableFromTheHostsContainer() {
            using TempConfigDirectory config = TempConfigDirectory.Create(MinimalConfig);
            RecordingSessionModuleFactory factory = new RecordingSessionModuleFactory();

            using IHost host = AlpineServerHost.Build(
                new[] { "--port", "0", "--config", config.Path },
                builder => {
                    builder.ModuleFactory = factory;
                    builder.Services.AddSingleton(factory);
                    builder.ConfigureLogging = QuietLogging;
                });

            Assert.Same(factory, host.Services.GetRequiredService<RecordingSessionModuleFactory>());
        }

        [Fact]
        public void AMissingGeometryDirectoryIsAWarningRatherThanARefusalToStart() {
            using TempConfigDirectory config = TempConfigDirectory.Create(MinimalConfig);

            using IHost host = AlpineServerHost.Build(
                new[] { "--port", "0", "--config", config.Path },
                builder => builder.ConfigureLogging = QuietLogging);

            Assert.Equal(0, host.Services.GetRequiredService<SceneGeometryLibrary>().Count);
        }

        /// <summary>Silences the console logging so a test run is not buried in startup chatter.</summary>
        private static void QuietLogging(ILoggingBuilder logging) {
            logging.ClearProviders();
        }

        private static GameLoopService FindLoop(IHost host) {
            IEnumerable<IHostedService> hosted = host.Services.GetServices<IHostedService>();
            GameLoopService loop = hosted.OfType<GameLoopService>().FirstOrDefault();

            Assert.NotNull(loop);
            return loop;
        }

        private static bool WaitFor(Func<bool> condition, int timeoutMs) {
            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.ElapsedMilliseconds < timeoutMs) {
                if (condition()) {
                    return true;
                }

                Thread.Sleep(5);
            }

            return condition();
        }
    }
}
