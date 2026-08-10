using System;

namespace AlpineLib.Netcode.Protocol {
    /// <summary>
    /// Raised whenever the wire format is violated: a writer running past the end of its buffer, a
    /// reader asking for more bytes than the datagram carries, or a malformed variable-length value.
    /// It exists as its own type so the transport layer can treat "this peer sent garbage" as a
    /// recoverable, peer-scoped fault instead of catching every <see cref="Exception"/>.
    /// </summary>
    public sealed class NetProtocolException : Exception {
        public NetProtocolException(string message) : base(message) { }

        public NetProtocolException(string message, Exception innerException) : base(message, innerException) { }
    }
}
