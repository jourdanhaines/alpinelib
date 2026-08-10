using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication.Messages {
    /// <summary>
    /// Server to client, fifteen times a second: every entity that changed since the last snapshot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sent <c>UnreliableSequenced</c>. A lost snapshot is not worth retransmitting — by the time it
    /// arrived a newer one would already have superseded it — but a late one must never overwrite a newer
    /// one, which is exactly what sequencing buys.
    /// </para>
    /// <para>
    /// Only dirty entities are written. Idle entities are the common case in a lobby full of people
    /// standing around talking, and sending their unchanged positions fifteen times a second is pure
    /// waste. The reliable <see cref="SnapshotKeyframe"/> is what stops a client that missed the one
    /// snapshot an entity was dirty in from being wrong indefinitely.
    /// </para>
    /// </remarks>
    public struct Snapshot : INetMessage {
        /// <summary>Sanity cap on records in one snapshot; a larger claim is treated as corruption.</summary>
        public const int MaxRecordCount = 512;

        /// <summary>Creates a snapshot over a set of records.</summary>
        public Snapshot(uint serverTick, List<EntitySnapshotRecord> records) {
            ServerTick = serverTick;
            Records = records;
        }

        /// <summary>The authoritative tick these states are from.</summary>
        public uint ServerTick { get; set; }

        /// <summary>The entities that changed. Null is written as an empty snapshot.</summary>
        public List<EntitySnapshotRecord> Records { get; set; }

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
                throw new NetProtocolException("Snapshot declared " + recordCount.ToString()
                    + " records, which exceeds the sanity cap of " + MaxRecordCount.ToString() + ".");
            }

            Records = new List<EntitySnapshotRecord>(recordCount);

            for (int recordIndex = 0; recordIndex < recordCount; recordIndex++) {
                Records.Add(reader.ReadMessage<EntitySnapshotRecord>());
            }
        }
    }
}
