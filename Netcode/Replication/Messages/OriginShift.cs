using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication.Origin;

namespace AlpineLib.Netcode.Replication.Messages {
    /// <summary>
    /// Server to client: the session origin moved. Sent reliable-ordered on the channel spawns and
    /// keyframes use, so everything reliable sent after it is already written in the new epoch. Also sent
    /// to a newcomer ahead of its first spawn or keyframe once the session has ever rebased.
    /// </summary>
    public struct OriginShift : INetMessage {
        /// <summary>Creates the announcement of one origin.</summary>
        public OriginShift(in SessionOrigin origin) {
            Origin = origin;
        }

        /// <summary>The origin now in force.</summary>
        public SessionOrigin Origin { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteUShort(Origin.Epoch);
            writer.WriteInt(Origin.CellX);
            writer.WriteInt(Origin.CellZ);
            writer.WriteDouble(Origin.CellSize);
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            ushort epoch = reader.ReadUShort();
            int cellX = reader.ReadInt();
            int cellZ = reader.ReadInt();
            double cellSize = reader.ReadDouble();

            if (double.IsNaN(cellSize) || double.IsInfinity(cellSize) || cellSize < 0.0) {
                throw new NetProtocolException("OriginShift carried an invalid cell size.");
            }

            Origin = new SessionOrigin(epoch, cellX, cellZ, cellSize);
        }
    }
}
