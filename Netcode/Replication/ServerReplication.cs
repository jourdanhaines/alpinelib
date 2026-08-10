using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication.Messages;
using AlpineLib.Netcode.Transport;

namespace AlpineLib.Netcode.Replication {
    /// <summary>
    /// The authoritative world of one session: it owns the entities, runs the motor for the ones it
    /// simulates, judges the ones it does not, and publishes the result to that session's peers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One per session, not one per server.</b> Every broadcast goes through
    /// <see cref="PeerSource"/> — the session's own member list — so a process hosting several igloos
    /// keeps their worlds apart without any of this code knowing that sessions exist. That is also why
    /// nothing here reads <c>NetServer.Peers</c>, which would silently leak one igloo's pawns into
    /// another's.
    /// </para>
    /// <para>
    /// <b>Router registration is opt-in.</b> A <see cref="MessageRouter"/> allows one handler per id and
    /// throws on a second, so several sessions sharing one <see cref="NetServer"/> cannot each claim
    /// <c>InputCommand</c>. A single-session host calls <see cref="AttachToRouter"/> and gets the wiring
    /// for free; a multi-session server leaves it alone, registers once itself, resolves which session a
    /// peer belongs to and calls <see cref="HandleInputCommand"/> and friends directly.
    /// </para>
    /// <para>
    /// <b>Threading.</b> Everything here runs on the thread that calls <see cref="Tick"/> — the Unity
    /// main thread on a listen host, the fixed-step loop thread on the dedicated server. Message handlers
    /// are invoked from inside the transport poll on that same thread, so no locking is needed and none
    /// should be added.
    /// </para>
    /// </remarks>
    public sealed class ServerReplication {
        /// <summary>How often the full reliable keyframe goes out, in seconds.</summary>
        public const double KeyframeIntervalSeconds = 1.0;

        /// <summary>
        /// Inputs buffered per pawn before the oldest are dropped: four hundred milliseconds at the
        /// default send rate. A client that runs further ahead than this is either lagging badly or
        /// trying to bank up a burst of movement, and neither deserves unbounded memory.
        /// </summary>
        public const int MaxQueuedInputsPerEntity = 12;

        /// <summary>Ceiling on motor steps run in one <see cref="Tick"/>, so a stall is not paid back at once.</summary>
        public const int MaxCatchUpSteps = 8;

        private readonly NetServer server;
        private readonly Func<IReadOnlyList<PeerHandle>> peerSource;
        private readonly MovementValidator validator;
        private readonly IGroundProvider groundProvider;
        private readonly NetConfig config;
        private readonly ServerEntityRegistry registry = new ServerEntityRegistry();
        private readonly Dictionary<uint, Queue<PawnInput>> inputQueues = new Dictionary<uint, Queue<PawnInput>>();
        private readonly List<EntitySnapshotRecord> snapshotRecords = new List<EntitySnapshotRecord>();
        private readonly List<EntityKeyframeRecord> keyframeRecords = new List<EntityKeyframeRecord>();
        private readonly List<uint> despawnScratch = new List<uint>();

        private double snapshotAccumulatorSeconds;
        private double keyframeAccumulatorSeconds;
        private uint currentTick;
        private uint lastSimulatedTick;
        private uint lastSnapshotTick;
        private bool hasSimulated;
        private bool isAttachedToRouter;

        /// <summary>Creates a replication world over flat ground at y = 0.</summary>
        public ServerReplication(NetServer server, Func<IReadOnlyList<PeerHandle>> peerSource, MovementValidator validator)
            : this(server, peerSource, validator, new FlatGroundProvider()) { }

        /// <summary>Creates a replication world with an explicit ground seam.</summary>
        public ServerReplication(
            NetServer server,
            Func<IReadOnlyList<PeerHandle>> peerSource,
            MovementValidator validator,
            IGroundProvider groundProvider) {
            this.server = server ?? throw new ArgumentNullException(nameof(server));
            this.peerSource = peerSource ?? throw new ArgumentNullException(nameof(peerSource));
            this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
            this.groundProvider = groundProvider ?? throw new ArgumentNullException(nameof(groundProvider));
            config = validator.Config;
        }

