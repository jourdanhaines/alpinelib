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

        private readonly NetClient client;
        private readonly NetConfig config;
        private readonly Dictionary<uint, NetEntity> entitiesById = new Dictionary<uint, NetEntity>();
        private readonly List<NetEntity> entities = new List<NetEntity>();
        private readonly Dictionary<uint, StateInterpolator> interpolators = new Dictionary<uint, StateInterpolator>();
        private readonly Dictionary<uint, PredictionBuffer> predictionBuffers = new Dictionary<uint, PredictionBuffer>();
        private readonly HashSet<uint> pendingEventSequences = new HashSet<uint>();
        private readonly Queue<uint> pendingEventOrder = new Queue<uint>();

        private IGroundProvider groundProvider;
        private uint nextEventSequence = 1u;
        private bool disposed;

        /// <summary>Creates a client world predicting over flat ground at y = 0.</summary>
        public ClientReplication(NetClient client, NetConfig config)
            : this(client, config, new FlatGroundProvider()) { }

        /// <summary>Creates a client world with an explicit ground seam for prediction.</summary>
        public ClientReplication(NetClient client, NetConfig config, IGroundProvider groundProvider) {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.groundProvider = groundProvider ?? throw new ArgumentNullException(nameof(groundProvider));

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
        /// Sends one tick of intent for an owned, server-authoritative pawn and immediately applies what
        /// the shared motor says it will do, so the pawn responds on this frame rather than in a round
        /// trip's time.
        /// </summary>
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

            PawnState predicted = PawnMotor.Step(in input, entity.State, profile, groundProvider, config.ServerTickInterval);
            entity.ApplyState(predicted, input.Tick);
            ResolvePredictionBuffer(entityId).Record(in input, in predicted);

            var message = new InputCommand(entityId, in input);
            client.Send(ReplicationMessageIds.InputCommand, in message, DeliveryClass.UnreliableSequenced);
            return predicted;
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
        /// The pose to render for a remote entity right now, taken from its interpolator at the client's
        /// current estimate of the server clock.
        /// </summary>
        public bool SampleRemote(uint entityId, out PawnState state) {
            StateInterpolator interpolator = GetInterpolator(entityId);

            if (interpolator == null) {
                state = default;
                return false;
            }

            return interpolator.Sample(client.Clock.EstimatedServerSeconds, out state);
        }

        /// <summary>Drops the whole world. Used when leaving a session or before rebuilding on rejoin.</summary>
        public void Clear() {
            entitiesById.Clear();
            entities.Clear();
            interpolators.Clear();
            predictionBuffers.Clear();
            pendingEventSequences.Clear();
            pendingEventOrder.Clear();
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

        private void HandleAuthorityCorrection(in AuthorityCorrection message, PeerHandle sender) {
            NetEntity entity = GetEntity(message.EntityId);

            if (entity == null || !IsOwned(entity)) {
                return;
            }

            PawnState resolved = ResolveCorrection(entity, in message);
            entity.ApplyState(resolved, message.ServerTick);
            OnAuthorityCorrected?.Invoke(entity, resolved);
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
                message.AcknowledgedInputTick,
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
