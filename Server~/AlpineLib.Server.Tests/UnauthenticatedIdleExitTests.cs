using AlpineLib.Netcode;
using AlpineLib.Netcode.Transport;
using AlpineLib.Server.Hosting;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// What the idle window counts as somebody being here.
    /// </summary>
    /// <remarks>
    /// A socket that completed the transport handshake and never said another word — a crashed client's
    /// half-open link, a stale client dialling a recycled ephemeral port — is nobody the server is up
    /// for. Counting it would leave an abandoned local server running until the machine is rebooted,
    /// which is the failure the idle window exists to prevent. The sibling case, a client that did
    /// authenticate, is pinned by <c>GameLoopIdleExitTests</c>.
    /// </remarks>
    public sealed class UnauthenticatedIdleExitTests {
        [Fact]
        public void ASocketThatNeverAuthenticatedDoesNotHoldTheServerOpen() {
            ServerRuntimeOptions options = new ServerRuntimeOptions { IdleExitSeconds = 1 };

            using DedicatedServerHarness harness = DedicatedServerHarness.Start(
                DedicatedServerHarness.BuildConfig(), options, null);

            using LiteNetTransport transport = new LiteNetTransport(harness.Config.Net.DisconnectTimeoutMs);
            NetClient lurker = new NetClient(transport, harness.Config.Net);
            lurker.Connect(harness.Endpoint);

            Assert.True(
                harness.PumpUntil(() => PumpLurker(lurker) && lurker.IsConnected, 5_000),
                "The lurker never completed the transport handshake.");
            Assert.Equal(0, harness.Query(() => harness.Registry.AuthenticatedPeerCount));

            Assert.True(
                harness.PumpUntil(() => PumpLurker(lurker) && harness.WasStopRequested, 15_000),
                "An unauthenticated socket still pins the server open past its idle window.");
        }

        /// <summary>Steps the lurker's own socket so its link stays up while the server waits it out.</summary>
        private static bool PumpLurker(NetClient lurker) {
            lurker.Update(1f / 60f);
            return true;
        }
    }
}
