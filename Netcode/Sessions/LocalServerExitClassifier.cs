using System;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode.Sessions {
    /// <summary>
    /// Reads why a local server exited before it was ready, from its exit code and its last output.
    /// </summary>
    /// <remarks>
    /// The output is checked as well as the code because an older server binary ends a failed bind with
    /// a clean exit; its log line is the only trace of the real cause.
    /// </remarks>
    public static class LocalServerExitClassifier {
        /// <summary>Classifies an exit before readiness.</summary>
        public static LocalServerStartFailure Classify(int exitCode, string recentOutput) {
            if (exitCode == ServerExitCodes.PortInUse) return LocalServerStartFailure.PortInUse;
            if (MentionsBindFailure(recentOutput)) return LocalServerStartFailure.PortInUse;

            return LocalServerStartFailure.Crashed;
        }

        /// <summary>True when the output carries the transport's bind failure line.</summary>
        public static bool MentionsBindFailure(string recentOutput) {
            if (string.IsNullOrEmpty(recentOutput)) return false;

            return recentOutput.IndexOf(TransportBindException.MessagePrefix, StringComparison.Ordinal) >= 0;
        }

        /// <summary>A short developer description of a failure on <paramref name="port"/>.</summary>
        public static string Describe(LocalServerStartFailure failure, int port) {
            switch (failure) {
                case LocalServerStartFailure.None: return "The local server started.";
                case LocalServerStartFailure.Missing: return "The local server is not installed.";
                case LocalServerStartFailure.PortInUse: return $"Port {port} is in use.";
                case LocalServerStartFailure.Timeout: return "The local server did not answer in time.";
                case LocalServerStartFailure.Cancelled: return "Hosting was cancelled.";
                default: return "The local server stopped before it was ready.";
            }
        }
    }
}
