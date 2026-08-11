using System;
using AlpineLib.DI;
using AlpineLib.Netcode.Collision;
using AlpineLib.Netcode.Replication;
using AlpineLib.Netcode.Timing;
using AlpineLib.Networking;
using AlpineLib.Sessions;
using UnityEngine;
using Numerics = System.Numerics;

namespace AlpineLib.Collision {
    /// <summary>
    /// Places a replicated platform in the scene from the shared path rather than from the snapshot
    /// stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A mover's pose is a pure function of the tick, so the honest way to render one is to evaluate that
    /// function — not to interpolate between the two most recent snapshots of it. That matters because a
    /// pawn standing on the platform is predicted at the <b>predicted</b> tick while everything else in
    /// the scene renders at the <b>interpolated</b> tick, a fixed interpolation delay behind. Rendering
    /// the platform at the interpolation tick while its rider is predicted would slide the rider across
    /// its deck by exactly that delay's worth of travel.
    /// </para>
    /// <para>
    /// So this view renders at the interpolation tick normally and blends to the predicted tick while the
    /// local pawn is riding it, over <see cref="BlendSeconds"/>. When the definition cannot be resolved —
    /// a client whose registry is older than the server's export — it falls back to the ordinary
    /// interpolated state, which is wrong by the delay but never wrong by a whole platform.
    /// </para>
    /// <para>
    /// Both ticks are carried as fractions of a tick rather than whole ticks. The simulation only ever
    /// asks for whole ones, but a platform drawn on whole ticks moves in visible thirty-hertz steps at a
    /// sixty-hertz frame rate; evaluating the two ticks that bracket the render time and mixing them
    /// costs one extra pure evaluation and takes the stepping out. Nothing here feeds the simulation, so
    /// none of it is on the deterministic path.
    /// </para>
    /// <para>
    /// <b>Translation only.</b> The rotation of a v1 mover is never touched, matching the exporter, which
    /// ignores the authored rotation rather than pretending to honour it.
    /// </para>
    /// <para>
    /// <b>The view needs a Unity collider.</b> The shared simulation stands the pawn on the mover's
    /// exported box, but the locally-owned pawn's <c>CharacterController</c> also runs Unity physics —
    /// gravity grounds it against Unity colliders, and against nothing else. A mover view without a
    /// collider leaves Unity believing there is no deck at all: gravity drags the visible pawn down to
    /// whatever static floor is underneath while the simulation insists its feet are on the platform,
    /// and the pawn renders sunk inside the mesh. Give the view a collider matching the exported box;
    /// <see cref="Start"/> warns when there is none.
    /// </para>
    /// <para>
    /// <b>Vertically-moving movers</b> work, with a caveat: the collider is moved by writing the
    /// transform, not by sweeping a rigidbody, so a descending deck is only tracked as fast as gravity
    /// re-grounds the controller each frame, and an ascending one relies on next-frame depenetration
    /// plus the sync component's convergence to the simulated height — which already contains the ride.
    /// That equilibrates within a frame or two at sensible speeds; keep vertical mover speeds well below
    /// the per-frame gravity ramp rather than expecting a rider-parenting system that does not exist.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(NetExecutionOrder.PawnDrivers)]
    [RequireComponent(typeof(NetEntityView))]
    public class NetMoverView : MonoBehaviour {
        /// <summary>Seconds spent blending between the interpolated and predicted tick when a rider steps on or off.</summary>
        public const float BlendSeconds = 0.15f;

        /// <summary>
        /// Metres above and below the local pawn's feet the rider probe searches for a supporting
        /// surface.
        /// </summary>
        /// <remarks>
        /// Generous on purpose. The probe is asking a yes-or-no question about which deck the player is
        /// standing on, not resolving a step, and the pawn's reported feet sit a tolerance either side of
        /// the surface depending on when in the tick the state was sampled. Answering "not riding" for a
        /// centimetre of that would drop the blend and start it again every few frames.
        /// </remarks>
        public const float RideProbeHeight = 0.35f;

        [Tooltip("Work out whether the local pawn is riding by probing the shared world. Off leaves that entirely to SetCarryingLocalPawn.")]
        [SerializeField] private bool detectRiderAutomatically = true;

        private NetEntityView entityView;
        private ISessionService sessionService;
        private INetworkService networkService;
        private MoverDefinition definition;
        private CollisionWorld resolvedWorld;
        private uint resolvedEntityId;
        private float tickIntervalSeconds = CollisionWorld.DefaultTickIntervalSeconds;
        private int moverIndex = -1;
        private bool isCarryingLocalPawn;
        private float blendWeight;
        private Numerics.Vector3 renderOffset;
        private bool hasWarnedTickIntervalMismatch;

