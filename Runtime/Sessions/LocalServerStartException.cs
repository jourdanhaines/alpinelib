using System;
using AlpineLib.Netcode.Sessions;

namespace AlpineLib.Sessions {
    /// <summary>A local server that did not come up, with the reason typed for a caller to branch on.</summary>
    public sealed class LocalServerStartException : InvalidOperationException {
        public LocalServerStartException(LocalServerStartFailure failure, int port, string message)
            : base(message) {
            Failure = failure;
            Port = port;
        }

        /// <summary>Why it did not come up.</summary>
        public LocalServerStartFailure Failure { get; }

        /// <summary>The port it was asked to bind.</summary>
        public int Port { get; }
    }
}
