using System.Threading.Tasks;
using AlpineLib.Netcode.Sessions;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// What happens to a leave that is still in flight when the thing driving it goes away.
    /// </summary>
    /// <remarks>
    /// <c>LeaveAsync</c>'s task is resolved either by the transport dropping or by the leave grace
    /// running out inside <c>Tick</c> — both of which need somebody to keep pumping the client. A
    /// session service that tears down mid-leave stops pumping and drops the client, and disposing the
    /// connection underneath does not raise a disconnect, so the awaiting caller used to wait for the
    /// rest of the run with its own "a request is in flight" latch stuck down.
    /// </remarks>
    public sealed class SessionClientLeaveTests {
        [Fact]
        public void ALeaveNobodyWillPumpAgainIsResolvedRatherThanOrphaned() {
            using DedicatedServerHarness harness = DedicatedServerHarness.Start();
            HarnessClient player = harness.AddClient("Driver");

            Assert.True(harness.Complete(player.Session.ConnectAsync(harness.Endpoint)).IsSuccess);
            harness.Complete(player.Session.CreateSessionAsync(string.Empty));

            // Deliberately not pumped: the grace has not run out, so this is the window a teardown
            // lands in.
            Task leaving = player.Session.LeaveAsync();

            Assert.False(leaving.IsCompleted);

            player.Session.AbandonPendingLeave();

            Assert.True(leaving.IsCompleted);
            Assert.False(leaving.IsFaulted);
        }

        /// <summary>Abandoning when nothing is pending is a no-op, so a teardown may call it blind.</summary>
        [Fact]
        public void AbandoningWithNoLeavePendingChangesNothing() {
            using DedicatedServerHarness harness = DedicatedServerHarness.Start();
            HarnessClient player = harness.AddClient("Driver");

            Assert.True(harness.Complete(player.Session.ConnectAsync(harness.Endpoint)).IsSuccess);
            harness.Complete(player.Session.CreateSessionAsync(string.Empty));

            player.Session.AbandonPendingLeave();

            Assert.Equal(ClientSessionState.InSession, player.Session.State);
        }
    }
}