        /// <summary>An entity was created here. Raised after the spawn has gone out.</summary>
        public event Action<NetEntity> OnEntitySpawned;

        /// <summary>An entity was destroyed here. Raised after the despawn has gone out.</summary>
        public event Action<uint> OnEntityDespawned;

        /// <summary>
        /// A client reported something impossible for a pawn it owns. Carries the entity and the verdict,
        /// so a host can surface it, log it or act on repeats.
        /// </summary>
        public event Action<NetEntity, MovementVerdict> OnMovementViolation;

        /// <summary>The live entity set.</summary>
        public ServerEntityRegistry Entities => registry;

        /// <summary>Where the floor is for this session's simulation.</summary>
        public IGroundProvider GroundProvider => groundProvider;

        /// <summary>Who this world broadcasts to — the session's members, and nobody else.</summary>
        public IReadOnlyList<PeerHandle> Peers => peerSource() ?? Array.Empty<PeerHandle>();

        /// <summary>Tick of the most recent snapshot broadcast.</summary>
        public uint LastSnapshotTick => lastSnapshotTick;

        /// <summary>True while this instance owns the replication ids on the server's router.</summary>
        public bool IsAttachedToRouter => isAttachedToRouter;

        /// <summary>
        /// Claims the client-to-server replication ids on the server's router. Valid only when this is
        /// the sole session on that server; see the note on the type.
        /// </summary>
        public void AttachToRouter() {
            if (isAttachedToRouter) {
                return;
            }

            server.Router.Register<InputCommand>(ReplicationMessageIds.InputCommand, HandleInputCommand);
            server.Router.Register<OwnerPawnUpdate>(ReplicationMessageIds.OwnerPawnUpdate, HandleOwnerPawnUpdate);
            server.Router.Register<EntityEvent>(ReplicationMessageIds.EntityEvent, HandleEntityEvent);
            isAttachedToRouter = true;
        }

        /// <summary>Releases the ids claimed by <see cref="AttachToRouter"/>.</summary>
        public void DetachFromRouter() {
            if (!isAttachedToRouter) {
                return;
            }

            server.Router.Unregister(ReplicationMessageIds.InputCommand);
            server.Router.Unregister(ReplicationMessageIds.OwnerPawnUpdate);
            server.Router.Unregister(ReplicationMessageIds.EntityEvent);
            isAttachedToRouter = false;
        }

        /// <summary>Creates an entity and tells the session about it.</summary>
        public NetEntity SpawnEntity(ushort prefabId, int ownerPeerId, AuthorityMode authority, in PawnState initialState) {
            NetEntity entity = registry.Create(prefabId, ownerPeerId, authority, in initialState);

            var message = new SpawnEntity(entity.Id, entity.PrefabId, entity.OwnerPeerId, entity.Authority, in initialState);
            server.SendToMany(Peers,ReplicationMessageIds.SpawnEntity, in message, DeliveryClass.ReliableOrdered);

            OnEntitySpawned?.Invoke(entity);
            return entity;
        }

        /// <summary>Destroys an entity and tells the session about it.</summary>
        public void DespawnEntity(uint entityId) {
            if (!registry.Remove(entityId)) {
                return;
            }

            inputQueues.Remove(entityId);
            BroadcastDespawn(entityId);
        }

        /// <summary>Destroys every entity a peer owns — what a player leaving for good triggers.</summary>
        public void DespawnOwnedBy(int ownerPeerId) {
            despawnScratch.Clear();
            registry.RemoveOwnedBy(ownerPeerId, despawnScratch);

            for (int idIndex = 0; idIndex < despawnScratch.Count; idIndex++) {
                uint entityId = despawnScratch[idIndex];
                inputQueues.Remove(entityId);
                BroadcastDespawn(entityId);
            }
        }

