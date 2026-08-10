using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication.Messages {
    /// <summary>
    /// Server to client: the whole world, every entity, whether it moved or not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sent <c>ReliableOrdered</c> once a second, and on demand when someone joins or rejoins. The
    /// periodic one is the floor under the unreliable snapshot stream: however many snapshots a client
    /// loses, it is never more than a second away from being right again. The on-demand one is how a
    /// rejoining player gets a world at all, which is why its records carry prefab and ownership rather
    /// than just state (see <see cref="EntityKeyframeRecord"/>).
    /// </para>
    /// </remarks>
    public struct SnapshotKeyframe : INetMessage {
        /// <summary>Sanity cap on records in one keyframe; a larger claim is treated as corruption.</summary>
        public const int MaxRecordCount = 512;

        /// <summary>Creates a keyframe over a set of records.</summary>
        public SnapshotKeyframe(uint serverTick, List<EntityKeyframeRecord> records) {
            ServerTick = serverTick;
            Records = records;
        }

        /// <summary>The authoritative tick these states are from.</summary>
        public uint ServerTick { get; set; }

        /// <summary>Every live entity. Null is written as an empty keyframe.</summary>
        public List<EntityKeyframeRecord> Records { get; set; }

        /// <inheritdoc />
        public void Serialize(ref NetWriter writer) {
            writer.WriteUInt(ServerTick);

            int recordCount = Records == null ? 0 : Records.Count;
            writer.WriteUShort((ushort)recordCount);

            for (int recordIndex = 0; recordIndex < recordCount; recordIndex++) {
                writer.WriteMessage(Records[recordIndex]);
            }
        }

        /// <inheritdoc />
        public void Deserialize(ref NetReader reader) {
            ServerTick = reader.ReadUInt();

            int recordCount = reader.ReadUShort();

            if (recordCount > MaxRecordCount) {
                throw new NetProtocolException("SnapshotKeyframe declared " + recordCount.ToString()
                    + " records, which exceeds the sanity cap of " + MaxRecordCount.ToString() + ".");
            }

            Records = new List<EntityKeyframeRecord>(recordCount);

            for (int recordIndex = 0; recordIndex < recordCount; recordIndex++) {
                Records.Add(reader.ReadMessage<EntityKeyframeRecord>());
            }
        }
    }
}
