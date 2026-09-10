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
    /// <see cref="MaxRecordCount"/> is a decode sanity cap and nothing more: it stops a corrupt length
    /// claim from being allocated for before a byte of body has been read. It is not a subject
    /// capacity and not a promise about how many records a sender emits. The binding limit is the send
    /// buffer, and <see cref="ServerStateChannel{TState}"/> splits a publish across as many envelopes
    /// as that takes, so its envelopes never come near this bound. The cap is enforced on both sides —
    /// an over-cap envelope fails on the server that built it rather than reaching a client as a
    /// malformed message.
    /// </para>
    /// <para>
    /// <see cref="HeaderBytes"/> is what a sender subtracts from its datagram budget before it starts
    /// measuring records into a chunk.
    /// </para>
    /// </remarks>
    public struct StateChannelEnvelope<TState> : INetMessage where TState : struct, INetMessage {
        /// <summary>Sanity cap on records in one envelope; a larger claim is treated as corruption.</summary>
        public const int MaxRecordCount = 512;

        /// <summary>Bytes the envelope's own header costs: the publish tick plus the record count.</summary>
        public const int HeaderBytes = 6;

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

            if (recordCount > MaxRecordCount) {
                throw new NetProtocolException("State channel envelope holds " + recordCount.ToString()
                    + " records, which exceeds the sanity cap of " + MaxRecordCount.ToString()
                    + " every receiver enforces.");
            }

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
