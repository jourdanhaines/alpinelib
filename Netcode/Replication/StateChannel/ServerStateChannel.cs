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
    /// <b>Removal is server-local.</b> <see cref="Remove"/> drops a subject from the channel but puts
    /// nothing on the wire: a client that already holds the id keeps its last state until the game tells
    /// it otherwise. Subjects on a state channel are expected to live as long as the session — the
    /// consist that exists for the whole match — so paying for a retire message on every channel would
    /// buy nothing for the case it is built for.
    /// </para>
    /// </remarks>
    public sealed class ServerStateChannel<TState> where TState : struct, INetMessage {
        /// <summary>How often the full reliable keyframe goes out, in seconds.</summary>
        public const double KeyframeIntervalSeconds = 1.0;

        private readonly NetServer server;
        private readonly Func<IReadOnlyList<PeerHandle>> peerSource;
        private readonly NetConfig config;
        private readonly ushort messageId;
        private readonly List<ushort> ids = new List<ushort>();
        private readonly Dictionary<ushort, StateEntry> entriesById = new Dictionary<ushort, StateEntry>();
        private readonly HashSet<ushort> dirtyIds = new HashSet<ushort>();
        private readonly List<StateChannelRecord<TState>> records = new List<StateChannelRecord<TState>>();

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

            if (MessageIdBudget.IsReservedByLibrary(messageId)) {
                throw new ArgumentException(
                    "Message id " + messageId.ToString() + " is reserved by the library. Author game messages in the "
                    + MessageIdBudget.GameBandStart.ToString() + "-" + MessageIdBudget.GameBandEnd.ToString() + " band.",
                    nameof(messageId));
            }

            this.messageId = messageId;
        }

        /// <summary>The id this channel's envelopes travel under.</summary>
        public ushort MessageId => messageId;

        /// <summary>How many subjects the channel is publishing.</summary>
        public int Count => ids.Count;

        /// <summary>The subjects, in the order they were first set. Iteration here is the wire order.</summary>
        public IReadOnlyList<ushort> Ids => ids;

        /// <summary>The session's peers, as its member list reports them at this moment.</summary>
        public IReadOnlyList<PeerHandle> Peers => peerSource() ?? Array.Empty<PeerHandle>();

        /// <summary>
        /// Records a subject's state and marks it for the next broadcast. A subject seen for the first
        /// time joins the end of the wire order and stays there.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Adding this subject would push the channel past <see cref="StateChannelEnvelope{TState}.MaxRecordCount"/>,
        /// beyond which its own keyframe would not decode.
        /// </exception>
        public void Set(ushort id, in TState state, uint tick) {
            if (!entriesById.ContainsKey(id)) {
                GuardCapacity();
                ids.Add(id);
            }

            entriesById[id] = new StateEntry(state, tick);
            dirtyIds.Add(id);
        }

        /// <summary>Drops a subject. Returns false if the channel never held it.</summary>
        public bool Remove(ushort id) {
            if (!entriesById.Remove(id)) {
                return false;
            }

            ids.Remove(id);
            dirtyIds.Remove(id);
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
        /// decision, and tying them together would make one hostage to the other.
        /// </remarks>
        /// <param name="serverTick">The authoritative tick counter, from <c>NetServer.Tick</c>.</param>
        /// <param name="deltaSeconds">Wall time since the previous call, which drives the send cadences.</param>
        public void Tick(uint serverTick, float deltaSeconds) {
            currentTick = serverTick;
            snapshotAccumulatorSeconds += deltaSeconds;
            keyframeAccumulatorSeconds += deltaSeconds;

            double snapshotInterval = config.SnapshotInterval;
            if (snapshotAccumulatorSeconds >= snapshotInterval) {
                snapshotAccumulatorSeconds -= snapshotInterval;
                BroadcastDirty(serverTick);
            }

            if (keyframeAccumulatorSeconds >= KeyframeIntervalSeconds) {
                keyframeAccumulatorSeconds = 0.0;
                BroadcastKeyframe(serverTick);
            }
        }

        /// <summary>
        /// Sends the subjects that changed since the last broadcast to the whole session, unreliably.
        /// Clears the dirty set whether or not anything was on it.
        /// </summary>
        public void BroadcastDirty(uint tick) {
            if (dirtyIds.Count == 0) {
                return;
            }

            BuildRecords(onlyDirty: true);
            dirtyIds.Clear();

            var message = new StateChannelEnvelope<TState>(tick, records);
            server.SendToMany(Peers, messageId, in message, DeliveryClass.UnreliableSequenced);
        }

        /// <summary>Sends every subject in full to the whole session, reliably — the 1 Hz floor.</summary>
        public void BroadcastKeyframe(uint tick) {
            if (ids.Count == 0) {
                return;
            }

            BuildRecords(onlyDirty: false);

            var message = new StateChannelEnvelope<TState>(tick, records);
            server.SendToMany(Peers, messageId, in message, DeliveryClass.ReliableOrdered);
        }

        /// <summary>
        /// Sends every subject in full to one peer — the join and rejoin path, so a late arrival does not
        /// wait up to a second for the keyframe floor to come round.
        /// </summary>
        public void SendKeyframeTo(PeerHandle peer) {
            if (!peer.IsValid || ids.Count == 0) {
                return;
            }

            BuildRecords(onlyDirty: false);

            var message = new StateChannelEnvelope<TState>(currentTick, records);
            server.Send(peer, messageId, in message, DeliveryClass.ReliableOrdered);
        }

        /// <summary>Fills the scratch list in wire order, either from the dirty set or from everything.</summary>
        private void BuildRecords(bool onlyDirty) {
            records.Clear();

            for (int idIndex = 0; idIndex < ids.Count; idIndex++) {
                ushort id = ids[idIndex];

                if (onlyDirty && !dirtyIds.Contains(id)) {
                    continue;
                }

                StateEntry entry = entriesById[id];
                records.Add(new StateChannelRecord<TState>(id, entry.Tick, entry.State));
            }
        }

        private void GuardCapacity() {
            if (ids.Count < StateChannelEnvelope<TState>.MaxRecordCount) {
                return;
            }

            throw new InvalidOperationException(
                "A state channel holds at most " + StateChannelEnvelope<TState>.MaxRecordCount.ToString()
                + " subjects, because a keyframe carrying more than that would not decode.");
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
