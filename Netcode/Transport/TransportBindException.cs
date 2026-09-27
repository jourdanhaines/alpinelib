using System;

namespace AlpineLib.Netcode.Transport {
    /// <summary>
    /// A server socket could not bind its port, almost always because another process already holds it.
    /// </summary>
    /// <remarks>
    /// Its own type so a host can tell "the port is taken" from every other startup fault without reading
    /// the message. The message still opens with <see cref="MessagePrefix"/>, because a launcher only sees
    /// the server's output and matches on that text.
    /// </remarks>
    public sealed class TransportBindException : InvalidOperationException {
        /// <summary>Opening words of every bind failure message, as a launcher reads it off the log.</summary>
        public const string MessagePrefix = "Could not bind UDP port";

        public TransportBindException(int port)
            : base($"{MessagePrefix} {port}.") {
            Port = port;
        }

        /// <summary>The port that was asked for.</summary>
        public int Port { get; }
    }
}