        /// <summary>The replicated entity this platform stands for.</summary>
        public NetEntityView EntityView => entityView;

        /// <summary>The mover this view renders, or null before the world resolves it.</summary>
        public MoverDefinition Definition => definition;

        /// <summary>Index of this platform in the world's mover list, or <c>-1</c> while unresolved.</summary>
        public int MoverIndex => moverIndex;

        /// <summary>True while the local player's pawn is standing on this platform.</summary>
        public bool IsCarryingLocalPawn => isCarryingLocalPawn;

        /// <summary>How far the render has blended towards the predicted tick, from zero to one. Diagnostic.</summary>
        public float BlendWeight => blendWeight;

        /// <summary>
        /// Binds the shared definition this platform is posed from, together with its index in the world
        /// the definition came out of.
        /// </summary>
        /// <remarks>
        /// The index has to travel with the definition because it is what a support hit reports, and the
        /// rider test compares the two. Ordinarily this component resolves both itself from the client
        /// world; the entry point exists for a game that already knows the answer, and for tests that
        /// have no session to resolve through.
        /// </remarks>
        public void BindDefinition(MoverDefinition moverDefinition, int worldMoverIndex) {
            definition = moverDefinition;
            moverIndex = worldMoverIndex;
        }

        /// <summary>Tells the view whether the local pawn is riding, which decides the tick it renders at.</summary>
        /// <remarks>
        /// Overwritten every frame while <c>detectRiderAutomatically</c> is on, which is the default —
        /// turn it off before driving this from outside.
        /// </remarks>
        public void SetCarryingLocalPawn(bool isCarrying) {
            isCarryingLocalPawn = isCarrying;
        }

        /// <summary>Sets the tick length mover poses are evaluated against, when binding by hand.</summary>
        public void SetTickInterval(float seconds) {
            if (seconds <= 0f) {
                return;
            }

            tickIntervalSeconds = seconds;
        }

        /// <summary>
        /// Places the platform for this frame, blending between the two ticks according to whether the
        /// local pawn is riding.
        /// </summary>
        /// <param name="interpolationTick">Tick everything else in the scene is rendered at, fractional.</param>
        /// <param name="predictedTick">Tick the local pawn's prediction has reached, fractional.</param>
        /// <param name="deltaSeconds">Frame length, which the blend advances by.</param>
        public void Render(double interpolationTick, double predictedTick, float deltaSeconds) {
            if (definition?.Path == null) {
                return;
            }

            AdvanceBlend(deltaSeconds);

            Numerics.Vector3 interpolated = EvaluateAt(interpolationTick);

            if (blendWeight <= 0f) {
                renderOffset = Numerics.Vector3.Zero;
                transform.position = interpolated.ToUnity();
                return;
            }

            Numerics.Vector3 predicted = EvaluateAt(predictedTick);

            if (blendWeight >= 1f) {
                renderOffset = predicted - interpolated;
                transform.position = predicted.ToUnity();
                return;
            }

            Numerics.Vector3 drawn = Numerics.Vector3.Lerp(interpolated, predicted, blendWeight);
            renderOffset = drawn - interpolated;
            transform.position = drawn.ToUnity();
        }

        private void Awake() {
            entityView = GetComponent<NetEntityView>();
        }

        /// <remarks>
        /// Resolved through <see cref="Injector.TryResolve{T}"/> rather than injected, so a platform
        /// prefab dropped into a scene with no networking installed simply never finds a session and
        /// stays where the designer left it.
        /// </remarks>
        private void Start() {
            if (GetComponent<Collider>() == null) {
                Debug.LogWarning(
                    $"NetMoverView::Start->{name} has no Collider. The simulation stands pawns on this " +
                    "mover, but the locally-owned pawn's CharacterController grounds against Unity " +
                    "colliders — without one it sinks through the deck. Add a collider matching the " +
                    "exported mover box.");
            }

            if (!Injector.HasInstance) {
                return;
            }

            Injector.Instance.TryResolve(out sessionService);
            Injector.Instance.TryResolve(out networkService);
        }

        /// <remarks>
        /// A published offset outliving its view would pin remote riders to a platform that is no longer
        /// being drawn away from the interpolation timeline.
        /// </remarks>
        private void OnDisable() {
            if (moverIndex < 0) {
                return;
            }

            sessionService?.Replication?.ClearMoverRenderOffset(moverIndex);
        }

