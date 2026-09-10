using AlpineLib.Server.Configuration;
using AlpineLib.Server.Hosting;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// Covers the timer that lets a server nobody dialled stop itself, and the loop's use of it.
    /// </summary>
    /// <remarks>
    /// The window is fed by hand rather than by wall clock wherever it can be, because a test that slept
    /// through a real thirty-second window would be a test nobody runs. The one case that has to be real
    /// is the loop actually asking the host to stop, and that runs with a window measured in tenths of a
    /// second.
    /// </remarks>
    public sealed class GameLoopIdleExitTests {
        private const float StepSeconds = 1f / 30f;

        [Fact]
        public void AWindowOfZeroMeansTheServerStaysUpForever() {
            IdleShutdownTimer timer = new IdleShutdownTimer(0.0);

            Assert.False(timer.IsEnabled);
            Assert.False(timer.Observe(0, 3600.0));
        }

        [Fact]
        public void ANegativeWindowIsTheSameAsNoWindow() {
            IdleShutdownTimer timer = new IdleShutdownTimer(-5.0);

            Assert.False(timer.IsEnabled);
            Assert.False(timer.Observe(0, 3600.0));
        }

        [Fact]
        public void TheWindowRunsFromStartupRatherThanFromTheFirstDeparture() {
            // A process that was launched and never dialled — a launcher that crashed, a player who backed
            // out of the loading screen — is exactly the case this exists for.
            IdleShutdownTimer timer = new IdleShutdownTimer(1.0);

            Assert.False(timer.Observe(0, 0.5));
            Assert.True(timer.Observe(0, 0.5));
        }

        [Fact]
        public void OneConnectionStartsTheWindowOver() {
            IdleShutdownTimer timer = new IdleShutdownTimer(1.0);

            timer.Observe(0, 0.9);
            timer.Observe(1, StepSeconds);

            Assert.Equal(0.0, timer.IdleSeconds);
            Assert.False(timer.Observe(0, 0.9));
        }

        [Fact]
        public void TheWindowRestartsWhenTheLastPeerLeaves() {
            IdleShutdownTimer timer = new IdleShutdownTimer(1.0);

            timer.Observe(2, 5.0);
            Assert.False(timer.Observe(0, 0.9));
            Assert.True(timer.Observe(0, 0.2));
        }

        [Fact]
        public void ItKeepsAskingOnceTheWindowHasRunOut() {
            // The loop stops on the first true, but a caller that kept stepping must not see the answer
            // flip back to "carry on".
            IdleShutdownTimer timer = new IdleShutdownTimer(0.5);

            Assert.True(timer.Observe(0, 0.5));
            Assert.True(timer.Observe(0, StepSeconds));
        }

        [Fact]
        public void ResetIsWhatAFreshConnectionDoes() {
            IdleShutdownTimer timer = new IdleShutdownTimer(1.0);

            timer.Observe(0, 0.9);
            timer.Reset();

            Assert.Equal(0.0, timer.IdleSeconds);
            Assert.False(timer.Observe(0, 0.9));
        }

        [Fact]
        public void ABackwardsSliceCannotWindTheWindowBack() {
            IdleShutdownTimer timer = new IdleShutdownTimer(1.0);

            timer.Observe(0, 0.9);
            timer.Observe(0, -10.0);

            Assert.Equal(0.9, timer.IdleSeconds, 3);
        }

        [Fact]
        public void ALoopNobodyDialsAsksTheHostToStop() {
            ServerRuntimeOptions options = new ServerRuntimeOptions { IdleExitSeconds = 1 };

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(
                DedicatedServerHarness.BuildConfig(), options, null);

            Assert.True(
                harness.PumpUntil(() => harness.WasStopRequested, 15_000),
                "The idle server never asked the host to stop.");
        }

        [Fact]
        public void AConnectedClientKeepsTheServerAlivePastTheWindow() {
            ServerRuntimeOptions options = new ServerRuntimeOptions { IdleExitSeconds = 1 };

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(
                DedicatedServerHarness.BuildConfig(), options, null);
            HarnessClient player = harness.AddClient("Driver");

            Assert.True(harness.Complete(player.Session.ConnectAsync(harness.Endpoint)).IsSuccess);
            harness.PumpUntil(() => false, 2_000);

            Assert.False(harness.WasStopRequested);
        }

        [Fact]
        public void ALoopWithNoWindowIsNeverAskedToStop() {
            using DedicatedServerHarness harness = DedicatedServerHarness.Start();

            Assert.False(harness.Loop.IdleTimer.IsEnabled);
            harness.PumpUntil(() => false, 500);

            Assert.False(harness.WasStopRequested);
        }

        [Fact]
        public void TheLoopAnnouncesThePortItActuallyBound() {
            // Port zero in the config: the announced port is the one the socket got, which is the only way
            // a launcher can learn it.
            using DedicatedServerHarness harness = DedicatedServerHarness.Start();

            Assert.True(harness.Loop.ReadyPort > 0);
            Assert.Equal(harness.Endpoint.Port, harness.Loop.ReadyPort);
        }

        [Fact]
        public void TheStepLengthIsWhatTheConfigAskedFor() {
            ServerConfigBundle config = DedicatedServerHarness.BuildConfig();

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(config, new ServerRuntimeOptions(), null);

            Assert.Equal(config.Net.ServerTickInterval, harness.Loop.StepSeconds);
        }
    }
}
