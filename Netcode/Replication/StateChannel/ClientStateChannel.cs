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
    /// bound would make the next session's registration throw on an id that looks free. The id is
    /// checked against <see cref="MessageIdBudget"/> here as well as on the server, because the client
    /// is the half where taking a library id does damage: the router refuses only the second
    /// registration, so a channel bound early enough wins the id and the library's own registration is
    /// what fails, naming a layer that did nothing wrong.
    /// </para>
    /// <para>
    /// <b>A state is a subject and a tick together.</b> Dirty envelopes ride an unreliable channel and
    /// keyframes ride a reliable one, so a keyframe restating a second-old value can land after a
    /// snapshot that already moved past it. Every record carries the tick its state was true at, and a
    /// record whose tick this channel already holds for that subject — older or equal — is dropped. So
    /// a keyframe cannot drag a driven train backwards, and a parked subject does not re-raise
    /// <see cref="Updated"/> once a second forever. The other edge of the same rule: a server that
    /// stamps <c>Set</c> with a tick it has already published publishes nothing, whatever the payload
    /// changed to. A retirement is gated the same way, only strictly: one older than the held state is
    /// dropped, so a reordered retirement cannot delete a subject the server has since restated. That
    /// rests on the server stamping every state with its own tick counter, which
    /// <c>ServerStateChannel.Set</c> enforces — the two ticks being compared here are otherwise not
    /// comparable at all.
    /// </para>
    /// <para>
    /// <b>One channel per connection.</b> The tick floor only means anything within one server's
    /// lifetime, and a listen host that returns to the lobby stands a fresh <c>NetServer</c> up whose
    /// tick counter restarts near zero. The library's own session service rebuilds its client-side
    /// objects every time a connection is built, and a game service that follows that pattern never has
    /// to think about this. A service that instead keeps one channel across connections must call
    /// <see cref="Clear"/> when the connection is replaced, or every record of the new session reads as
    /// stale and the channel is deaf for the rest of the process's life.
    /// </para>
    /// <para>
    /// <b>The game's own codec runs here.</b> <typeparamref name="TState"/> is decoded inside the
    /// client's poll, where only <c>NetProtocolException</c> is caught, so a <c>Deserialize</c> that
    /// throws anything else takes the whole client update with it.
    /// </para>
    /// </remarks>
    public sealed class ClientStateChannel<TState> : IDisposable where TState : struct, INetMessage {
        private readonly NetClient client;
        private readonly ushort messageId;
        private readonly List<ushort> ids = new List<ushort>();
        private readonly Dictionary<ushort, StateEntry> entriesById = new Dictionary<ushort, StateEntry>();

        private bool disposed;

        /// <summary>Creates a channel and binds it to the id its server counterpart publishes under.</summary>
        /// <exception cref="ArgumentException">The id is one the library already speaks.</exception>
        public ClientStateChannel(NetClient client, ushort messageId) {
            this.client = client ?? throw new ArgumentNullException(nameof(client));

            MessageIdBudget.GuardGameMessageId(messageId, nameof(messageId));

            this.messageId = messageId;

            client.Router.Register<StateChannelEnvelope<TState>>(messageId, HandleEnvelope);
        }

        /// <summary>
        /// A subject's state moved forward: its id, the new state and the tick it was true at. Raised
        /// once per accepted record, and a record restating a tick already held is not one — so a
        /// listener may be edge-triggered, and one that only cares about a single subject can filter on
        /// the id without the channel needing to know that.
        /// </summary>
        public event Action<ushort, TState, uint> Updated;

        /// <summary>
        /// A subject was retired by the server and has been forgotten. Raised only for a subject this
        /// channel actually held, so a retirement for an id it never heard of is silent.
        /// </summary>
        public event Action<ushort> Removed;

        /// <summary>The id this channel listens on.</summary>
        public ushort MessageId => messageId;

        /// <summary>The subjects held, in the order they were first heard about.</summary>
        /// <remarks>
        /// Eventually consistent within a publish rather than atomic across one: the server splits a
        /// large publish into several envelopes and each is folded on its own, so a caller reading this
        /// from inside <see cref="Updated"/> — or on a frame that lands between two chunks — sees a
        /// partial roster. Per-subject consumers never notice; anything that treats this as a set
        /// should read it outside the event.
        /// </remarks>
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

        /// <summary>
        /// Forgets every subject and the tick floor, keeping the router binding. For a channel carried
        /// across connections: a channel that outlives the connection it was filled from must be
        /// emptied before the next one publishes, or the new server's lower ticks all read as stale.
        /// </summary>
        /// <remarks>
        /// Silent on purpose — no <see cref="Removed"/> for the subjects dropped. The caller is tearing
        /// a session down and already knows; a storm of retirements for a session that no longer exists
        /// is noise every listener would have to filter back out.
        /// </remarks>
        public void Clear() {
            ids.Clear();
            entriesById.Clear();
            LastServerTick = 0u;
        }

        /// <summary>Releases the router id. Safe to call twice.</summary>
        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            client.Router.Unregister(messageId);
        }

        /// <remarks>
        /// The loop stops if a handler disposes the channel mid-envelope, which is what a session
        /// teardown listener does: delivering the rest after the caller said it was finished would be a
        /// surprise, and it would fold records into a store nothing owns any more.
        /// </remarks>
        private void HandleEnvelope(in StateChannelEnvelope<TState> message, PeerHandle sender) {
            if (message.ServerTick > LastServerTick) {
                LastServerTick = message.ServerTick;
            }

            List<StateChannelRecord<TState>> records = message.Records;

            if (records == null) {
                return;
            }

            for (int recordIndex = 0; recordIndex < records.Count && !disposed; recordIndex++) {
                ApplyRecord(records[recordIndex]);
            }
        }

        /// <summary>Adopts one record unless it restates a tick already held for that subject.</summary>
        private void ApplyRecord(in StateChannelRecord<TState> record) {
            if (record.IsRetired) {
                RetireSubject(record);
                return;
            }

            if (entriesById.TryGetValue(record.Id, out StateEntry held)) {
                AdoptOverHeld(record, held);
                return;
            }

            ids.Add(record.Id);
            entriesById[record.Id] = new StateEntry(record.State, record.Tick);
            Updated?.Invoke(record.Id, record.State, record.Tick);
        }

        /// <summary>Replaces a subject's held state, unless the record is not newer than it.</summary>
        private void AdoptOverHeld(in StateChannelRecord<TState> record, in StateEntry held) {
            if (record.Tick <= held.Tick) {
                return;
            }

            entriesById[record.Id] = new StateEntry(record.State, record.Tick);
            Updated?.Invoke(record.Id, record.State, record.Tick);
        }

        /// <summary>
        /// Drops a retired subject, unless the retirement is older than the state currently held for it.
        /// </summary>
        /// <remarks>
        /// A retirement is stamped with the tick it was published at, so it is subject to the same
        /// reordering as a state: the dirty publish is unreliable and unsequenced, so a retirement can
        /// arrive after a restatement the server made later, and applying it would delete a subject
        /// that exists until the next keyframe put it back. Both ticks come from the server's one
        /// counter, which is what makes the comparison mean anything. It is strict where
        /// <see cref="AdoptOverHeld"/>'s is not: a state at the held tick restates what is already
        /// known and is worth nothing, while a retirement at the held tick is a later decision about
        /// that same tick and must win. A retirement for a subject this channel does not hold stays a
        /// no-op, which is what makes the server's repeats until the next keyframe harmless.
        /// </remarks>
        private void RetireSubject(in StateChannelRecord<TState> record) {
            if (!entriesById.TryGetValue(record.Id, out StateEntry held)) {
                return;
            }

            if (record.Tick < held.Tick) {
                return;
            }

            entriesById.Remove(record.Id);
            ids.Remove(record.Id);
            Removed?.Invoke(record.Id);
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