        private void Update() {
            ClientReplication replication = sessionService?.Replication;

            if (replication == null) {
                return;
            }

            if (entityView == null || !entityView.IsBound) {
                return;
            }

            ResolveDefinition(replication.CollisionWorld);

            if (definition?.Path == null || networkService?.Client == null) {
                FallBackToInterpolator(replication);
                return;
            }

            NetClock clock = networkService.Client.Clock;
            double interpolationTick = ResolveInterpolationTick(replication, clock);
            double predictedTick = ResolvePredictedTick(replication, interpolationTick);

            if (detectRiderAutomatically) {
                isCarryingLocalPawn = DetectCarryingLocalPawn(replication, predictedTick);
            }

            Render(interpolationTick, predictedTick, Time.deltaTime);
            PublishRenderOffset(replication);
        }

        /// <summary>
        /// Tells the client world how far this platform is currently drawn from its interpolation-tick
        /// pose, so remote-pawn drivers can re-anchor riders onto the deck as it is actually rendered.
        /// </summary>
        /// <remarks>
        /// The offset is only ever non-zero while the local pawn is riding — that is the one time the
        /// platform is drawn ahead of the interpolation timeline, and exactly the time a remote rider
        /// drawn on that timeline would otherwise trail across the deck by the interpolation delay's
        /// worth of travel. Consumers may read it a frame stale, because every driver shares one
        /// execution-order slot; the blend ramps the offset over <see cref="BlendSeconds"/>, so a frame
        /// of staleness is sub-millimetre.
        /// </remarks>
        private void PublishRenderOffset(ClientReplication replication) {
            if (moverIndex < 0) {
                return;
            }

            replication.SetMoverRenderOffset(moverIndex, renderOffset);
        }

        /// <summary>
        /// Finds this platform's definition in the client's world, and remembers which world and which
        /// entity the answer was for so a scene swap or a rebind asks again.
        /// </summary>
        private void ResolveDefinition(CollisionWorld world) {
            if (world == null) {
                return;
            }

            if (ReferenceEquals(world, resolvedWorld) && resolvedEntityId == entityView.EntityId) {
                return;
            }

            resolvedWorld = world;
            resolvedEntityId = entityView.EntityId;
            tickIntervalSeconds = world.TickIntervalSeconds;
            definition = null;
            moverIndex = -1;

            if (entityView.Kind != EntityKind.Mover) {
                return;
            }

            if (TryFindMover(world, entityView.AuxId)) {
                return;
            }

            Debug.LogWarning(
                $"NetMoverView::ResolveDefinition->{name} has no mover {entityView.AuxId} in the loaded geometry; " +
                "falling back to interpolated placement. Re-export the scene.");
        }

        /// <summary>Matches the bound entity's aux id against the world's movers, in export order.</summary>
        private bool TryFindMover(CollisionWorld world, ushort moverId) {
            for (int candidateIndex = 0; candidateIndex < world.Movers.Count; candidateIndex++) {
                MoverDefinition candidate = world.Movers[candidateIndex];

                if (candidate == null || candidate.MoverId != moverId) {
                    continue;
                }

                definition = candidate;
                moverIndex = candidateIndex;
                return true;
            }

            return false;
        }

        /// <summary>
        /// The fractional tick everything else in the scene is being rendered at: the client's estimate
        /// of the server clock, held back by the timeline's current interpolation delay.
        /// </summary>
        /// <remarks>
        /// Seconds become ticks through the world's tick interval, not the clock's: the paths were
        /// exported against the world's interval, and it is the world's number the simulation evaluates
        /// them with. The two agree whenever config and export are in step; when they are not, the world
        /// wins here so the platform at least runs at the speed the server is running it, and the
        /// mismatch is reported once instead of rendered as a permanently wrong platform speed.
        /// </remarks>
        private double ResolveInterpolationTick(ClientReplication replication, NetClock clock) {
            WarnOnceOnTickIntervalMismatch(clock);

            double renderSeconds = clock.EstimatedServerSeconds - replication.Timeline.DelaySeconds;
            double tick = renderSeconds / tickIntervalSeconds;

            return tick > 0.0 ? tick : 0.0;
        }

        private void WarnOnceOnTickIntervalMismatch(NetClock clock) {
            if (hasWarnedTickIntervalMismatch) {
                return;
            }

            if (Math.Abs(tickIntervalSeconds - clock.TickInterval) <= 1e-6) {
                return;
            }

            hasWarnedTickIntervalMismatch = true;
            Debug.LogWarning(
                $"NetMoverView::ResolveInterpolationTick->{name}: geometry tick interval " +
                $"{tickIntervalSeconds:0.####}s does not match the clock's {clock.TickInterval:0.####}s. " +
                "Re-export the scene geometry against the current tick rate; using the geometry's value.");
        }

