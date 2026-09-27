using AlpineLib.Netcode.Sessions;
using AlpineLib.Netcode.Transport;
using Xunit;

namespace AlpineLib.Server.Tests {
    /// <summary>How a launcher reads a server that exited before it was ready.</summary>
    public sealed class LocalServerExitClassifierTests {
        [Fact]
        public void ThePortInUseCodeIsAPortInUse() {
            Assert.Equal(LocalServerStartFailure.PortInUse, LocalServerExitClassifier.Classify(ServerExitCodes.PortInUse, string.Empty));
        }

        [Fact]
        public void ABindFailureLineIsAPortInUseEvenOnACleanExit() {
            string output = "crit: AlpineLib.Server.GameLoop.GameLoopService[0]\n      " + new TransportBindException(9050).Message + "\n";

            Assert.Equal(LocalServerStartFailure.PortInUse, LocalServerExitClassifier.Classify(ServerExitCodes.Clean, output));
        }

        [Theory]
        [InlineData(ServerExitCodes.Clean)]
        [InlineData(ServerExitCodes.BadConfiguration)]
        [InlineData(ServerExitCodes.LoopFault)]
        [InlineData(134)]
        public void AnyOtherExitIsACrash(int exitCode) {
            Assert.Equal(LocalServerStartFailure.Crashed, LocalServerExitClassifier.Classify(exitCode, "Unhandled exception. boom"));
        }

        [Fact]
        public void NoOutputIsNotABindFailure() {
            Assert.False(LocalServerExitClassifier.MentionsBindFailure(null));
            Assert.False(LocalServerExitClassifier.MentionsBindFailure(string.Empty));
        }

        [Fact]
        public void APortInUseDescriptionNamesThePort() {
            Assert.Equal("Port 9050 is in use.", LocalServerExitClassifier.Describe(LocalServerStartFailure.PortInUse, 9050));
        }

        [Fact]
        public void AHostDenialCarriesTheStartFailure() {
            SessionJoinResult denied = SessionJoinResult.Denied(
                SessionEndReason.TransportLost, SessionDenial.HostStartFailed, "Port 9050 is in use.", LocalServerStartFailure.PortInUse, 9050);
            SessionJoinResult plain = SessionJoinResult.Denied(SessionEndReason.TransportLost, SessionDenial.NoServerEndpoint, "No server endpoint.");

            Assert.Equal(LocalServerStartFailure.PortInUse, denied.HostStartFailure);
            Assert.Equal(9050, denied.HostStartPort);
            Assert.Equal(SessionDenial.HostStartFailed, denied.Denial);
            Assert.Equal(LocalServerStartFailure.None, plain.HostStartFailure);
            Assert.Equal(LocalServerStartFailure.None, SessionJoinResult.Connected().HostStartFailure);
        }
    }
}
