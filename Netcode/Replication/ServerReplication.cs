using System;
using System.Collections.Generic;
using AlpineLib.Netcode.Collision;
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
    /// <see cref="PeerSource"/> — the session's own member list — so a process hosting several lobbies
    /// keeps their worlds apart without any of this code knowing that sessions exist. That is also why
    /// nothing here reads <c>NetServer.Peers</c>, which would silently leak one lobby's pawns into
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
    /// <b>Movers are entities here too.</b> A scene's moving platforms are not a separate broadcast
    /// channel: <see cref="UseWorld"/> spawns one <see cref="EntityKind.Mover"/> entity per mover in the
    /// loaded world, and <see cref="StepMovers"/> writes each one's authored pose at the top of every
    /// tick, before any pawn is simulated. Clients therefore learn about platforms through the same
    /// spawn, snapshot and keyframe path as everyone else, and a client that cannot resolve the mover's
    /// path — an old build, a scene it has not loaded — still sees the platform move because the state is
    /// on the wire regardless.
    /// </para>
    /// <para>
    /// <b>The simulated pawn is a world-space pawn.</b> Everything on the server-authority stepping path
    /// — <see cref="PawnMotor"/>, the collision world it steps against, the mover poses, the snapshot
    /// records built from the result — is expressed in world metres. A carrier-relative state therefore
    /// has no meaning here, and <see cref="SpawnEntity(ushort, int, AuthorityMode, EntityKind, ushort, in PawnState)"/>
    /// refuses one outright rather than letting the motor step a deck-local position against world
    /// geometry and produce a pawn that walks through the floor. Carrier frames belong to
    /// <see cref="AuthorityMode.OwnerClient"/> pawns, whose states this world stores and validates but
    /// never simulates.
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

        /// <summary>
        /// Longest interval one owner update may be measured over, in seconds; see
        /// <see cref="MeasuredIntervalSince"/> for why an uncapped one lets silence buy distance.
        /// </summary>
        public const float MaxMeasuredIntervalSeconds = 1f;

        /// <summary>Starved ticks during which the last intent is repeated at full strength.</summary>
        public const int StarvationHoldTicks = 2;

        /// <summary>Per-tick multiplier applied to a starved pawn's move vector past the hold.</summary>
        public const float StarvationDecayFactor = 0.5f;

        /// <summary>
        /// Owner id given to entities no player owns — the movers. Matches <see cref="PeerHandle.None"/>,
        /// so every "does anyone own this" test in this file and on the client agrees without a special
        /// case for platforms.
        /// </summary>
        public const int UnownedPeerId = -1;

        private readonly NetServer server;
        private readonly Func<IReadOnlyList<PeerHandle>> peerSource;
        private readonly MovementValidator validator;
        private readonly NetConfig config;
        private readonly ServerEntityRegistry registry = new ServerEntityRegistry();
        private readonly Dictionary<uint, Queue<PawnInput>> inputQueues = new Dictionary<uint, Queue<PawnInput>>();
        private readonly Dictionary<uint, int> moverIndexByEntityId = new Dictionary<uint, int>();
        private readonly List<EntitySnapshotRecord> snapshotRecords = new List<EntitySnapshotRecord>();
        private readonly List<EntityKeyframeRecord> keyframeRecords = new List<EntityKeyframeRecord>();
        private readonly List<uint> despawnScratch = new List<uint>();

        private CollisionWorld world;
        private double snapshotAccumulatorSeconds;
        private double keyframeAccumulatorSeconds;
        private uint currentTick;
        private uint lastSimulatedTick;
        private uint lastSnapshotTick;
        private bool hasSimulated;
        private bool isAttachedToRouter;

        /// <summary>
        /// Creates a replication world over an endless floor at y = 0 — what a session with no exported
        /// geometry for its scene falls back to.
        /// </summary>
        public ServerReplication(NetServer server, Func<IReadOnlyList<PeerHandle>> peerSource, MovementValidator validator)
            : this(server, peerSource, validator, CollisionWorld.Flat()) { }

        /// <summary>Creates a replication world simulating against a scene's exported collision geometry.</summary>
        public ServerReplication(
            NetServer server,
            Func<IReadOnlyList<PeerHandle>> peerSource,
            MovementValidator validator,
            CollisionWorld world) {
            this.server = server ?? throw new ArgumentNullException(nameof(server));
            this.peerSource = peerSource ?? throw new ArgumentNullException(nameof(peerSource));
            this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
            this.world = world ?? throw new ArgumentNullException(nameof(world));
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

        /// <summary>
        /// The scene collision this session simulates against. Every owning client predicts through a
        /// world built from the same exported bytes, so a mismatch here is a correction on every tick.
        /// Swapped by <see cref="UseWorld"/> when the session changes scene.
        /// </summary>
        public CollisionWorld World => world;

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

        /// <summary>Creates a pawn and tells the session about it.</summary>
        public NetEntity SpawnEntity(ushort prefabId, int ownerPeerId, AuthorityMode authority, in PawnState initialState) {
            return SpawnEntity(prefabId, ownerPeerId, authority, EntityKind.Pawn, 0, in initialState);
        }

        /// <summary>Creates an entity of any kind and tells the session about it.</summary>
        /// <remarks>
        /// The kind is not cosmetic. Only a pawn is seeded with a resting input and only a pawn is ever
        /// stepped by the motor; a mover spawned through here is posed from its path instead, and giving
        /// it an input would put a platform on the starvation-decay path the moment its owner — nobody —
        /// failed to send one.
        /// </remarks>
        public NetEntity SpawnEntity(
            ushort prefabId,
            int ownerPeerId,
            AuthorityMode authority,
            EntityKind kind,
            ushort auxId,
            in PawnState initialState) {
            if (authority == AuthorityMode.Server && initialState.IsCarrierRelative) {
                throw new ArgumentException(
                    "A server-simulated entity must spawn in world space; carrier-relative states are only meaningful for owner-simulated pawns.",
                    nameof(initialState));
            }

            NetEntity entity = registry.Create(prefabId, ownerPeerId, authority, kind, auxId, in initialState);

            if (kind == EntityKind.Pawn) {
                // Until the owner's first input arrives the motor runs on LastInput, and a default one would
                // quietly rewrite the spawn state's gait and crouch on the first starved tick — a flags
                // change the owner never asked for and the first correction would have to repair.
                entity.LastInput = new PawnInput(0u, System.Numerics.Vector2.Zero, initialState.Locomotion, false, initialState.IsCrouching);
            }

            var message = new SpawnEntity(
                entity.Id,
                entity.PrefabId,
                entity.OwnerPeerId,
                entity.Authority,
                entity.Kind,
                entity.AuxId,
                in initialState);
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
            moverIndexByEntityId.Remove(entityId);
            BroadcastDespawn(entityId);
        }

        /// <summary>
        /// Points this session at a different scene's collision, replacing whatever movers the old world
        /// contributed with the new one's.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the scene-change path: a session moving from the lobby to a match calls it once, and
        /// everything downstream follows. Pawns are deliberately left alone — the same pawns walk into
        /// the new scene, and their positions are the session's problem, not the geometry's — while every
        /// mover entity is despawned and respawned, because a mover's identity is its row in a particular
        /// scene's export and carrying one across a scene change would leave a platform following a path
        /// that no longer exists.
        /// </para>
        /// <para>
        /// The new movers are spawned already posed at the current tick rather than at their first
        /// waypoint. A platform on a two-minute cycle would otherwise snap from wherever the phase puts it
        /// back to the start of its route on the first tick after the swap, which every client would see as
        /// a teleport before the interpolator had anything to smooth.
        /// </para>
        /// </remarks>
        public void UseWorld(CollisionWorld nextWorld) {
            if (nextWorld == null) {
                throw new ArgumentNullException(nameof(nextWorld));
            }

            DespawnMovers();
            world = nextWorld;
            SpawnMovers();
        }

        /// <summary>
        /// Writes every mover's authored pose for one tick. Called at the top of each simulated tick,
        /// before the pawns are stepped.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Order matters and is the reason this is its own call rather than a branch inside the pawn loop.
        /// A pawn standing on a platform reads that platform's travel for this tick out of the collision
        /// world, so the platform's state must already describe the tick the pawn is about to be simulated
        /// through. Both ends evaluate the same pure path from the same tick, so this is bookkeeping for
        /// the wire rather than a second simulation — but bookkeeping that is one tick stale is a rider
        /// sliding backwards on every platform in the scene.
        /// </para>
        /// <para>
        /// The velocity written alongside the position is the tick's travel divided by the tick interval.
        /// Nothing on the server integrates it; it exists so that a client interpolating a platform it
        /// cannot resolve a path for still has a direction and a speed to extrapolate from between
        /// snapshots.
        /// </para>
        /// </remarks>
        public void StepMovers(uint tick) {
            IReadOnlyList<NetEntity> entities = registry.Entities;
            float interval = config.ServerTickInterval;

            for (int entityIndex = 0; entityIndex < entities.Count; entityIndex++) {
                NetEntity entity = entities[entityIndex];

                if (entity.Kind != EntityKind.Mover || !moverIndexByEntityId.TryGetValue(entity.Id, out int moverIndex)) {
                    continue;
                }

                entity.ApplyState(MoverStateAt(moverIndex, tick, interval), tick);
            }
        }

        /// <summary>
        /// Removes every mover entity the previous world contributed, telling the session about each.
        /// </summary>
        /// <remarks>
        /// The ids are collected into a list of their own rather than the shared despawn scratch, because
        /// each removal raises <see cref="OnEntityDespawned"/> and a handler is entitled to despawn
        /// something else while this is still walking its own list.
        /// </remarks>
        private void DespawnMovers() {
            var moverIds = new List<uint>(moverIndexByEntityId.Count);
            IReadOnlyList<NetEntity> entities = registry.Entities;

            for (int entityIndex = 0; entityIndex < entities.Count; entityIndex++) {
                NetEntity entity = entities[entityIndex];

                if (entity.Kind == EntityKind.Mover) {
                    moverIds.Add(entity.Id);
                }
            }

            for (int idIndex = 0; idIndex < moverIds.Count; idIndex++) {
                DespawnEntity(moverIds[idIndex]);
            }

            moverIndexByEntityId.Clear();
        }

        /// <summary>Spawns one unowned, server-authoritative entity for each mover in the current world.</summary>
        private void SpawnMovers() {
            IReadOnlyList<MoverDefinition> movers = world.Movers;
            float interval = config.ServerTickInterval;

            for (int moverIndex = 0; moverIndex < movers.Count; moverIndex++) {
                MoverDefinition mover = movers[moverIndex];

                if (mover == null) {
                    continue;
                }

                PawnState initialState = MoverStateAt(moverIndex, currentTick, interval);
                NetEntity entity = SpawnEntity(
                    mover.PrefabId,
                    UnownedPeerId,
                    AuthorityMode.Server,
                    EntityKind.Mover,
                    mover.MoverId,
                    in initialState);
                moverIndexByEntityId[entity.Id] = moverIndex;
            }
        }

        /// <summary>
        /// One mover's replicated state at a tick: its path position, the tick's travel expressed as a
        /// velocity, no facing, and grounded — a platform is a floor, and a client rendering it has no use
        /// for a falling flag.
        /// </summary>
        private PawnState MoverStateAt(int moverIndex, uint tick, float interval) {
            System.Numerics.Vector3 position = world.EvaluateMoverPosition(moverIndex, tick);
            System.Numerics.Vector3 delta = world.MoverDelta(moverIndex, tick);
            var velocity = new System.Numerics.Vector3(delta.X / interval, delta.Y / interval, delta.Z / interval);
            return new PawnState(position, 0f, velocity, PawnState.PackFlags(WireLocomotion.WalkSlow, false, true));
        }

        /// <summary>Destroys every entity a peer owns — what a player leaving for good triggers.</summary>
        public void DespawnOwnedBy(int ownerPeerId) {
            despawnScratch.Clear();
            registry.RemoveOwnedBy(ownerPeerId, despawnScratch);

            for (int idIndex = 0; idIndex < despawnScratch.Count; idIndex++) {
                uint entityId = despawnScratch[idIndex];
                inputQueues.Remove(entityId);
                moverIndexByEntityId.Remove(entityId);
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
        /// The tick this world last stepped, so a session module ticking alongside it stamps its own
        /// state with the same number the pawns were published under.
        /// </summary>
        public uint CurrentTick => currentTick;

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

            for (int inputIndex = 0; inputIndex < message.Count; inputIndex++) {
                EnqueueInput(entity, message.GetInput(inputIndex));
            }
        }

        /// <summary>
        /// Admits one input if it is new. Redundant bundles and channel replays legitimately re-deliver
        /// sequences already consumed; applying one twice would move the pawn twice, so only a sequence
        /// above the highest ever accepted gets through.
        /// </summary>
        private void EnqueueInput(NetEntity entity, in PawnInput input) {
            if (!IsAfter(input.Sequence, entity.HighestReceivedInputSequence)) {
                return;
            }

            entity.HighestReceivedInputSequence = input.Sequence;
            Queue<PawnInput> queue = ResolveInputQueue(entity.Id);

            if (queue.Count >= MaxQueuedInputsPerEntity) {
                queue.Dequeue();
            }

            queue.Enqueue(input);
        }

        /// <summary>Sequence ordering that survives the counter wrapping past uint.MaxValue.</summary>
        private static bool IsAfter(uint sequence, uint reference) {
            return (int)(sequence - reference) > 0;
        }

        /// <summary>
        /// Takes an owner's claimed state for an owner-authoritative pawn, judges it, and corrects the
        /// owner when the claim did not survive judgement.
        /// </summary>
        /// <remarks>
        /// The per-entity frame-change window is kept here rather than inside the validator, which is
        /// stateless and shared by every pawn: this is where an entity and the current tick are both in
        /// hand. Changes inside one window are accepted unmeasured until the budget is spent, and the
        /// ones past it come back as rejections — the previous state, previous frame and all — which the
        /// owner is told about like any other, so an alternating claim is visible through
        /// <see cref="OnMovementViolation"/> rather than free.
        ///
        /// The resync flag is consulted only once the measurement has refused the claim, so an owner
        /// repeating the flag against packet loss pays nothing for the repeats that were never needed;
        /// see <see cref="TryAcceptResync"/>.
        /// </remarks>
        public void HandleOwnerPawnUpdate(in OwnerPawnUpdate message, PeerHandle sender) {
            if (!TryResolveOwnedEntity(message.EntityId, sender, AuthorityMode.OwnerClient, out NetEntity entity)) {
                return;
            }

            float deltaSeconds = MeasuredIntervalSince(entity.LastDirtyTick);
            bool carrierChangeAllowed = IsCarrierChangeAllowed(entity, message.State.CarrierId);
            MovementVerdict verdict = validator.Validate(
                entity.PrefabId,
                entity.State,
                message.State,
                deltaSeconds,
                carrierChangeAllowed);

            if (verdict.RequiresCorrection && TryAcceptResync(entity, in message, deltaSeconds)) return;

            entity.ApplyState(verdict.ResolvedState, currentTick);
            entity.LastAcknowledgedInputSequence = message.ClientTick;

            if (!verdict.RequiresCorrection) {
                return;
            }

            OnMovementViolation?.Invoke(entity, verdict);
            SendCorrection(entity, sender);
        }

        /// <summary>
        /// Adopts a claim the measurement refused, when the owner flagged it as a resync and the
        /// frame-change budget still covers one; see <see cref="OwnerPawnUpdate.ResyncFlag"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The trust boundary.</b> A resync is a claim like any other and the server cannot check it:
        /// what the owner is asserting is that the pose either side of the gap belongs to two different
        /// stories, exactly as a frame change asserts they belong to two different origins. It is
        /// therefore given the frame change's terms and no better ones — accepted whole, unmeasured, and
        /// charged one slot of the same per-window budget — so a client that flags every update buys
        /// <see cref="MovementValidator.MaxCarrierSwitchesPerWindow"/> unmeasured moves per window and not
        /// one more. Once the budget is spent the refused verdict stands, and a claim that really is a
        /// teleport is thrown away like any other.
        /// </para>
        /// <para>
        /// <b>A burst is one resumption and is charged once.</b> The flag rides an unreliable datagram,
        /// so the owner sets it on the next few sends rather than one — see
        /// <c>NetActorSync.SendOwnerSample</c> — and the sends behind the first are that same claim
        /// arriving again, not a second one. Charging each of them would spend a whole window's budget on
        /// a single withhold exit and refuse the rider's next honest boarding, which is the harm the
        /// budget exists to prevent. So a flagged claim landing within
        /// <see cref="MovementValidator.ResyncBurstTicks"/> of the resync this pawn is already inside is
        /// adopted free — but only while it is that resumption carried on, per
        /// <see cref="MovementValidator.IsResyncBurstContinuation"/>. A repeat that is a fresh teleport
        /// is not a repeat and is refused outright, so the free window cannot be farmed.
        /// </para>
        /// <para>
        /// <b>At most one resync is adopted per pawn per server tick.</b> The window is counted in server
        /// ticks, and how many datagrams share a tick is chosen by the sender, not by the server: the
        /// owner channel is unreliable-sequenced, so every packet with a newer sequence is delivered and
        /// the whole arrival queue is handled at one value of <c>currentTick</c>. Without this rule a
        /// client that flags everything and sends two hundred times between two ticks gets two hundred
        /// free continuations for one charged slot, each judged at the full one-tick bar, silently — the
        /// free tail stops being bounded by anything. An honest owner sends one sample per send tick and
        /// so never trips it; a later claim in the same tick falls back to the measured verdict, which
        /// means a violation and a correction like any other refused move. A byte-identical duplicate
        /// leaves the pawn unmoved and unstamped and so stays free, which is right — it moves nothing.
        /// </para>
        /// <para>
        /// An accepted resync never corrects the owner: the whole point is that the state the server
        /// holds is the stale one, so handing it back is the harm being removed. A resync arriving with a
        /// frame change is left to the frame-change path so the two cannot charge the budget twice.
        /// </para>
        /// </remarks>
        private bool TryAcceptResync(NetEntity entity, in OwnerPawnUpdate message, float deltaSeconds) {
            if (!message.IsResync) return false;
            if (entity.State.CarrierId != message.State.CarrierId) return false;

            if (IsInsideResyncBurst(entity)) {
                if (HasMovedThisTick(entity)) return false;

                if (!validator.IsResyncBurstContinuation(
                        entity.PrefabId,
                        entity.State,
                        message.State,
                        deltaSeconds)) {
                    return false;
                }

                AdoptResync(entity, in message);
                return true;
            }

            if (!ChargeUnmeasuredMove(entity)) return false;

            entity.LastAcceptedResyncTick = currentTick;
            entity.HasAcceptedResync = true;
            AdoptResync(entity, in message);
            return true;
        }

        /// <summary>
        /// Whether this pawn is still inside the resync burst it last opened, so a further flagged claim
        /// is a repeat of that resumption rather than a new one.
        /// </summary>
        /// <remarks>
        /// The window is anchored to the send that opened the burst and never moved on by the repeats it
        /// covers: sliding it would let a client hold one paid-for burst open forever. The elapsed count
        /// is a plain unsigned subtraction, which stays correct across the tick counter wrapping.
        /// </remarks>
        private bool IsInsideResyncBurst(NetEntity entity) {
            if (!entity.HasAcceptedResync) return false;

            return currentTick - entity.LastAcceptedResyncTick < MovementValidator.ResyncBurstTicks;
        }

        /// <summary>
        /// Whether something has already moved this pawn on the tick being handled, so a further claim
        /// arriving on it cannot be the last one carried on.
        /// </summary>
        /// <remarks>
        /// <see cref="NetEntity.LastDirtyTick"/> is the right stamp to read: every accepted, clamped or
        /// refused claim writes the resolved state through <c>ApplyState</c>, so equality means this tick
        /// already produced a pose. It is deliberately not extended to the charged path — that one is
        /// bounded by <see cref="MovementValidator.MaxCarrierSwitchesPerWindow"/> whatever the arrival
        /// pattern — nor to ordinary measured updates, where two sends bunching into one tick interval is
        /// something jitter does to an honest client and each of them is measured anyway.
        /// </remarks>
        private bool HasMovedThisTick(NetEntity entity) {
            return currentTick == entity.LastDirtyTick;
        }

        /// <summary>Takes a resync claim whole, which is what an unmeasured move means.</summary>
        private void AdoptResync(NetEntity entity, in OwnerPawnUpdate message) {
            entity.ApplyState(message.State, currentTick);
            entity.LastAcknowledgedInputSequence = message.ClientTick;
        }

        /// <summary>
        /// Counts a claimed frame change into this pawn's window and answers whether the budget covers
        /// it; see <see cref="MovementValidator.MaxCarrierSwitchesPerWindow"/>.
        /// </summary>
        /// <remarks>
        /// A claim that keeps the frame the pawn is already in costs nothing and is not counted — it is
        /// measured as an ordinary move, unless it is a resync, which spends from the same budget through
        /// <see cref="TryAcceptResync"/>.
        /// </remarks>
        private bool IsCarrierChangeAllowed(NetEntity entity, ushort claimedCarrierId) {
            if (entity.State.CarrierId == claimedCarrierId) return true;

            return ChargeUnmeasuredMove(entity);
        }

        /// <summary>
        /// Spends one slot of this pawn's per-window budget of moves the server accepts without measuring
        /// them, and answers whether the budget covered it.
        /// </summary>
        /// <remarks>
        /// Every unmeasured move charges here, the refused attempts included, so a client cannot hold a
        /// window open by continuing to claim after its budget has run out. The elapsed count is a plain
        /// unsigned subtraction, which stays correct across the tick counter wrapping.
        /// </remarks>
        private bool ChargeUnmeasuredMove(NetEntity entity) {
            uint elapsedTicks = currentTick - entity.CarrierSwitchWindowStartTick;

            if (entity.CarrierSwitchesInWindow == 0 || elapsedTicks >= validator.CarrierSwitchCooldownTicks) {
                entity.CarrierSwitchWindowStartTick = currentTick;
                entity.CarrierSwitchesInWindow = 1;
                return true;
            }

            if (entity.CarrierSwitchesInWindow < byte.MaxValue) {
                entity.CarrierSwitchesInWindow++;
            }

            return entity.CarrierSwitchesInWindow <= MovementValidator.MaxCarrierSwitchesPerWindow;
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

            // Each catch-up step is stamped with the tick it stands for, ending on the one just reached.
            // The motor's world contains movers whose poses are a pure function of that stamp, so paying
            // back a stall with eight steps all labelled "now" would drag a platform through eight ticks
            // of travel in a single step and take its rider with it.
            uint firstTick = serverTick - elapsed + 1u;

            for (uint step = 0; step < elapsed; step++) {
                uint tick = firstTick + step;

                // Platforms first: a pawn stepped through this tick asks the world where its ride has got
                // to, and the answer has to be this tick's pose rather than the previous one's.
                StepMovers(tick);
                StepServerAuthorityEntities(tick);
            }

            lastSimulatedTick = serverTick;
        }

        /// <summary>One fixed motor step for every entity the server simulates on its owner's behalf.</summary>
        private void StepServerAuthorityEntities(uint tick) {
            IReadOnlyList<NetEntity> entities = registry.Entities;
            float interval = config.ServerTickInterval;

            for (int entityIndex = 0; entityIndex < entities.Count; entityIndex++) {
                NetEntity entity = entities[entityIndex];

                // Movers are server-authoritative too, but they are posed by StepMovers rather than
                // simulated. Letting one through here would hand a platform a starved pawn's input and
                // walk it off its own path.
                if (entity.Authority != AuthorityMode.Server || entity.Kind != EntityKind.Pawn) {
                    continue;
                }

                MovementProfile profile = config.GetMovementProfile(entity.PrefabId);

                if (profile == null) {
                    continue;
                }

                PawnInput input = NextInputFor(entity);
                PawnState next = PawnMotor.Step(in input, entity.State, profile, world, tick, interval);
                entity.ApplyState(next, tick);
            }
        }

        /// <summary>
        /// Pulls the next queued input, or repeats the last one without its jump when the stream has a
        /// gap. Only a real input advances the acknowledged sequence — inventing one would tell the
        /// owner's prediction buffer to discard work the server has never seen.
        /// </summary>
        private PawnInput NextInputFor(NetEntity entity) {
            if (inputQueues.TryGetValue(entity.Id, out Queue<PawnInput> queue) && queue.Count > 0) {
                PawnInput input = queue.Dequeue();
                entity.LastInput = input;
                entity.LastAcknowledgedInputSequence = input.Sequence;
                entity.StarvedTicks = 0;
                return input;
            }

            return StarvedInputFor(entity);
        }

        /// <summary>
        /// The input to run when the owner's stream has a gap. The last intent is held at full strength
        /// for a couple of ticks — ordinary phase mismatch between the owner's send loop and this tick
        /// loop must not hitch the pawn — and past that its move vector is halved each tick, so the pawn
        /// winds down instead of free-running. Every metre simulated here is distance the owner never
        /// predicted, comes back as a correction, and is the rubber-band; decaying bounds it.
        /// </summary>
        private static PawnInput StarvedInputFor(NetEntity entity) {
            entity.StarvedTicks++;
            PawnInput repeated = entity.LastInput.WithoutJump();

            if (entity.StarvedTicks > StarvationHoldTicks) {
                repeated.MoveDirection *= StarvationDecayFactor;
            }

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

            var message = new AuthorityCorrection(entity.Id, currentTick, entity.LastAcknowledgedInputSequence, entity.State);
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
                    entity.Kind,
                    entity.AuxId,
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
        /// Seconds an owner update is measured over: the age of the pawn's last change, floored at one
        /// tick and capped at <see cref="MaxMeasuredIntervalSeconds"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The floor matters because two updates landing inside one tick would otherwise divide by zero
        /// elapsed time and read as infinite speed.
        /// </para>
        /// <para>
        /// The cap matters because the tick this measures from is stamped when the pawn's state actually
        /// <em>changes</em>, not when it last reported: a pawn standing still, one whose owner stopped
        /// sending, and one whose updates the library is withholding all age the same way, and without a
        /// cap the allowance grows with the gap until a claim of any size fits inside it. Silence is not
        /// credit. A second is far longer than any honest gap between owner updates at thirty a second,
        /// so a client reporting continuously is measured exactly as before; a client that goes quiet and
        /// comes back somewhere else says so with <see cref="OwnerPawnUpdate.ResyncFlag"/>, which is
        /// budgeted, rather than by waiting long enough to buy the distance.
        /// </para>
        /// </remarks>
        private float MeasuredIntervalSince(uint tick) {
            uint elapsedTicks = currentTick > tick ? currentTick - tick : 1u;
            uint maxTicks = (uint)Math.Max(1, (int)(MaxMeasuredIntervalSeconds * config.ServerTickRate));

            if (elapsedTicks > maxTicks) {
                elapsedTicks = maxTicks;
            }

            return elapsedTicks * config.ServerTickInterval;
        }
    }
}