        /// <summary>Raises a server-originated event on an entity. Sequence zero marks it as not an echo.</summary>
        public void SendEntityEvent(uint entityId, byte eventId, byte argument) {
            var message = new EntityEvent(entityId, eventId, argument, EntityEvent.ServerSequence);
            server.SendToMany(Peers,ReplicationMessageIds.EntityEvent, in message, DeliveryClass.ReliableOrdered);
        }

        /// <summary>
        /// A peer is now part of this session. Sends it the world as one reliable keyframe — which is
        /// also the rejoin path, since a keyframe record carries everything a spawn would have.
        /// </summary>
        public void OnPeerJoined(PeerHandle peer) {
            SendKeyframeTo(peer);
        }

        /// <summary>A peer left. Its pawns stay until the session decides their fate, so this only drops input.</summary>
        public void OnPeerLeft(PeerHandle peer) {
            IReadOnlyList<NetEntity> entities = registry.Entities;

            for (int entityIndex = 0; entityIndex < entities.Count; entityIndex++) {
                NetEntity entity = entities[entityIndex];

                if (entity.OwnerPeerId == peer.Id) {
                    inputQueues.Remove(entity.Id);
                }
            }
        }

        /// <summary>
        /// One pump of the world: simulate every tick that has elapsed, then publish on the snapshot and
        /// keyframe cadences.
        /// </summary>
        /// <param name="serverTick">The authoritative tick counter, from <c>NetServer.Tick</c>.</param>
        /// <param name="deltaSeconds">Wall time since the previous call, which drives the send cadences.</param>
        public void Tick(uint serverTick, float deltaSeconds) {
            currentTick = serverTick;
            SimulateElapsedTicks(serverTick);

            snapshotAccumulatorSeconds += deltaSeconds;
            keyframeAccumulatorSeconds += deltaSeconds;

            double snapshotInterval = config.SnapshotInterval;
            if (snapshotAccumulatorSeconds >= snapshotInterval) {
                snapshotAccumulatorSeconds -= snapshotInterval;
                BuildAndBroadcastSnapshot(serverTick);
            }

            if (keyframeAccumulatorSeconds >= KeyframeIntervalSeconds) {
                keyframeAccumulatorSeconds = 0.0;
                BuildAndBroadcastKeyframe(serverTick);
            }
        }

        /// <summary>
        /// Sends the entities that changed since the last snapshot, and corrects every owner whose pawn
        /// the server is simulating for them.
        /// </summary>
        public void BuildAndBroadcastSnapshot(uint tick) {
            snapshotRecords.Clear();
            IReadOnlyList<NetEntity> entities = registry.Entities;

            for (int entityIndex = 0; entityIndex < entities.Count; entityIndex++) {
                NetEntity entity = entities[entityIndex];

                if (!entity.IsDirtySince(lastSnapshotTick)) {
                    continue;
                }

                snapshotRecords.Add(new EntitySnapshotRecord(entity.Id, entity.State));
            }

            lastSnapshotTick = tick;

            if (snapshotRecords.Count > 0) {
                var message = new Snapshot(tick, snapshotRecords);
                server.SendToMany(Peers,ReplicationMessageIds.Snapshot, in message, DeliveryClass.UnreliableSequenced);
            }

            SendOwnerCorrections(tick);
        }

        /// <summary>Sends every entity in full to the whole session, reliably.</summary>
        public void BuildAndBroadcastKeyframe(uint tick) {
            BuildKeyframeRecords();

            var message = new SnapshotKeyframe(tick, keyframeRecords);
            server.SendToMany(Peers,ReplicationMessageIds.SnapshotKeyframe, in message, DeliveryClass.ReliableOrdered);
        }

