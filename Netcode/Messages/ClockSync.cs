using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Messages {
    /// <summary>
    /// The server's tick counter, broadcast to every connected peer once a second.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what bootstraps and then anchors a client's <c>NetClock</c>. It carries the tick alone:
    /// the round trip needed to turn a stamp into an estimate is measured by the transport, which
    /// already tracks it for every peer, so putting a timestamp in the payload would only add a second
    /// and worse clock to disagree with.
    /// </para>
    /// <para>
    /// Sent <c>UnreliableSequenced</c>. A dropped sync costs nothing — another follows in a second and
    /// the estimate free-runs meanwhile — but a reordered one would drag the estimate backwards, and
    /// sequencing is what rules that out.
    /// </para>
    /// </remarks>
    public struct ClockSync : INetMessage {
        /// <summary>Creates a sync for one authoritative tick.</summary>
        public ClockSync(uint serverTick) {
            ServerTick = serverTick;
        }

        /// <summary>The server's tick counter at the moment the packet was queued.</summary>
        public uint ServerTick { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteUInt(ServerTick);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            ServerTick = reader.ReadUInt();
        }
    }
}