        /// <summary>
        /// The tick the local pawn's prediction has reached: the base tick of its last correction plus
        /// every input still in flight, which is exactly the tick its newest predicted state was stepped
        /// at.
        /// </summary>
        /// <remarks>
        /// Whole rather than fractional, deliberately. This is the number the rider's own position was
        /// computed from, and a platform drawn half a tick away from it would slide the rider across the
        /// deck by that half tick — the very error the predicted tick exists to remove.
        /// </remarks>
        private static double ResolvePredictedTick(ClientReplication replication, double fallbackTick) {
            NetEntity pawn = FindLocalPawn(replication);

            if (pawn == null || !replication.TryGetPredictionBaseTick(pawn.Id, out uint baseTick)) {
                return fallbackTick;
            }

            PredictionBuffer buffer = replication.GetPredictionBuffer(pawn.Id);

            return baseTick + (double)(buffer?.Count ?? 0);
        }

        /// <summary>
        /// Asks the shared world what the local pawn is standing on at the tick it was predicted at, and
        /// answers true when it is this platform.
        /// </summary>
        /// <remarks>
        /// Asked of the world rather than tracked through the motor because the motor keeps no state
        /// about what it stands on — the rider rule is a stateless lookup by design — and because a view
        /// that answers from the same query the simulation used cannot disagree with it about which deck
        /// the player is on.
        /// </remarks>
        private bool DetectCarryingLocalPawn(ClientReplication replication, double predictedTick) {
            if (moverIndex < 0) {
                return false;
            }

            NetEntity pawn = FindLocalPawn(replication);

            if (pawn == null || !pawn.State.IsGrounded) {
                return false;
            }

            Numerics.Vector3 foot = pawn.State.Position;
            uint probeTick = ToWholeTick(predictedTick);

            bool supported = replication.CollisionWorld.TryGetSupport(
                foot.X,
                foot.Z,
                foot.Y + RideProbeHeight,
                foot.Y - RideProbeHeight,
                probeTick,
                out SupportHit hit);

            return supported && hit.IsMover && hit.MoverIndex == moverIndex;
        }

        /// <summary>The pawn this client predicts, or null when it owns none yet.</summary>
        private static NetEntity FindLocalPawn(ClientReplication replication) {
            for (int entityIndex = 0; entityIndex < replication.Entities.Count; entityIndex++) {
                NetEntity candidate = replication.Entities[entityIndex];

                if (replication.IsPredicted(candidate)) {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Places the platform from the replicated snapshot stream, for a client that cannot resolve the
        /// path this platform runs on.
        /// </summary>
        private void FallBackToInterpolator(ClientReplication replication) {
            if (!replication.SampleRemote(entityView.EntityId, out PawnState state)) {
                return;
            }

            transform.position = state.Position.ToUnity();
        }

        /// <summary>Moves the blend one frame towards whichever tick the platform should be drawn at.</summary>
        private void AdvanceBlend(float deltaSeconds) {
            float target = isCarryingLocalPawn ? 1f : 0f;

            if (deltaSeconds <= 0f) {
                return;
            }

            blendWeight = Mathf.MoveTowards(blendWeight, target, deltaSeconds / BlendSeconds);
        }

        /// <summary>
        /// Evaluates the path at a fractional tick by mixing the two whole ticks that bracket it.
        /// </summary>
        private Numerics.Vector3 EvaluateAt(double tickTime) {
            uint lowTick = ToWholeTick(tickTime);
            Numerics.Vector3 low = definition.Path.EvaluatePosition(lowTick, tickIntervalSeconds);
            float fraction = (float)(tickTime - lowTick);

            if (fraction <= 0f || lowTick == uint.MaxValue) {
                return low;
            }

            Numerics.Vector3 high = definition.Path.EvaluatePosition(lowTick + 1u, tickIntervalSeconds);

            return new Numerics.Vector3(
                low.X + (high.X - low.X) * fraction,
                low.Y + (high.Y - low.Y) * fraction,
                low.Z + (high.Z - low.Z) * fraction);
        }

        /// <summary>Floors a fractional tick into the whole tick the simulation would speak in.</summary>
        private static uint ToWholeTick(double tickTime) {
            if (tickTime <= 0.0) {
                return 0u;
            }

            double floored = Math.Floor(tickTime);

            return floored >= uint.MaxValue ? uint.MaxValue : (uint)floored;
        }
    }
}
