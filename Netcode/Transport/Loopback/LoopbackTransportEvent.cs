namespace AlpineLib.Netcode.Transport {
    /// <summary>One event waiting in a <see cref="LoopbackTransport"/> until its owner polls.</summary>
    internal readonly struct LoopbackTransportEvent {
        private LoopbackTransportEvent(LoopbackTransportEventKind kind, PeerHandle peer, byte[] payload, DeliveryClass delivery, DisconnectReason reason) {
            Kind = kind;
            Peer = peer;
            Payload = payload;
            Delivery = delivery;
            Reason = reason;
        }

        public LoopbackTransportEventKind Kind { get; }

        public PeerHandle Peer { get; }

        public byte[] Payload { get; }

        public DeliveryClass Delivery { get; }

        public DisconnectReason Reason { get; }

        public static LoopbackTransportEvent Connected(PeerHandle peer) {
            return new LoopbackTransportEvent(LoopbackTransportEventKind.Connected, peer, null, DeliveryClass.ReliableOrdered, DisconnectReason.Graceful);
        }

        public static LoopbackTransportEvent Disconnected(PeerHandle peer, DisconnectReason reason) {
            return new LoopbackTransportEvent(LoopbackTransportEventKind.Disconnected, peer, null, DeliveryClass.ReliableOrdered, reason);
        }

        public static LoopbackTransportEvent Data(PeerHandle sender, byte[] payload, DeliveryClass delivery) {
            return new LoopbackTransportEvent(LoopbackTransportEventKind.Data, sender, payload, delivery, DisconnectReason.Graceful);
        }
    }
}
