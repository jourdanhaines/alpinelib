using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Sessions.Messages {
    /// <summary>
    /// Broadcast whenever the session's phase machine advances.
    /// </summary>
    /// <remarks>
    /// Carries the phase alone. Everything a phase needs beyond its name travels in the message that
    /// accompanies it — <see cref="MatchLoad"/> for MatchLoading, <see cref="MatchEnd"/> for
    /// MatchResults — which keeps this broadcast cheap enough to send unconditionally.
    /// </remarks>
    public struct PhaseChanged : INetMessage {
        /// <summary>Creates the broadcast for one transition.</summary>
        public PhaseChanged(SessionPhase phase) {
            Phase = phase;
        }

        /// <summary>The phase the session has just entered.</summary>
        public SessionPhase Phase { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteByte((byte)Phase);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Phase = (SessionPhase)reader.ReadByte();
        }
    }
}
