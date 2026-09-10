using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// The game-defined payload the state channel tests replicate: a stand-in for the scalar state of a
    /// vehicle the server steps on its own — distance travelled, speed, and the control notch it is on.
    /// </summary>
    /// <remarks>
    /// It is deliberately nothing like a <c>PawnState</c>. The point of a state channel is that it moves
    /// a struct the library has never seen, so a test payload that looked like a pawn would let a
    /// hidden dependency on pawn shape pass unnoticed.
    /// </remarks>
    public struct StateChannelTestState : INetMessage {
        /// <summary>Creates a state.</summary>
        public StateChannelTestState(float distance, float velocity, sbyte notch) {
            Distance = distance;
            Velocity = velocity;
            Notch = notch;
        }

        /// <summary>Metres travelled along the route.</summary>
        public float Distance { get; set; }

        /// <summary>Metres per second, signed by travel direction.</summary>
        public float Velocity { get; set; }

        /// <summary>The control notch, negative for brake.</summary>
        public sbyte Notch { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteFloat(Distance);
            writer.WriteFloat(Velocity);
            writer.WriteSByte(Notch);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Distance = reader.ReadFloat();
            Velocity = reader.ReadFloat();
            Notch = reader.ReadSByte();
        }
    }
}
