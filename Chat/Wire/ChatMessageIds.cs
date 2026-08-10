namespace AlpineLib.Chat.Wire {
    /// <summary>
    /// Chat's single reservation in the protocol-wide message id map.
    /// </summary>
    /// <remarks>
    /// Everything chat says travels under this one envelope id, with
    /// <see cref="ChatWireMessageType"/> as the first byte of the body. That is why chat can grow new
    /// frame types without ever touching the netcode id map, and why the netcode router never needs a
    /// chat registration beyond the raw handler that claims this id.
    /// </remarks>
    public static class ChatMessageIds {
        /// <summary>The envelope every chat frame rides in, band 192 of the protocol id map.</summary>
        public const ushort ChatPayload = 192;
    }
}