        /// <summary>Sends every entity in full to one peer — the join, rejoin and desync-repair path.</summary>
        public void SendKeyframeTo(PeerHandle peer) {
            if (!peer.IsValid) {
                return;
            }

            BuildKeyframeRecords();

            var message = new SnapshotKeyframe(currentTick, keyframeRecords);
            server.Send(peer, ReplicationMessageIds.SnapshotKeyframe, in message, DeliveryClass.ReliableOrdered);
        }

        /// <summary>
        /// Takes an owner's intent for a server-authoritative pawn. Queued rather than applied: the motor
        /// runs on the fixed tick, and applying input the moment a packet lands would make a pawn's speed
        /// depend on how its owner's packets happened to line up with the loop.
        /// </summary>
        public void HandleInputCommand(in InputCommand message, PeerHandle sender) {
            if (!TryResolveOwnedEntity(message.EntityId, sender, AuthorityMode.Server, out NetEntity entity)) {
                return;
            }

            Queue<PawnInput> queue = ResolveInputQueue(entity.Id);

            if (queue.Count >= MaxQueuedInputsPerEntity) {
                queue.Dequeue();
            }

            queue.Enqueue(message.Input);
        }

        /// <summary>
        /// Takes an owner's claimed state for an owner-authoritative pawn, judges it, and corrects the
        /// owner when the claim did not survive judgement.
        /// </summary>
        public void HandleOwnerPawnUpdate(in OwnerPawnUpdate message, PeerHandle sender) {
            if (!TryResolveOwnedEntity(message.EntityId, sender, AuthorityMode.OwnerClient, out NetEntity entity)) {
                return;
            }

            float deltaSeconds = ElapsedSince(entity.LastDirtyTick);
            MovementVerdict verdict = validator.Validate(entity.PrefabId, entity.State, message.State, deltaSeconds);

            entity.ApplyState(verdict.ResolvedState, currentTick);
            entity.LastAcknowledgedInputTick = message.ClientTick;

            if (!verdict.RequiresCorrection) {
                return;
            }

            OnMovementViolation?.Invoke(entity, verdict);
            SendCorrection(entity, sender);
        }

        /// <summary>
        /// Takes a client-raised event and echoes it to the whole session, the sender included, so every
        /// peer sees one server-chosen order. The sender's own sequence rides along untouched — that is
        /// what lets it recognise its echo and not play the event twice.
        /// </summary>
        public void HandleEntityEvent(in EntityEvent message, PeerHandle sender) {
            if (!registry.TryGet(message.EntityId, out NetEntity entity) || entity.OwnerPeerId != sender.Id) {
                return;
            }

            server.SendToMany(Peers,ReplicationMessageIds.EntityEvent, in message, DeliveryClass.ReliableOrdered);
        }

        /// <summary>Runs the motor once for every authoritative tick that has passed since the last call.</summary>
        private void SimulateElapsedTicks(uint serverTick) {
            if (!hasSimulated) {
                // Anchor on the first pump rather than simulating from tick zero: a session that opens on a
                // server which has been up for an hour would otherwise try to pay back an hour of ticks.
                hasSimulated = true;
                lastSimulatedTick = serverTick;
                return;
            }

            uint elapsed = serverTick - lastSimulatedTick;

            if (elapsed > MaxCatchUpSteps) {
                elapsed = MaxCatchUpSteps;
            }

            for (uint step = 0; step < elapsed; step++) {
                StepServerAuthorityEntities(serverTick);
            }

            lastSimulatedTick = serverTick;
        }

        /// <summary>One fixed motor step for every entity the server simulates on its owner's behalf.</summary>
        private void StepServerAuthorityEntities(uint tick) {
            IReadOnlyList<NetEntity> entities = registry.Entities;
            float interval = config.ServerTickInterval;

            for (int entityIndex = 0; entityIndex < entities.Count; entityIndex++) {
                NetEntity entity = entities[entityIndex];

                if (entity.Authority != AuthorityMode.Server) {
                    continue;
                }

                MovementProfile profile = config.GetMovementProfile(entity.PrefabId);

                if (profile == null) {
                    continue;
                }

                PawnInput input = NextInputFor(entity);
                PawnState next = PawnMotor.Step(in input, entity.State, profile, groundProvider, interval);
                entity.ApplyState(next, tick);
            }
        }

