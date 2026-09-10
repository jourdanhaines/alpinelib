using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode.Replication.StateChannel {
    /// <summary>
    /// The receiving half of a <see cref="ServerStateChannel{TState}"/>: holds the latest state the
    /// server published for each subject and raises an event whenever one moves forward.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike its server counterpart this claims its router id in the constructor and gives it back in
    /// <see cref="Dispose"/>. A client has exactly one connection, so there is no second instance to
    /// collide with; what there is instead is a session teardown, and a channel that left its handler
    /// bound would make the next session's registration throw on an id that looks free.
    /// </para>
    /// <para>
    /// <b>Ticks decide, not arrival order.</b> Dirty envelopes ride an unreliable channel and keyframes
    /// ride a reliable one, so a keyframe restating a second-old value can land after a snapshot that
    /// already moved past it. Every record carries the tick its state was true at, and a record older
    /// than what is already held is dropped rather than applied — which is the only thing standing
    /// between a driven train and a once-a-second twitch backwards.
    /// </para>
    /// </remarks>
    public sealed class ClientStateChannel<TState> : IDisposable where TState : struct, INetMessage {
        private readonly NetClient client;
        private readonly ushort messageId;
        private readonly List<ushort> ids = new List<ushort>();
        private readonly Dictionary<ushort, StateEntry> entriesById = new Dictionary<ushort, StateEntry>();

        private bool disposed;

        /// <summary>Creates a channel and binds it to the id its server counterpart publishes under.</summary>
        public ClientStateChannel(NetClient client, ushort messageId) {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.messageId = messageId;

            client.Router.Register<StateChannelEnvelope<TState>>(messageId, HandleEnvelope);
        }

        /// <summary>
        /// A subject's state moved forward: its id, the new state and the tick it was true at. Raised
        /// once per accepted record, so a listener that only cares about one subject can filter on the id
        /// without the channel needing to know that.
        /// </summary>
        public event Action<ushort, TState, uint> Updated;

        /// <summary>The id this channel listens on.</summary>
        public ushort MessageId => messageId;

        /// <summary>The subjects held, in the order they were first heard about.</summary>
        public IReadOnlyList<ushort> Ids => ids;

        /// <summary>
        /// The publish tick of the newest envelope accepted. Never moves backwards, so a late keyframe
        /// cannot drag a caller's sense of how current the channel is into the past.
        /// </summary>
        public uint LastServerTick { get; private set; }

        /// <summary>The state held for a subject and the tick it was true at.</summary>
        public bool TryGet(ushort id, out TState state, out uint tick) {
            if (!entriesById.TryGetValue(id, out StateEntry entry)) {
                state = default;
                tick = 0u;
                return false;
            }

            state = entry.State;
            tick = entry.Tick;
            return true;
        }

        /// <summary>Releases the router id. Safe to call twice.</summary>
        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            client.Router.Unregister(messageId);
        }

        private void HandleEnvelope(in StateChannelEnvelope<TState> message, PeerHandle sender) {
            if (message.ServerTick > LastServerTick) {
                LastServerTick = message.ServerTick;
            }

            List<StateChannelRecord<TState>> records = message.Records;

            if (records == null) {
                return;
            }

            for (int recordIndex = 0; recordIndex < records.Count; recordIndex++) {
                ApplyRecord(records[recordIndex]);
            }
        }

        /// <summary>Adopts one record unless it restates something older than what is already held.</summary>
        private void ApplyRecord(in StateChannelRecord<TState> record) {
            if (entriesById.TryGetValue(record.Id, out StateEntry held) && record.Tick < held.Tick) {
                return;
            }

            if (!entriesById.ContainsKey(record.Id)) {
                ids.Add(record.Id);
            }

            entriesById[record.Id] = new StateEntry(record.State, record.Tick);
            Updated?.Invoke(record.Id, record.State, record.Tick);
        }

        /// <summary>One subject's held state and the tick it was true at.</summary>
        private readonly struct StateEntry {
            public StateEntry(in TState state, uint tick) {
                State = state;
                Tick = tick;
            }

            /// <summary>The state last accepted.</summary>
            public TState State { get; }

            /// <summary>The tick that state was authoritative at.</summary>
            public uint Tick { get; }
        }
    }
}
