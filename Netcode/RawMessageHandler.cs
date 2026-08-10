using System;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode {
    /// <summary>
    /// Receive callback for an envelope id whose body the netcode layer does not decode — today that is
    /// the chat pipe (id 192), whose frames carry their own codec.
    /// </summary>
    /// <remarks>
    /// The payload points into the transport's receive buffer and is recycled the moment the handler
    /// returns, exactly as with <see cref="INetTransport.OnData"/>. A handler that needs to keep the
    /// bytes must copy them.
    /// </remarks>
    /// <param name="envelopeId">The id the payload arrived under, so one handler can serve several.</param>
    /// <param name="payload">The envelope body: everything after the two-byte id header.</param>
    /// <param name="sender">Peer the envelope came from; <see cref="PeerHandle.None"/> on a client.</param>
    public delegate void RawMessageHandler(ushort envelopeId, ArraySegment<byte> payload, PeerHandle sender);
}
