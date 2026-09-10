using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Server.Tests {
    /// <summary>
    /// A message a game's own module claims an id for, so the module-factory tests can prove a game can
    /// speak on the same socket the library owns.
    /// </summary>
    public struct GameModuleTestMessage : INetMessage {
        /// <summary>Wire id, inside the band a game may claim.</summary>
        public const ushort MessageId = MessageIdBudget.GameBandStart;

        /// <summary>Creates a message.</summary>
        public GameModuleTestMessage(byte payload) {
            Payload = payload;
        }

        /// <summary>Anything at all; the tests only care that it survived the trip.</summary>
        public byte Payload { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteByte(Payload);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            Payload = reader.ReadByte();
        }
    }
}
