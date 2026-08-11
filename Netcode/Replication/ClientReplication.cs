using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication.Messages;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// The client's copy of the session's world: it receives spawns, snapshots and corrections, predicts
    /// the pawn it owns, and interpolates everyone else's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Entities fall into two kinds and are handled in opposite directions. The pawn this client owns
    /// under <see cref="AuthorityMode.Server"/> is driven <em>forward</em> from local input through
    /// <see cref="PawnMotor"/> and only ever pulled back by an <c>AuthorityCorrection</c>, which is why
    /// its snapshot records are deliberately ignored — adopting a state from a hundred milliseconds ago
    /// would undo the prediction that makes the pawn feel connected to the controller. Every other pawn
    /// is driven <em>backward</em>, rendered from a buffer of received states at a fixed delay through
    /// <see cref="StateInterpolator"/>.
    /// </para>
    /// <para>
    /// Unlike its server counterpart this claims its router ids in the constructor. A client has exactly
    /// one connection and one session, so there is no second instance to collide with, and making the
    /// caller remember an attach step would be ceremony with no purpose.
    /// </para>
    /// </remarks>
    public sealed class ClientReplication : IDisposable {
        /// <summary>
        /// Outstanding self-raised events remembered for echo suppression. Bounded because the pending
        /// set is only ever a round trip deep in practice, and an unbounded one would grow for the whole
        /// session if a stamp ever failed to come back.
        /// </summary>
        public const int MaxPendingEventSequences = 64;

        /// <summary>Horizontal position error below which a correction counts as agreement, in metres.</summary>
        public const float CorrectionHorizontalEpsilon = 0.01f;

        /// <summary>
        /// Vertical position error below which a correction counts as agreement, in metres. Far looser
        /// than the horizontal bound on purpose: the visible pawn stands on real scene geometry while
        /// both simulations stand on the ground seam, and until a real heightfield provider exists that
        /// gap is a fact of the floor, not a prediction error worth a rewind.
        /// </summary>
        public const float CorrectionVerticalEpsilon = 0.25f;

        /// <summary>Per-axis velocity error below which a correction counts as agreement, in m/s.</summary>
        public const float CorrectionVelocityEpsilon = 0.1f;

        /// <summary>
        /// Flag bits a correction must agree on: gait and crouch. Grounded is excluded — it flaps across
        /// step timing at ledges and ground tolerance, and a divergence that matters shows up in position.
        /// </summary>
        public const byte CorrectionFlagsMask = PawnState.LocomotionMask | PawnState.CrouchBit;

        private readonly NetClient client;
        private readonly NetConfig config;
        private readonly Dictionary<uint, NetEntity> entitiesById = new Dictionary<uint, NetEntity>();
        private readonly List<NetEntity> entities = new List<NetEntity>();
        private readonly Dictionary<uint, StateInterpolator> interpolators = new Dictionary<uint, StateInterpolator>();
        private readonly Dictionary<uint, PredictionBuffer> predictionBuffers = new Dictionary<uint, PredictionBuffer>();
        private readonly HashSet<uint> pendingEventSequences = new HashSet<uint>();
        private readonly Queue<uint> pendingEventOrder = new Queue<uint>();
        private readonly Dictionary<uint, RecentInputs> recentInputsByEntity = new Dictionary<uint, RecentInputs>();
        private readonly Dictionary<uint, SettledPrediction> settledByEntity = new Dictionary<uint, SettledPrediction>();

        private readonly InterpolationTimeline timeline;

        private IGroundProvider groundProvider;
        private uint nextEventSequence = 1u;
        private uint nextInputSequence = 1u;
        private bool disposed;

        /// <summary>Creates a client world predicting over flat ground at y = 0.</summary>
        public ClientReplication(NetClient client, NetConfig config)
            : this(client, config, new FlatGroundProvider()) { }

        /// <summary>Creates a client world with an explicit ground seam for prediction.</summary>
        public ClientReplication(NetClient client, NetConfig config, IGroundProvider groundProvider) {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.groundProvider = groundProvider ?? throw new ArgumentNullException(nameof(groundProvider));
            timeline = new InterpolationTimeline(config);

            LocalPeerId = PeerHandle.None.Id;

            client.Router.Register<SpawnEntity>(ReplicationMessageIds.SpawnEntity, HandleSpawnEntity);
            client.Router.Register<DespawnEntity>(ReplicationMessageIds.DespawnEntity, HandleDespawnEntity);
            client.Router.Register<Snapshot>(ReplicationMessageIds.Snapshot, HandleSnapshot);
            client.Router.Register<SnapshotKeyframe>(ReplicationMessageIds.SnapshotKeyframe, HandleSnapshotKeyframe);
            client.Router.Register<EntityEvent>(ReplicationMessageIds.EntityEvent, HandleEntityEvent);
            client.Router.Register<AuthorityCorrection>(ReplicationMessageIds.AuthorityCorrection, HandleAuthorityCorrection);
        }

        /// <summary>An entity appeared, from a spawn or from a keyframe describing one not seen before.</summary>
        public event Action<NetEntity> OnEntitySpawned;

        /// <summary>An entity is gone.</summary>
        public event Action<uint> OnEntityDespawned;

        /// <summary>A discrete event on an entity: entity id, event id, argument. Echoes are filtered out.</summary>
        public event Action<uint, byte, byte> OnEntityEvent;

        /// <summary>
        /// The server disagreed with local prediction, or clamped a reported state. Carries the entity
        /// and the state now in force, so a controller can decide between snapping and smoothing.
        /// </summary>
        public event Action<NetEntity, PawnState> OnAuthorityCorrected;

        /// <summary>
        /// This client's peer id, as the session handshake reported it. Ownership is decided against it,
        /// so a controller that reads <see cref="IsOwned"/> before the session sets this sees nothing as
        /// owned — which is the safe direction to be wrong in.
        /// </summary>
        public int LocalPeerId { get; set; }

        /// <summary>Ground seam used for prediction. Must match the server's or corrections never settle.</summary>
        public IGroundProvider GroundProvider {
            get => groundProvider;
            set => groundProvider = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Every entity this client knows about.</summary>
        public IReadOnlyList<NetEntity> Entities => entities;

        /// <summary>Corrections that disagreed with prediction and forced a rewind. Diagnostic.</summary>
        public int CorrectionsApplied { get; private set; }

        /// <summary>Corrections that matched prediction and were acknowledged without a rewind. Diagnostic.</summary>
        public int CorrectionsSkipped { get; private set; }

        /// <summary>The adaptive render-delay controller for this connection.</summary>
        public InterpolationTimeline Timeline => timeline;

        /// <summary>
        /// Advances the render-delay controller for one frame. Called by whoever pumps the client —
        /// once per frame, before remote pawns sample.
        /// </summary>
        public void Tick(float deltaSeconds) {
            timeline.Update(deltaSeconds, client.Clock.PingMs);
        }

        /// <summary>Finds an entity by id, or null.</summary>
        public NetEntity GetEntity(uint entityId) {
            return entitiesById.TryGetValue(entityId, out NetEntity entity) ? entity : null;
        }

        /// <summary>The render buffer for an entity, or null when it has none.</summary>
        public StateInterpolator GetInterpolator(uint entityId) {
            return interpolators.TryGetValue(entityId, out StateInterpolator interpolator) ? interpolator : null;
        }

        /// <summary>The pending-input record for a predicted entity, or null when it is not predicted.</summary>
        public PredictionBuffer GetPredictionBuffer(uint entityId) {
            return predictionBuffers.TryGetValue(entityId, out PredictionBuffer buffer) ? buffer : null;
        }

        /// <summary>True when this client's player owns the entity.</summary>
        public bool IsOwned(NetEntity entity) {
            return entity != null && entity.OwnerPeerId >= 0 && entity.OwnerPeerId == LocalPeerId;
        }

        /// <summary>True when the entity is one this client predicts rather than interpolates.</summary>
        public bool IsPredicted(NetEntity entity) {
            return IsOwned(entity) && entity.Authority == AuthorityMode.Server;
        }

        /// <summary>
        /// Sends one step of intent for an owned, server-authoritative pawn and immediately applies what
        /// the shared motor says it will do, so the pawn responds on this frame rather than in a round
        /// trip's time.
        /// </summary>
        /// <remarks>
        /// The input's sequence is stamped here, from a counter this class owns, whatever the caller put
        /// in it. Sequences are the acknowledgement contract with the server and must never duplicate or
        /// regress — a clock-derived stamp does both — so no caller is trusted to provide them.
        /// </remarks>
        /// <returns>The predicted state, also written to the entity.</returns>
        public PawnState SubmitInput(uint entityId, in PawnInput input) {
            NetEntity entity = GetEntity(entityId);

            if (entity == null || !IsPredicted(entity)) {
                return entity?.State ?? default;
            }

            MovementProfile profile = config.GetMovementProfile(entity.PrefabId);

            if (profile == null) {
                return entity.State;
            }

            PawnInput stamped = input;
            stamped.Sequence = nextInputSequence;
            nextInputSequence++;

            PawnState predicted = PawnMotor.Step(in stamped, entity.State, profile, groundProvider, config.ServerTickInterval);
            entity.ApplyState(predicted, stamped.Sequence);
            ResolvePredictionBuffer(entityId).Record(in stamped, in predicted);

            InputCommand message = BuildInputBundle(entityId, in stamped);
            client.Send(ReplicationMessageIds.InputCommand, in message, DeliveryClass.UnreliableSequenced);
            return predicted;
        }

        /// <summary>
        /// Wraps the newest input together with the previous two sent for the same entity, so any single
        /// lost or reordered packet is healed by the next one that survives; the server deduplicates by
        /// sequence.
        /// </summary>
        private InputCommand BuildInputBundle(uint entityId, in PawnInput newest) {
            recentInputsByEntity.TryGetValue(entityId, out RecentInputs recent);
            InputCommand command = InputCommand.Bundle(entityId, in recent.BeforePrevious, in recent.Previous, in newest, recent.Count);

            recent.BeforePrevious = recent.Previous;
            recent.Previous = newest;
            recent.Count = recent.Count < 2 ? recent.Count + 1 : 2;
            recentInputsByEntity[entityId] = recent;

            return command;
        }

        /// <summary>The last two inputs sent for one entity, kept only to ride along as redundancy.</summary>
        private struct RecentInputs {
            public PawnInput BeforePrevious;
            public PawnInput Previous;
            public int Count;
        }

        /// <summary>The most recently settled acknowledgement for one entity, wire-quantized.</summary>
        private readonly struct SettledPrediction {
            public SettledPrediction(uint sequence, in PawnState state) {
                Sequence = sequence;
                State = state;
            }

            public uint Sequence { get; }

            public PawnState State { get; }
        }

        /// <summary>
        /// Reports a locally simulated state for an owned pawn in
        /// <see cref="AuthorityMode.OwnerClient"/>. The server will judge it and may send it back changed.
        /// </summary>
        public void SubmitOwnerPawnState(uint entityId, in PawnState state) {
            NetEntity entity = GetEntity(entityId);

            if (entity == null || !IsOwned(entity) || entity.Authority != AuthorityMode.OwnerClient) {
                return;
            }

            entity.ApplyState(state, client.Clock.EstimatedServerTick);

            var message = new OwnerPawnUpdate(entityId, client.Clock.EstimatedServerTick, in state);
            client.Send(ReplicationMessageIds.OwnerPawnUpdate, in message, DeliveryClass.UnreliableSequenced);
        }

        /// <summary>
        /// Raises a discrete event on an owned entity. The caller plays it locally straight away; the
        /// stamp returned here is remembered so the server's echo of the same event is recognised and
        /// dropped instead of played a second time.
        /// </summary>
        /// <returns>
        /// The sequence stamp sent with the event, or <see cref="EntityEvent.ServerSequence"/> when the
        /// entity is not one this client may raise events on and nothing was sent.
        /// </returns>
        public uint SendEntityEvent(uint entityId, byte eventId, byte argument) {
            NetEntity entity = GetEntity(entityId);

            if (entity == null || !IsOwned(entity)) {
                // The authority drops events raised on somebody else's pawn, so sending one would only
                // leave a stamp in the pending set that no echo ever comes back to clear.
                return EntityEvent.ServerSequence;
            }

            uint sequence = nextEventSequence;
            nextEventSequence++;

            if (nextEventSequence == EntityEvent.ServerSequence) {
                // Zero is the server's marker, so a wrapped counter must step over it.
                nextEventSequence = 1u;
            }

            RememberPendingEvent(sequence);

            var message = new EntityEvent(entityId, eventId, argument, sequence);
            client.Send(ReplicationMessageIds.EntityEvent, in message, DeliveryClass.ReliableOrdered);
            return sequence;
        }

        /// <summary>
        /// The pose to render for a remote entity right now: the client's estimate of the server clock,
        /// held back by the timeline's current interpolation delay.
        /// </summary>
        public bool SampleRemote(uint entityId, out PawnState state) {
            StateInterpolator interpolator = GetInterpolator(entityId);

            if (interpolator == null) {
                state = default;
                return false;
            }

            double renderSeconds = client.Clock.EstimatedServerSeconds - timeline.DelaySeconds;
            return interpolator.Sample(renderSeconds, out state);
        }

        /// <summary>Drops the whole world. Used when leaving a session or before rebuilding on rejoin.</summary>
        public void Clear() {
            entitiesById.Clear();
            entities.Clear();
            interpolators.Clear();
            predictionBuffers.Clear();
            pendingEventSequences.Clear();
            pendingEventOrder.Clear();
            recentInputsByEntity.Clear();
            settledByEntity.Clear();
        }

        /// <inheritdoc />
        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            Clear();

            client.Router.Unregister(ReplicationMessageIds.SpawnEntity);
            client.Router.Unregister(ReplicationMessageIds.DespawnEntity);
            client.Router.Unregister(ReplicationMessageIds.Snapshot);
            client.Router.Unregister(ReplicationMessageIds.SnapshotKeyframe);
            client.Router.Unregister(ReplicationMessageIds.EntityEvent);
            client.Router.Unregister(ReplicationMessageIds.AuthorityCorrection);
        }

        private void HandleSpawnEntity(in SpawnEntity message, PeerHandle sender) {
            AdoptEntity(message.EntityId, message.PrefabId, message.OwnerPeerId, message.Authority, message.State, 0u);
        }

        private void HandleDespawnEntity(in DespawnEntity message, PeerHandle sender) {
            if (!entitiesById.Remove(message.EntityId)) {
                return;
            }

            interpolators.Remove(message.EntityId);
            predictionBuffers.Remove(message.EntityId);
            RemoveFromList(message.EntityId);
            OnEntityDespawned?.Invoke(message.EntityId);
        }

        private void HandleSnapshot(in Snapshot message, PeerHandle sender) {
            if (message.Records == null) {
                return;
            }

            timeline.OnSnapshotArrived(client.Clock.EstimatedServerSeconds);

            for (int recordIndex = 0; recordIndex < message.Records.Count; recordIndex++) {
                EntitySnapshotRecord record = message.Records[recordIndex];
                ApplyReceivedState(record.EntityId, record.State, message.ServerTick);
            }
        }

        private void HandleSnapshotKeyframe(in SnapshotKeyframe message, PeerHandle sender) {
            if (message.Records == null) {
                return;
            }

            for (int recordIndex = 0; recordIndex < message.Records.Count; recordIndex++) {
                EntityKeyframeRecord record = message.Records[recordIndex];
                AdoptEntity(
                    record.EntityId,
                    record.PrefabId,
                    record.OwnerPeerId,
                    record.Authority,
                    record.State,
                    message.ServerTick);
            }
        }

        private void HandleEntityEvent(in EntityEvent message, PeerHandle sender) {
            if (message.Sequence != EntityEvent.ServerSequence && pendingEventSequences.Remove(message.Sequence)) {
                // Our own event coming back. It was played the moment it was raised; playing it again is
                // exactly the double emote the pending set exists to prevent.
                return;
            }

            OnEntityEvent?.Invoke(message.EntityId, message.EventId, message.Argument);
        }

        /// <summary>
        /// A correction arrived. The server sends one on every snapshot without knowing what this client
        /// predicted, so agreement is judged here: a state matching the prediction for the acknowledged
        /// sequence settles that sequence and does nothing else, and only a genuine disagreement rewinds,
        /// replays and tells the pawn's controller anything happened.
        /// </summary>
        private void HandleAuthorityCorrection(in AuthorityCorrection message, PeerHandle sender) {
            NetEntity entity = GetEntity(message.EntityId);

            if (entity == null || !IsOwned(entity)) {
                return;
            }

            if (TryAcknowledgeCleanCorrection(entity, in message)) {
                CorrectionsSkipped++;
                return;
            }

            CorrectionsApplied++;
            PawnState resolved = ResolveCorrection(entity, in message);
            settledByEntity[entity.Id] = new SettledPrediction(message.AcknowledgedInputSequence, message.State);
            entity.ApplyState(resolved, message.ServerTick);
            OnAuthorityCorrected?.Invoke(entity, resolved);
        }

        /// <summary>
        /// Settles a correction that agrees with what was predicted for its acknowledged sequence.
        /// </summary>
        /// <remarks>
        /// An idle owner keeps receiving corrections that acknowledge the same sequence long after the
        /// pending step for it was dropped, so the last settled sequence and its state are remembered
        /// per entity — otherwise every correction after the first would look unmatchable and force a
        /// pointless rewind.
        /// </remarks>
        /// <returns>True when the correction matched and has been fully handled.</returns>
        private bool TryAcknowledgeCleanCorrection(NetEntity entity, in AuthorityCorrection message) {
            if (entity.Authority != AuthorityMode.Server) {
                return false;
            }

            PredictionBuffer buffer = GetPredictionBuffer(entity.Id);

            if (buffer == null) {
                return false;
            }

            if (buffer.TryGetPredictedState(message.AcknowledgedInputSequence, out PawnState predicted)) {
                PawnState quantized = predicted.Quantized();

                if (!CorrectionMatchesPrediction(in quantized, message.State)) {
                    return false;
                }

                buffer.Acknowledge(message.AcknowledgedInputSequence);
                settledByEntity[entity.Id] = new SettledPrediction(message.AcknowledgedInputSequence, in quantized);
                return true;
            }

            if (!settledByEntity.TryGetValue(entity.Id, out SettledPrediction settled)) {
                return false;
            }

            if (settled.Sequence != message.AcknowledgedInputSequence) {
                return false;
            }

            return CorrectionMatchesPrediction(settled.State, message.State);
        }

        /// <summary>
        /// Whether an authoritative state and the local prediction for the same sequence agree closely
        /// enough that correcting would only re-derive what is already held. The prediction is compared
        /// after wire quantization so rounding the channel itself introduced never reads as divergence.
        /// </summary>
        private static bool CorrectionMatchesPrediction(in PawnState predicted, in PawnState authoritative) {
            if ((predicted.Flags & CorrectionFlagsMask) != (authoritative.Flags & CorrectionFlagsMask)) {
                return false;
            }

            if (MathF.Abs(predicted.Position.X - authoritative.Position.X) > CorrectionHorizontalEpsilon) {
                return false;
            }

            if (MathF.Abs(predicted.Position.Z - authoritative.Position.Z) > CorrectionHorizontalEpsilon) {
                return false;
            }

            if (MathF.Abs(predicted.Position.Y - authoritative.Position.Y) > CorrectionVerticalEpsilon) {
                return false;
            }

            return MathF.Abs(predicted.Velocity.X - authoritative.Velocity.X) <= CorrectionVelocityEpsilon
                && MathF.Abs(predicted.Velocity.Y - authoritative.Velocity.Y) <= CorrectionVelocityEpsilon
                && MathF.Abs(predicted.Velocity.Z - authoritative.Velocity.Z) <= CorrectionVelocityEpsilon;
        }

        /// <summary>
        /// Turns a correction into the state the pawn should now hold: a rewind-and-replay for a predicted
        /// pawn, a plain snap for an owner-authoritative one that has just been told it was wrong.
        /// </summary>
        private PawnState ResolveCorrection(NetEntity entity, in AuthorityCorrection message) {
            if (entity.Authority != AuthorityMode.Server) {
                predictionBuffers.Remove(entity.Id);
                return message.State;
            }

            MovementProfile profile = config.GetMovementProfile(entity.PrefabId);

            if (profile == null) {
                return message.State;
            }

            return ResolvePredictionBuffer(entity.Id).Reconcile(
                message.AcknowledgedInputSequence,
                message.State,
                profile,
                groundProvider,
                config.ServerTickInterval);
        }

        /// <summary>
        /// Takes a state off the wire for an entity. Predicted pawns are skipped on purpose — their
        /// authority arrives as corrections, and a snapshot would drag them back into the past.
        /// </summary>
        private void ApplyReceivedState(uint entityId, in PawnState state, uint serverTick) {
            NetEntity entity = GetEntity(entityId);

            if (entity == null || IsPredicted(entity)) {
                return;
            }

            entity.ApplyState(state, serverTick);
            ResolveInterpolator(entityId).Push(serverTick, in state);
        }

        /// <summary>
        /// Registers an entity described by a spawn or a keyframe. A keyframe repeating one already known
        /// updates it in place rather than announcing it twice, which is what makes the same message serve
        /// both the periodic broadcast and a rejoining client's first sight of the world.
        /// </summary>
        private void AdoptEntity(
            uint entityId,
            ushort prefabId,
            int ownerPeerId,
            AuthorityMode authority,
            in PawnState state,
            uint serverTick) {
            if (entitiesById.TryGetValue(entityId, out NetEntity existing)) {
                existing.OwnerPeerId = ownerPeerId;
                ApplyReceivedState(entityId, in state, serverTick);
                return;
            }

            var entity = new NetEntity(entityId, prefabId, ownerPeerId, authority, in state);
            entitiesById.Add(entityId, entity);
            entities.Add(entity);

            if (IsPredicted(entity)) {
                predictionBuffers[entityId] = new PredictionBuffer();
                // Corrections that arrive before the first input acknowledge sequence zero; seeding the
                // settled record with the spawn state lets them match instead of forcing a rewind.
                settledByEntity[entityId] = new SettledPrediction(0u, state.Quantized());
            }
            else {
                ResolveInterpolator(entityId).Push(serverTick, in state);
            }

            OnEntitySpawned?.Invoke(entity);
        }

        private StateInterpolator ResolveInterpolator(uint entityId) {
            if (interpolators.TryGetValue(entityId, out StateInterpolator interpolator)) {
                return interpolator;
            }

            interpolator = new StateInterpolator(config);
            interpolators.Add(entityId, interpolator);
            return interpolator;
        }

        private PredictionBuffer ResolvePredictionBuffer(uint entityId) {
            if (predictionBuffers.TryGetValue(entityId, out PredictionBuffer buffer)) {
                return buffer;
            }

            buffer = new PredictionBuffer();
            predictionBuffers.Add(entityId, buffer);
            return buffer;
        }

        private void RememberPendingEvent(uint sequence) {
            if (pendingEventOrder.Count >= MaxPendingEventSequences) {
                pendingEventSequences.Remove(pendingEventOrder.Dequeue());
            }

            pendingEventSequences.Add(sequence);
            pendingEventOrder.Enqueue(sequence);
        }

        private void RemoveFromList(uint entityId) {
            for (int entityIndex = 0; entityIndex < entities.Count; entityIndex++) {
                if (entities[entityIndex].Id != entityId) {
                    continue;
                }

                entities.RemoveAt(entityIndex);
                return;
            }
        }
    }
}
