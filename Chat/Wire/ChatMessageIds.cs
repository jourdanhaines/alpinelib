namespace AlpineLib.Chat.Wire {
    /// <summary>
    /// Chat's single reservation in the protocol-wide message id map.
    /// </summary>
    /// <remarks>
    /// Everything chat says travels under this one envelope id, with
    /// <see cref="ChatWireMessageType"/> as the first byte of the body. That is why chat can grow new
    /// frame types without ever touching the netcode id map, and why the netcode router never needs a
    /// chat registration beyond the raw handler that claims this id.
    ///
    /// Chat reserves exactly one id, not a range. <c>MessageIdBudget</c> in the netcode assembly is the
    /// single authority on the whole map and already accounts for this one.
    /// </remarks>
    public static class ChatMessageIds {
        /// <summary>The envelope every chat frame rides in, band 192 of the protocol id map.</summary>
        public const ushort ChatPayload = 192;
    }
}
