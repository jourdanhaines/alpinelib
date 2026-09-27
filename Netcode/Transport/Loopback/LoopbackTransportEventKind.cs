namespace AlpineLib.Netcode.Transport {
    /// <summary>What a queued <see cref="LoopbackTransportEvent"/> reports.</summary>
    internal enum LoopbackTransportEventKind : byte {
        Connected = 0,
        Disconnected = 1,
        Data = 2
    }
}
