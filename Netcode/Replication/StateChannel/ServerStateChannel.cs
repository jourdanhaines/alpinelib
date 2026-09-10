using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode.Replication.StateChannel {
    /// <summary>
    /// Publishes a set of game-defined states to a session's peers on the snapshot cadence, under one
    /// message id the game picks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is not an entity.</b> <c>ServerReplication</c> replicates pawns: things with a
    /// position, a motor, an owner and a prediction story. A train's scalar state — distance along the
    /// track, velocity, direction, lever notch — has none of that, and forcing it through
    /// <c>PawnState</c> would mean quantizing it as a position it is not and inheriting a correction
    /// path it does not want. This channel replicates whatever struct the game hands it, keeps a
    /// dirty set and a keyframe floor, and stops there. It holds no reference to
    /// <c>ServerReplication</c> and neither knows about the other.
    /// </para>
    /// <para>
    /// <b>Server to client only, so no router registration.</b> Nothing here receives, which is why
    /// there is no attach step and no id collision to arbitrate: several channels on one server each
    /// take their own id and never touch the router. A game that wants the other direction — a lever
    /// notch request, say — registers that message itself.
    /// </para>
    /// <para>
    /// <b>Per-session, like everything else that broadcasts.</b> Every send goes through
    /// <see cref="Peers"/>, the session's own member list, so a process hosting several lobbies keeps
    /// their channels apart without this code knowing sessions exist.
    /// </para>
    /// <para>
    /// <b>A publish is chunked to fit the datagram.</b> A send buffer is
    /// <see cref="NetBufferPool.DefaultBufferSize"/> bytes and <c>NetWriter</c> does not grow, so a
    /// publish that did not fit would throw out of the caller's tick loop. Records are therefore
    /// measured — by serializing each one into a scratch writer, which is the only answer that stays
    /// right for a <typeparamref name="TState"/> whose encoded size varies with its contents — and
    /// packed into as many envelopes as it takes. Both cadences chunk, so a keyframe is bounded the
    /// same way a dirty broadcast is. The one thing chunking cannot rescue is a single record too big
    /// for an empty datagram; that throws, because no amount of splitting would send it. A dirty
    /// publish that chunks also drops the sequencer — see <c>BroadcastDirtyRecords</c> — so a reorder
    /// inside the burst cannot discard the chunks that went out before it.
    /// </para>
    /// <para>
    /// <b>Retirement rides the record, not a message of its own.</b> <see cref="Remove"/> puts a
    /// retired record on the wire and the client forgets the subject. The retirement is repeated until
    /// the next keyframe has carried it reliably, because the dirty broadcast it first went out on is
    /// unreliable and a client that missed it would hold a ghost forever.
    /// </para>
    /// <para>
    /// <b>The game's own codec runs on the receive path.</b> A client decodes
    /// <typeparamref name="TState"/> inside its poll, where only <c>NetProtocolException</c> is caught,
    /// so a <c>Deserialize</c> that throws anything else takes the whole client update with it.
    /// </para>
    /// </remarks>
    public sealed class ServerStateChannel<TState> where TState : struct, INetMessage {
        /// <summary>How often the full reliable keyframe goes out, in seconds.</summary>
        public const double KeyframeIntervalSeconds = 1.0;

        /// <summary>
        /// Scratch room for measuring one record — several datagrams' worth, on purpose.
        /// </summary>
        /// <remarks>
        /// A record only ever outgrows an envelope because the game's state carries a string or a
        /// variable-length list, and those overshoot by a lot rather than by a byte. Measuring into a
        /// buffer the size of a datagram meant the answerable "subject N is too big" error only fired
        /// inside the few bytes between the envelope budget and the datagram, and every larger record —
        /// the shape that actually happens — came out as a raw writer overflow instead. Anything past
        /// even this is still named, just with a lower bound for its size.
        /// </remarks>
        private const int MeasureBufferBytes = NetBufferPool.DefaultBufferSize * 8;

        private readonly NetServer server;
        private readonly Func<IReadOnlyList<PeerHandle>> peerSource;
        private readonly NetConfig config;
        private readonly ushort messageId;
        private readonly List<ushort> ids = new List<ushort>();
        private readonly List<ushort> retiredIds = new List<ushort>();
        private readonly Dictionary<ushort, StateEntry> entriesById = new Dictionary<ushort, StateEntry>();
        private readonly HashSet<ushort> dirtyIds = new HashSet<ushort>();
        private readonly List<StateChannelRecord<TState>> records = new List<StateChannelRecord<TState>>();
        private readonly List<StateChannelRecord<TState>> chunk = new List<StateChannelRecord<TState>>();
        private readonly byte[] measureBuffer = new byte[MeasureBufferBytes];

        private double snapshotAccumulatorSeconds;
        private double keyframeAccumulatorSeconds;
        private uint currentTick;

        /// <summary>Creates a channel publishing under one game-owned message id.</summary>
        /// <param name="server">The facade the session broadcasts through.</param>
        /// <param name="peerSource">The session's member list, read fresh on every send.</param>
        /// <param name="messageId">The id this channel's envelopes travel under.</param>
        /// <param name="config">Supplies the snapshot cadence, so a channel and its session agree.</param>
        /// <exception cref="ArgumentException">The id is one the library already speaks.</exception>
        public ServerStateChannel(
            NetServer server,
            Func<IReadOnlyList<PeerHandle>> peerSource,
            ushort messageId,
            NetConfig config) {
            this.server = server ?? throw new ArgumentNullException(nameof(server));
            this.peerSource = peerSource ?? throw new ArgumentNullException(nameof(peerSource));
            this.config = config ?? throw new ArgumentNullException(nameof(config));

            MessageIdBudget.GuardGameMessageId(messageId, nameof(messageId));

            this.messageId = messageId;
        }

        /// <summary>
        /// Bytes one envelope's records may occupy: the send buffer less the id header the frame adds
        /// and the envelope's own header. A chunk is packed up to this and no further.
        /// </summary>
        public static int MaxRecordBytesPerEnvelope =>
            NetBufferPool.DefaultBufferSize - NetEnvelope.HeaderSize - StateChannelEnvelope<TState>.HeaderBytes;

        /// <summary>The id this channel's envelopes travel under.</summary>
        public ushort MessageId => messageId;

        /// <summary>How many subjects the channel is publishing.</summary>
        public int Count => ids.Count;

        /// <summary>The subjects, in the order they were first set. Iteration here is the wire order.</summary>
        public IReadOnlyList<ushort> Ids => ids;

        /// <summary>Subjects retired but not yet carried by a keyframe, so still being repeated.</summary>
        public IReadOnlyList<ushort> RetiringIds => retiredIds;

        /// <summary>The session's peers, as its member list reports them at this moment.</summary>
        public IReadOnlyList<PeerHandle> Peers => peerSource() ?? Array.Empty<PeerHandle>();

        /// <summary>
        /// Records a subject's state and marks it for the next broadcast. A subject seen for the first
        /// time joins the end of the wire order and stays there.
        /// </summary>
        /// <remarks>
        /// A state is identified by its subject and its tick together, and a client drops a record whose
        /// tick it already holds. Always stamp the tick the state was computed at: restating one tick
        /// with a new payload publishes nothing.
        /// </remarks>
        public void Set(ushort id, in TState state, uint tick) {
            if (!entriesById.ContainsKey(id)) {
                ids.Add(id);
                retiredIds.Remove(id);
            }

            entriesById[id] = new StateEntry(state, tick);
            dirtyIds.Add(id);
        }

        /// <summary>
        /// Drops a subject and tells the session to forget it. Returns false if the channel never held
        /// it.
        /// </summary>
        /// <remarks>
        /// A subject <see cref="Set"/> again before the retirement has gone out is never retired on the
        /// wire at all, so the client's memory of its tick is intact and <see cref="Set"/>'s equal-tick
        /// rule still applies: "remove it and put this in its place" needs a newer tick than the one
        /// the client already holds, exactly as a plain restatement does.
        /// </remarks>
        public bool Remove(ushort id) {
            if (!entriesById.Remove(id)) {
                return false;
            }

            ids.Remove(id);
            dirtyIds.Remove(id);

            if (!retiredIds.Contains(id)) {
                retiredIds.Add(id);
            }

            return true;
        }

        /// <summary>The state last set for a subject, or false if the channel does not hold it.</summary>
        public bool TryGet(ushort id, out TState state) {
            if (!entriesById.TryGetValue(id, out StateEntry entry)) {
                state = default;
                return false;
            }

            state = entry.State;
            return true;
        }

        /// <summary>
        /// One pump of the channel: publish the dirty subjects on the snapshot cadence and the whole set
        /// on the keyframe floor.
        /// </summary>
        /// <remarks>
        /// The two cadences are driven by wall time rather than by the tick counter for the same reason
        /// replication does it: the send rate is a bandwidth decision and the tick rate is a simulation
        /// decision, and tying them together would make one hostage to the other. Debt past one interval
        /// is dropped rather than banked, so a load hitch costs a late send and not a burst of catch-up
        /// sends at several times the configured rate afterwards. The keyframe is a floor and not a
        /// rate: its accumulator is reset rather than drained, so the interval between two keyframes is
        /// one second plus whatever of a frame the crossing overshot by.
        /// </remarks>
        /// <param name="serverTick">The authoritative tick counter, from <c>NetServer.Tick</c>.</param>
        /// <param name="deltaSeconds">Wall time since the previous call, which drives the send cadences.</param>
        public void Tick(uint serverTick, float deltaSeconds) {
            currentTick = serverTick;
            snapshotAccumulatorSeconds += deltaSeconds;
            keyframeAccumulatorSeconds += deltaSeconds;

            double snapshotInterval = config.SnapshotInterval;
            if (snapshotAccumulatorSeconds >= snapshotInterval) {
                snapshotAccumulatorSeconds = DrainedBy(snapshotAccumulatorSeconds, snapshotInterval);
                BroadcastDirty(serverTick);
            }

            if (keyframeAccumulatorSeconds >= KeyframeIntervalSeconds) {
                keyframeAccumulatorSeconds = 0.0;
                BroadcastKeyframe(serverTick);
            }
        }

        /// <summary>
        /// Sends the subjects that changed since the last broadcast to the whole session, unreliably,
        /// along with any retirement still in flight.
        /// </summary>
        /// <remarks>
        /// The dirty set is cleared only once every chunk has gone out. A send that throws — an
        /// oversized record, a transport refusing the datagram — therefore leaves the pending subjects
        /// pending, so the next broadcast republishes them instead of silently dropping a state nobody
        /// will ever hear about again.
        /// </remarks>
        public void BroadcastDirty(uint tick) {
            if (dirtyIds.Count == 0 && retiredIds.Count == 0) {
                return;
            }

            BuildRecords(onlyDirty: true, retireTick: tick);
            BroadcastDirtyRecords(tick);
            dirtyIds.Clear();
        }

        /// <summary>Sends every subject in full to the whole session, reliably — the 1 Hz floor.</summary>
        /// <remarks>
        /// Clears the dirty set too: a keyframe reaches every peer the dirty broadcast would have, so
        /// leaving it set would only buy an unreliable resend of what just went out reliably. It also
        /// retires the pending retirements, which is what bounds how long they are repeated.
        /// </remarks>
        public void BroadcastKeyframe(uint tick) {
            if (ids.Count == 0 && retiredIds.Count == 0) {
                return;
            }

            BuildRecords(onlyDirty: false, retireTick: tick);
            BroadcastRecords(tick, DeliveryClass.ReliableOrdered);
            dirtyIds.Clear();
            retiredIds.Clear();
        }

        /// <summary>
        /// Sends every subject in full to one peer — the join and rejoin path, so a late arrival does not
        /// wait up to a second for the keyframe floor to come round.
        /// </summary>
        /// <remarks>
        /// Neither accumulator is touched: this keyframe is one peer's business and resetting the floor
        /// would delay everyone else's. The joiner may therefore see a broadcast keyframe moments after
        /// its own, which costs one duplicate envelope and nothing else. Pending retirements ride along
        /// so a subject the joiner just heard about on the unreliable stream is not left behind as a
        /// ghost; ids it never held are ignored on the far side.
        /// </remarks>
        public void SendKeyframeTo(PeerHandle peer) {
            if (!peer.IsValid || (ids.Count == 0 && retiredIds.Count == 0)) {
                return;
            }

            BuildRecords(onlyDirty: false, retireTick: currentTick);
            SendRecordsTo(peer, currentTick, DeliveryClass.ReliableOrdered);
        }

        /// <summary>Fills the scratch list in wire order, either from the dirty set or from everything.</summary>
        private void BuildRecords(bool onlyDirty, uint retireTick) {
            records.Clear();

            for (int idIndex = 0; idIndex < ids.Count; idIndex++) {
                ushort id = ids[idIndex];

                if (onlyDirty && !dirtyIds.Contains(id)) {
                    continue;
                }

                StateEntry entry = entriesById[id];
                records.Add(new StateChannelRecord<TState>(id, entry.Tick, entry.State));
            }

            for (int retiredIndex = 0; retiredIndex < retiredIds.Count; retiredIndex++) {
                records.Add(StateChannelRecord<TState>.Retired(retiredIds[retiredIndex], retireTick));
            }
        }

        /// <summary>
        /// Sends a dirty publish, dropping the sequencer as soon as it takes more than one datagram.
        /// </summary>
        /// <remarks>
        /// <c>UnreliableSequenced</c> discards a packet that arrives after a later one, which is exactly
        /// right for a publish that is one datagram: a superseded snapshot is worth nothing. It is
        /// exactly wrong for a publish split across several, because the chunks share one publish tick
        /// and a reorder inside the burst throws the earlier chunks away outright — every subject in
        /// them missing that publish rather than merely arriving late. Plain <c>Unreliable</c> costs
        /// nothing here: every record carries its own tick and the client already compares on it, so
        /// the protection the sequencer offered was one the record tick had anyway.
        /// </remarks>
        private void BroadcastDirtyRecords(uint tick) {
            IReadOnlyList<PeerHandle> targets = Peers;
            int start = 0;
            bool isFirstChunk = true;

            while (start < records.Count) {
                FillChunk(start);
                start += chunk.Count;

                bool isWholePublish = isFirstChunk && start >= records.Count;
                isFirstChunk = false;

                DeliveryClass delivery = isWholePublish ? DeliveryClass.UnreliableSequenced : DeliveryClass.Unreliable;
                var message = new StateChannelEnvelope<TState>(tick, chunk);
                server.SendToMany(targets, messageId, in message, delivery);
            }
        }

        /// <summary>Sends the built records to the whole session, split across datagram-sized envelopes.</summary>
        private void BroadcastRecords(uint tick, DeliveryClass delivery) {
            IReadOnlyList<PeerHandle> targets = Peers;
            int start = 0;

            while (start < records.Count) {
                FillChunk(start);

                var message = new StateChannelEnvelope<TState>(tick, chunk);
                server.SendToMany(targets, messageId, in message, delivery);
                start += chunk.Count;
            }
        }

        /// <summary>Sends the built records to one peer, split the same way a broadcast is.</summary>
        private void SendRecordsTo(PeerHandle peer, uint tick, DeliveryClass delivery) {
            int start = 0;

            while (start < records.Count) {
                FillChunk(start);

                var message = new StateChannelEnvelope<TState>(tick, chunk);
                server.Send(peer, messageId, in message, delivery);
                start += chunk.Count;
            }
        }

        /// <summary>
        /// Packs records from <paramref name="start"/> into the chunk list until the next one would not
        /// fit the datagram. Always takes at least one, because a record that cannot fit alone throws.
        /// </summary>
        private void FillChunk(int start) {
            chunk.Clear();

            int budget = MaxRecordBytesPerEnvelope;
            int used = 0;

            for (int recordIndex = start; recordIndex < records.Count; recordIndex++) {
                int recordBytes = MeasureRecord(records[recordIndex]);
                GuardRecordFits(records[recordIndex].Id, recordBytes, budget);

                if (used + recordBytes > budget || chunk.Count == StateChannelEnvelope<TState>.MaxRecordCount) {
                    return;
                }

                chunk.Add(records[recordIndex]);
                used += recordBytes;
            }
        }

        /// <summary>
        /// The encoded size of one record, measured by writing it. The state is the game's own struct,
        /// so its size is not something this channel can compute from the type.
        /// </summary>
        private int MeasureRecord(in StateChannelRecord<TState> record) {
            var writer = new NetWriter(measureBuffer);

            try {
                writer.WriteMessage(record);
            }
            catch (NetProtocolException) {
                // Past even the scratch buffer: the exact size is unknown, but which subject it was and
                // that it cannot be split are the two things the caller needs.
                GuardRecordFits(record.Id, measureBuffer.Length, MaxRecordBytesPerEnvelope, isLowerBound: true);
            }

            return writer.Written;
        }

        private static void GuardRecordFits(ushort id, int recordBytes, int budget, bool isLowerBound = false) {
            if (recordBytes <= budget) {
                return;
            }

            string size = isLowerBound
                ? "more than " + recordBytes.ToString() + " bytes"
                : recordBytes.ToString() + " bytes";

            throw new InvalidOperationException(
                "Subject " + id.ToString() + " encodes to " + size
                + ", past the " + budget.ToString()
                + " an envelope has room for. Splitting cannot help a single oversized record: shrink the state.");
        }

        /// <summary>
        /// Pays one interval off a cadence accumulator, dropping anything still owed past a whole
        /// further interval. A frame hitch is a late send, not a licence to send at several times the
        /// configured rate until the debt clears.
        /// </summary>
        private static double DrainedBy(double accumulatorSeconds, double intervalSeconds) {
            double remainder = accumulatorSeconds - intervalSeconds;
            return remainder > intervalSeconds ? 0.0 : remainder;
        }

        /// <summary>One subject's stored state and the tick it was true at.</summary>
        private readonly struct StateEntry {
            public StateEntry(in TState state, uint tick) {
                State = state;
                Tick = tick;
            }

            /// <summary>The state as last set.</summary>
            public TState State { get; }

            /// <summary>The tick that state was authoritative at.</summary>
            public uint Tick { get; }
        }
    }
}