        /// <summary>
        /// Pulls the next queued input, or repeats the last one without its jump when the stream has a
        /// gap. Only a real input advances the acknowledged tick — inventing one would tell the owner's
        /// prediction buffer to discard work the server has never seen.
        /// </summary>
        private PawnInput NextInputFor(NetEntity entity) {
            if (inputQueues.TryGetValue(entity.Id, out Queue<PawnInput> queue) && queue.Count > 0) {
                PawnInput input = queue.Dequeue();
                entity.LastInput = input;
                entity.LastAcknowledgedInputTick = input.Tick;
                return input;
            }

            PawnInput repeated = entity.LastInput.WithoutJump();
            entity.LastInput = repeated;
            return repeated;
        }

        /// <summary>Tells each owner where the server has actually put the pawn it is predicting.</summary>
        private void SendOwnerCorrections(uint tick) {
            IReadOnlyList<NetEntity> entities = registry.Entities;

            for (int entityIndex = 0; entityIndex < entities.Count; entityIndex++) {
                NetEntity entity = entities[entityIndex];

                if (entity.Authority != AuthorityMode.Server || entity.OwnerPeerId < 0) {
                    continue;
                }

                SendCorrection(entity, new PeerHandle(entity.OwnerPeerId));
            }
        }

        private void SendCorrection(NetEntity entity, PeerHandle owner) {
            if (!owner.IsValid) {
                return;
            }

            var message = new AuthorityCorrection(entity.Id, currentTick, entity.LastAcknowledgedInputTick, entity.State);
            server.Send(owner, ReplicationMessageIds.AuthorityCorrection, in message, DeliveryClass.UnreliableSequenced);
        }

        private void BuildKeyframeRecords() {
            keyframeRecords.Clear();
            IReadOnlyList<NetEntity> entities = registry.Entities;

            for (int entityIndex = 0; entityIndex < entities.Count; entityIndex++) {
                NetEntity entity = entities[entityIndex];
                keyframeRecords.Add(new EntityKeyframeRecord(
                    entity.Id,
                    entity.PrefabId,
                    entity.OwnerPeerId,
                    entity.Authority,
                    entity.State));
            }
        }

        private void BroadcastDespawn(uint entityId) {
            var message = new DespawnEntity(entityId);
            server.SendToMany(Peers,ReplicationMessageIds.DespawnEntity, in message, DeliveryClass.ReliableOrdered);
            OnEntityDespawned?.Invoke(entityId);
        }

        /// <summary>
        /// Finds an entity, but only if this sender owns it and it is in the expected authority mode.
        /// Every client-to-server replication message goes through here: without it, one peer could drive
        /// another's pawn simply by naming its id.
        /// </summary>
        private bool TryResolveOwnedEntity(uint entityId, PeerHandle sender, AuthorityMode expected, out NetEntity entity) {
            if (!registry.TryGet(entityId, out entity)) {
                return false;
            }

            if (entity.OwnerPeerId != sender.Id || entity.Authority != expected) {
                entity = null;
                return false;
            }

            return true;
        }

        private Queue<PawnInput> ResolveInputQueue(uint entityId) {
            if (inputQueues.TryGetValue(entityId, out Queue<PawnInput> queue)) {
                return queue;
            }

            queue = new Queue<PawnInput>(MaxQueuedInputsPerEntity);
            inputQueues.Add(entityId, queue);
            return queue;
        }

        /// <summary>
        /// Seconds between a past tick and now, floored at one tick. The floor matters: two updates
        /// landing inside one tick would otherwise divide by zero elapsed time and read as infinite speed.
        /// </summary>
        private float ElapsedSince(uint tick) {
            uint elapsedTicks = currentTick > tick ? currentTick - tick : 1u;
            return elapsedTicks * config.ServerTickInterval;
        }
    }
}
