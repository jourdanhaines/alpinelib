using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;

namespace AlpineLib.Netcode.Replication.StateChannel {
    /// <summary>
    /// Server to client: the records a state channel is publishing this send, under one message id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One envelope carries both cadences. The dirty broadcast rides <c>UnreliableSequenced</c> — a lost
    /// one is superseded before a retransmit could land, but a late one must never overwrite a newer
    /// one — and the periodic keyframe rides <c>ReliableOrdered</c>. Nothing in the payload says which it
    /// was, and nothing needs to: every record carries the tick its state was true at, so a client
    /// resolves a stale line by comparing ticks rather than by trusting the channel it arrived on.
    /// </para>
    /// <para>
    /// <see cref="MaxRecordCount"/> is a decode sanity cap, not a capacity promise: it stops a corrupt
    /// length claim from being allocated for. The real ceiling is the send buffer, which a channel with
    /// hundreds of dirty subjects would overrun long before this bound — the same trade
    /// <c>Snapshot</c> makes, and the reason a channel is meant for tens of subjects rather than
    /// thousands.
    /// </para>
    /// </remarks>
    public struct StateChannelEnvelope<TState> : INetMessage where TState : struct, INetMessage {
        /// <summary>Sanity cap on records in one envelope; a larger claim is treated as corruption.</summary>
        public const int MaxRecordCount = 512;

        /// <summary>Creates an envelope over a set of records.</summary>
        public StateChannelEnvelope(uint serverTick, List<StateChannelRecord<TState>> records) {
            ServerTick = serverTick;
            Records = records;
        }

        /// <summary>The tick the envelope was published at.</summary>
        public uint ServerTick { get; set; }

        /// <summary>The records being published. Null is written as an empty envelope.</summary>
        public List<StateChannelRecord<TState>> Records { get; set; }

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
                throw new NetProtocolException("State channel envelope declared " + recordCount.ToString()
                    + " records, which exceeds the sanity cap of " + MaxRecordCount.ToString() + ".");
            }

            Records = new List<StateChannelRecord<TState>>(recordCount);

            for (int recordIndex = 0; recordIndex < recordCount; recordIndex++) {
                Records.Add(reader.ReadMessage<StateChannelRecord<TState>>());
            }
        }
    }
}
