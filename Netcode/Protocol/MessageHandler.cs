using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode.Protocol {
    /// <summary>
    /// Receive callback for one message type. The message is passed by <c>in</c> so a handler sees the
    /// decoded struct without a copy and without the struct ever being boxed onto the heap.
    /// </summary>
    /// <param name="message">The decoded message.</param>
    /// <param name="sender">Peer the message arrived from; <see cref="PeerHandle.None"/> on a client
    /// receiving from the server, where there is only ever one counterpart.</param>
    public delegate void MessageHandler<TMessage>(in TMessage message, PeerHandle sender)
        where TMessage : struct, INetMessage;
}
