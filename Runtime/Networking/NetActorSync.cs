using System.Collections.Generic;
using AlpineLib.Actors;
using AlpineLib.Actors.Locomotion;
using AlpineLib.DI;
using AlpineLib.Netcode;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Sessions;
using UnityEngine;
using Numerics = System.Numerics;

namespace AlpineLib.Networking {
    /// <summary>
    /// The owner's end of a replicated pawn: samples what the local player is asking their actor to do,
    /// sends it to the authority at the configured rate, and keeps the actor sitting where the shared
    /// motor says it will end up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only ever present on a pawn the local player owns. Remote pawns are driven by
    /// <see cref="NetController"/> instead, and an unowned or unbound view leaves this component idle.
    /// </para>
    /// <para>
    /// In the default <see cref="AuthorityMode.Server"/> the send is an <c>InputCommand</c> and the
    /// shared motor immediately predicts the result locally, so the pawn answers the stick on the frame
    /// it moved rather than a round trip later; the server's verdict arrives as an
    /// <c>AuthorityCorrection</c>, which the client world rewinds and replays before handing back the
    /// resolved state. In <see cref="AuthorityMode.OwnerClient"/> nothing is predicted — the actor's own
    /// simulation is the truth and is reported as a <c>PawnState</c> for the server to validate.
    /// </para>
    /// <para>
    /// Intent is derived from the actor rather than read from an input reader on purpose: this component
    /// ships in the library and knows nothing about a game's action maps, while every game's actor
    /// already carries the resolved motion, gait and crouch. The one thing an actor cannot express after
    /// the fact is a jump — it is an impulse, gone by the next sample — so a controller announces that
    /// through <see cref="QueueJump"/>.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(NetExecutionOrder.PawnDrivers)]
    [RequireComponent(typeof(NetEntityView))]
    public class NetActorSync : MonoBehaviour {
        [Header("Prediction")]
        [Tooltip("Write the predicted position back onto the actor. Off leaves the actor's own movement in charge and only corrections are applied.")]
        [SerializeField] private bool applyPredictedPosition = true;
        [Tooltip("Write the predicted facing back onto the actor. Off by default: the motor derives yaw from travel, which fights a first-person camera that owns the facing.")]
        [SerializeField] private bool applyPredictedYaw;
        [Tooltip("How hard the actor is pulled onto the prediction, per second. Higher closes the gap sooner; around 20 lands within a couple of frames without the actor feeling dragged.")]
        [SerializeField] private float followSharpness = 20f;

        [Header("Corrections")]
        [Tooltip("Metres the actor may differ from the prediction before it is placed outright. Below this the difference is walked off by the follow, never teleported.")]
        [SerializeField] private float correctionSnapDistance = 1f;
        [Tooltip("Seconds a correction under the snap distance is spread over. Zero applies every correction the moment it lands.")]
        [SerializeField] private float correctionSmoothingSeconds = 0.12f;

        /// <summary>
        /// Seconds of send backlog kept when frames are long. Anything older is dropped rather than
        /// burst-sent, because stale intent is worse than missing intent.
        /// </summary>
        private const float MaxSendBacklogSeconds = 0.25f;

        /// <summary>
        /// Metres below which a correction residual is simply dropped, so the smoothing does not chase an
        /// offset nobody could see for the rest of the session. Doubles as the shortest follow step worth
        /// pushing through the character controller.
        /// </summary>
        private const float ResidualEpsilon = 0.001f;

        /// <summary>
        /// How long an owned pawn's spawn placement waits for the carrier its state names, before giving
        /// up and placing it in world space.
        /// </summary>
        /// <remarks>
        /// Long enough to cover a join whose first keyframe arrives before the scene's carriers have
        /// registered, short enough that a pawn is never left standing at its prefab's authored transform
        /// for a noticeable part of a session. What happens at the end of it is a fall back to the
        /// authority's <em>current</em> pose, never a placement at the spawn state's deck-local numbers;
        /// see <see cref="PlaceOnExpiredSpawnDeferral"/>.
        /// </remarks>
        public const float SpawnPlacementCarrierWaitSeconds = 5f;

        private NetEntityView _view;
        private Actor _actor;
        private CharacterController _characterController;
        private LocomotionSystem _locomotion;
        private CrouchSystem _crouch;
        private INetCarrierSource _carrierSource;
        private INetworkService _networkService;
        private ISessionService _sessionService;
        private ClientReplication _boundReplication;
        private float _sendAccumulatorSeconds;
        private bool _jumpQueued;
        private Vector3 _correctionResidual;
        private PawnState _predictedState;
        private bool _hasPredictedState;
        private uint _placedForEntityId;
        private PawnState _spawnState;
        private bool _hasSpawnState;
        private float _spawnPlacementDeadline;
        private bool _hasWarnedUnplaceableSpawn;
        private readonly HashSet<ushort> _warnedCarrierIds = new HashSet<ushort>();
        private readonly HashSet<int> _warnedUnusableCarriers = new HashSet<int>();

        /// <summary>
        /// True while this pawn is actually being replicated: bound to an entity this client owns inside
        /// a live session. Controllers use it to decide whether a jump goes through
        /// <see cref="QueueJump"/> or straight to the actor.
        /// </summary>
        public bool IsNetworked =>
            _view != null && _view.IsBound && _view.IsOwned && ResolveReplication() != null;

        /// <summary>
        /// Flags a jump on the next input sent to the authority. The visible impulse is applied on that
        /// same send, not on the frame of the press.
        /// </summary>
        /// <remarks>
        /// Latched rather than sampled: a jump is pressed on a render frame and sent on a network tick,
        /// and those rarely coincide. The latch clears when the input carrying it goes out, so a jump is
        /// never sent twice. Deferring the local <see cref="Actor.Jump"/> to the same send keeps the
        /// visible arc, the predicted arc and the authoritative arc all starting on the same step — a
        /// jump applied on the press frame starts up to a full send interval before the wire's, and that
        /// head start comes back as a correction at the top of every arc.
        /// </remarks>
        public void QueueJump() {
            _jumpQueued = true;
        }

        /// <summary>
        /// Announces a discrete event on this pawn — an emote, a wave, anything the game defines — so
        /// every other client plays it too.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Announced rather than replicated: state replication carries where a pawn is, not the instants
        /// that happen to it, and a one-frame trigger sampled fifteen times a second is a trigger the
        /// observers mostly miss.
        /// </para>
        /// <para>
        /// The caller plays the event locally on the frame it happened; this only tells the authority.
        /// The server echoes it back to the whole session, sender included, so everyone sees one
        /// server-chosen order — and the client world recognises this client's own stamp coming back and
        /// drops it rather than playing the emote a second time.
        /// </para>
        /// </remarks>
        public void RaiseEvent(byte eventId, byte argument) {
            ClientReplication replication = ResolveReplication();

            if (replication == null) return;
            if (!_view.IsBound || !_view.IsOwned) return;

            replication.SendEntityEvent(_view.EntityId, eventId, argument);
        }

        private void Awake() {
            _view = GetComponent<NetEntityView>();
            _actor = GetComponent<Actor>();
            _characterController = GetComponent<CharacterController>();
            _locomotion = GetComponent<LocomotionSystem>();
            _crouch = GetComponent<CrouchSystem>();

            // Matched to the codebase's other interface lookup. TryGetComponent's silent false would
            // quietly demote this pawn to world-space replication with nothing in the log to say so.
            _carrierSource = GetComponent<INetCarrierSource>();
        }

        /// <remarks>
        /// Services are resolved through <see cref="Injector.TryResolve{T}"/> rather than injected,
        /// because a pawn must still work in a scene opened directly with no networking installed at
        /// all — there this component simply never finds a session and never sends anything.
        /// </remarks>
        private void Start() {
            if (!Injector.HasInstance) return;

            Injector.Instance.TryResolve(out _networkService);
            Injector.Instance.TryResolve(out _sessionService);
        }

        private void OnDestroy() {
            UnbindReplication();
        }

        /// <remarks>
        /// The server-authoritative send stays here, in the <c>Update</c> phase, because it is an
        /// <em>intent</em>: the input is what the player asked for this frame and the prediction that
        /// answers it must be recorded before <see cref="FollowPrediction"/> converges the actor onto it,
        /// in the same phase, on the same frame.
        /// </remarks>
        private void Update() {
            DecayCorrectionResidual();

            ClientReplication replication = ResolveReplication();

            if (replication == null) return;
            if (!_view.IsBound || !_view.IsOwned) return;

            BindReplication(replication);
            PlaceOnSpawnState();

            if (_view.Authority != AuthorityMode.OwnerClient) {
                AccumulateAndSend(replication);
            }

            FollowPrediction();
        }

        /// <summary>
        /// Puts a freshly bound owner-simulated pawn where the authority says it is, once.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Nothing else ever places an owned pawn. It is possessed by the game's own controller rather
        /// than <see cref="NetController"/>, prediction is skipped for owner authority, and a correction
        /// only arrives once the server has something to disagree with — so a rejoining rider whose
        /// keyframe named a carrier the spawner could not resolve was left standing at its prefab's
        /// authored transform, reported that pose, and had the authority adopt it. The pawn did not
        /// heal; the world converged onto the bug.
        /// </para>
        /// <para>
        /// The state is taken at bind and held, so what is placed is the spawn pose rather than whatever
        /// the wrongly-placed pawn has since talked the server into. A carrier that has not registered
        /// yet defers the placement rather than losing it, for at most
        /// <see cref="SpawnPlacementCarrierWaitSeconds"/>, after which
        /// <see cref="PlaceOnExpiredSpawnDeferral"/> decides what there is left to place.
        /// </para>
        /// </remarks>
        private void PlaceOnSpawnState() {
            if (_view.Authority != AuthorityMode.OwnerClient) return;
            if (_placedForEntityId == _view.EntityId) return;

            RecordSpawnState();

            if (NetCarrierFrame.TryToWorld(in _spawnState, out PawnState world)) {
                PlaceAt(in world);
                return;
            }

            if (Time.unscaledTime < _spawnPlacementDeadline) return;

            PlaceOnExpiredSpawnDeferral();
        }

        /// <summary>
        /// Decides where a pawn goes once the carrier its spawn state named has had its whole wait and
        /// still cannot be resolved.
        /// </summary>
        /// <remarks>
        /// The spawn state's numbers are metres from a deck's origin, so placing the pawn at them as world
        /// space drops it within a few metres of the world origin — the very failure this placement exists
        /// to remove, reached deliberately instead of by accident. The authority's current state is the
        /// better answer whenever it is world-frame: a pawn with no carrier to name has spent the whole
        /// wait reporting world space and having it adopted, so that pose is a real place. When it is
        /// still carrier-relative there is nothing here anyone can turn into a position — the capture side
        /// withholds rather than inventing one — so the deferral continues, warning once, and a carrier
        /// that registers late still heals the pawn on the next frame.
        /// </remarks>
        private void PlaceOnExpiredSpawnDeferral() {
            PawnState current = _view.Entity.State;

            if (!current.IsCarrierRelative) {
                Debug.LogWarning($"NetActorSync::PlaceOnExpiredSpawnDeferral->{name} spawned on carrier {_spawnState.CarrierId}, which never registered; placing entity {_view.EntityId} at the authority's current world pose instead.");
                PlaceAt(in current);
                return;
            }

            if (_hasWarnedUnplaceableSpawn) return;

            _hasWarnedUnplaceableSpawn = true;
            Debug.LogError($"NetActorSync::PlaceOnExpiredSpawnDeferral->{name} is still carrier-relative on carrier {current.CarrierId}, which no loaded carrier answers to; entity {_view.EntityId} keeps its current pose and the placement waits for that carrier.");
        }

        /// <summary>
        /// Latches the state this pawn was bound with, and the deadline its carrier has to appear by.
        /// </summary>
        /// <remarks>
        /// The deadline runs on unscaled time: it measures how long the network has been naming something
        /// this client cannot resolve, which keeps passing while a game sits at <c>timeScale</c> zero.
        /// </remarks>
        private void RecordSpawnState() {
            if (_hasSpawnState) return;

            _spawnState = _view.Entity.State;
            _hasSpawnState = true;
            _spawnPlacementDeadline = Time.unscaledTime + SpawnPlacementCarrierWaitSeconds;
        }

        /// <summary>
        /// Places the actor outright at a world-space state and hands its motion to the actor's own
        /// integrators, then marks this entity as placed.
        /// </summary>
        private void PlaceAt(in PawnState world) {
            _correctionResidual = Vector3.zero;
            Teleport(world.Position.ToUnity());
            transform.rotation = Quaternion.Euler(0f, world.YawDegrees, 0f);
            SyncActorMotion(in world);

            _placedForEntityId = _view.EntityId;
            _hasSpawnState = false;
        }

        /// <summary>
        /// The owner-simulated send, deferred to the end of the frame so it reports where the pawn
        /// actually finished it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// An owner-simulated pawn reports a measured pose, not an intent, and a game carries its riders
        /// last: a platform rider applies the deck's travel in its own <c>LateUpdate</c>, after the brain
        /// has moved the actor and before the actor settles. Sampling in <c>Update</c> would read the
        /// transform from before this frame's carry while reading the carrier's transform from after it,
        /// and <see cref="NetCarrierFrame.ToLocal"/> would subtract one frame of carrier travel out of
        /// the rider's deck position — half a metre at a thirty-metre-a-second consist and sixty frames,
        /// a full metre at thirty, and it moves with the frame rate.
        /// </para>
        /// <para>
        /// This component's execution order already puts it after the actor's own <c>LateUpdate</c> and
        /// after any rider carrying the pawn, so by here the transform, the actor's measured velocity and
        /// the carrier's pose are all this frame's.
        /// </para>
        /// </remarks>
        private void LateUpdate() {
            ClientReplication replication = ResolveReplication();

            if (replication == null) return;
            if (!_view.IsBound || !_view.IsOwned) return;
            if (_view.Authority != AuthorityMode.OwnerClient) return;

            BindReplication(replication);
            AccumulateAndSend(replication);
        }

        /// <summary>
        /// Advances the send clock and pushes at most one sample per network tick, dropping a backlog
        /// longer than <see cref="MaxSendBacklogSeconds"/> instead of flushing it in a burst.
        /// </summary>
        private void AccumulateAndSend(ClientReplication replication) {
            float sendInterval = ResolveSendInterval();

            _sendAccumulatorSeconds += Time.deltaTime;

            if (_sendAccumulatorSeconds > MaxSendBacklogSeconds) {
                _sendAccumulatorSeconds = sendInterval;
            }

            if (_sendAccumulatorSeconds < sendInterval) return;

            _sendAccumulatorSeconds -= sendInterval;
            SendSample(replication);
        }

        private void SendSample(ClientReplication replication) {
            if (_view.Authority == AuthorityMode.OwnerClient) {
                if (!TryCaptureState(out PawnState captured)) return;

                ApplyDeferredJump();
                replication.SubmitOwnerPawnState(_view.EntityId, captured);
                FlushQueuedJump(replication);
                return;
            }

            PawnInput input = BuildInput();
            ApplyDeferredJump();
            PawnState predicted = replication.SubmitInput(_view.EntityId, in input);
            FlushQueuedJump(replication);
            ApplyPredictedState(in predicted);
        }

        /// <summary>
        /// Plays the latched jump's local impulse on the tick its input leaves, so the visible arc and
        /// the simulated arcs share a start step; see <see cref="QueueJump"/>.
        /// </summary>
        private void ApplyDeferredJump() {
            if (!_jumpQueued || _actor == null) return;

            _actor.Jump();
        }

        /// <summary>
        /// Announces the latched jump, once the input carrying it has gone out, and clears the latch.
        /// </summary>
        /// <remarks>
        /// The impulse and the announcement are two different jobs. The input's jump bit is what makes
        /// the authority's motor push the pawn upwards; this event is what makes every other client's
        /// pawn actually play the jump, because a remote pawn is driven from interpolated state and an
        /// impulse leaves nothing in that state to read back. Sent from here rather than from
        /// <see cref="QueueJump"/> so the two always leave on the same tick, and after
        /// <see cref="BuildInput"/> has read the latch.
        /// </remarks>
        private void FlushQueuedJump(ClientReplication replication) {
            if (!_jumpQueued) return;

            _jumpQueued = false;
            replication.SendEntityEvent(_view.EntityId, NetController.JumpEventId, 0);
        }

        /// <summary>
        /// Turns this frame's intent into one step of input: where the player is asking to go, at which
        /// gait, and whether they are crouching or jumping. The sequence field is left zero — the client
        /// world stamps it from its own monotonic counter on submit.
        /// </summary>
        /// <remarks>
        /// The move direction is the direction the possessing controller <em>commanded</em> this frame,
        /// never the velocity the transform was measured to have. Measured velocity contains everything
        /// that ever moves the transform — including this component's own correction writes — and
        /// sending it back as intent turns every correction into new input, a loop that feeds itself.
        /// </remarks>
        private PawnInput BuildInput() {
            WireLocomotion gait = ResolveGait();
            bool isCrouching = _crouch != null && _crouch.IsCrouching;

            return new PawnInput(0u, ResolveMoveDirection(), gait, _jumpQueued, isCrouching);
        }

        private Numerics.Vector2 ResolveMoveDirection() {
            if (_actor == null) return Numerics.Vector2.Zero;

            Vector3 commanded = _actor.CommandedMoveDirection;
            commanded.y = 0f;

            return Vector3.ClampMagnitude(commanded, 1f).ToPlanarNumerics();
        }

        /// <summary>
        /// Reads the actor's current pose as an authoritative state, for owner-simulated pawns.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A pawn whose game reports a carrier is captured in that carrier's frame instead of the
        /// world's, so what leaves the wire is the metre a second it is walking rather than the forty the
        /// train is doing. Only this owner-simulated path converts: a server-authoritative pawn is
        /// stepped by the shared motor, which knows only world space, and reporting anything else would
        /// be reporting a pose the authority cannot act on.
        /// </para>
        /// <para>
        /// <b>A carrier that is present but unusable withholds the update instead of reporting one.</b> A
        /// game only hands out a carrier for a body it is also carrying, so a carrier the registry cannot
        /// resolve back to itself — see <see cref="NetCarrier.IsRegistered"/> — leaves this component with
        /// two lies to choose between: deck-local metres under a label nobody could invert, or world
        /// coordinates sliding past at the deck's speed while the pawn's own gait says it is walking. The
        /// second is what the server sees as a movement violation every single tick, so neither is sent.
        /// See <see cref="IsCarrierUsable"/> for what the pawn looks like meanwhile.
        /// </para>
        /// </remarks>
        /// <returns>False when nothing truthful can be said about this pawn's position this tick.</returns>
        private bool TryCaptureState(out PawnState state) {
            bool isGrounded = _actor != null && _actor.IsGrounded;
            bool isCrouching = _crouch != null && _crouch.IsCrouching;
            byte flags = PawnState.PackFlags(ResolveGait(), isCrouching, isGrounded);

            state = new PawnState(
                transform.position.ToNumerics(),
                transform.eulerAngles.y,
                _actor != null ? _actor.Velocity.ToNumerics() : Numerics.Vector3.Zero,
                flags);

            NetCarrier carrier = _carrierSource?.CurrentCarrier;

            if (carrier == null) return true;
            if (!IsCarrierUsable(carrier)) return false;

            state = NetCarrierFrame.ToLocal(in state, carrier);
            return true;
        }

        /// <summary>
        /// Whether a carrier this pawn's game handed out may be named on the wire, reporting once per
        /// carrier when it may not.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Reported rather than passed over in silence because the degraded mode it starts is otherwise
        /// invisible: with no frame to name, the owner sends nothing at all, the authority holds the last
        /// state it received, and every observer — the host included — draws this pawn standing still
        /// wherever that was, however far its game carries the body in the meantime. The owner's own
        /// screen is unaffected, which is exactly why the log line matters.
        /// </para>
        /// <para>
        /// It ends by itself the moment the carrier registers, which the retry in
        /// <see cref="NetCarrier"/> keeps attempting every frame; what it does not end from is a duplicate
        /// id or a scaled carrier nobody fixes, and those are already an error in the log.
        /// </para>
        /// </remarks>
        private bool IsCarrierUsable(NetCarrier carrier) {
            if (carrier.IsRegistered) return true;

            if (_warnedUnusableCarriers.Add(carrier.GetInstanceID())) {
                Debug.LogWarning($"NetActorSync::IsCarrierUsable->{name} is riding '{carrier.name}', which is not registered (id {carrier.CarrierId}); this pawn's updates are withheld and it stays frozen for every observer until that carrier registers.");
            }

            return false;
        }

        /// <summary>
        /// Adopts the pose the shared motor just predicted as the place the actor is being pulled towards.
        /// Nothing is displaced here; <see cref="FollowPrediction"/> does the moving, every frame.
        /// </summary>
        /// <remarks>
        /// <para>
        /// All three axes, including Y. The simulation now stands on the same exported scene geometry the
        /// visible actor does — ramps, steps, platforms and all — so there is no longer a seam whose
        /// vertical had to be left to the actor's own gravity, and letting the prediction own the height
        /// is what makes a pawn walk up a ramp on its owner's screen and the server's alike.
        /// </para>
        /// <para>
        /// Recorded rather than applied because prediction happens on network ticks and the player watches
        /// render frames. Writing the pose here would move the pawn in the sawtooth of the send clock;
        /// recording it and converging continuously spreads the same displacement across every frame in
        /// between.
        /// </para>
        /// </remarks>
        private void ApplyPredictedState(in PawnState predicted) {
            if (applyPredictedYaw) {
                transform.rotation = Quaternion.Euler(0f, predicted.YawDegrees, 0f);
            }

            _predictedState = predicted;
            _hasPredictedState = true;
        }

        /// <summary>
        /// Closes part of the gap between the actor and where the simulation says it is, once per frame,
        /// through the character controller.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This replaces the old teleport-when-far-enough write, which was the source of the owner's
        /// grounded jitter: the gap between actor and prediction is a phase term that breathes with the
        /// send accumulator and crosses any fixed tolerance twice per second at ordinary gaits, so the
        /// pawn spent its life alternating between drifting forward and being yanked back a frame's worth
        /// of travel. A continuous pull has no threshold to cross and therefore nothing to oscillate
        /// about.
        /// </para>
        /// <para>
        /// The step is exponential — <c>1 - exp(-sharpness * dt)</c> — so the same fraction of the gap is
        /// paid off per unit of time whatever the frame rate, and the displacement goes through
        /// <c>CharacterController.Move</c> rather than the transform: a move keeps the controller's
        /// own grounding, its collision and its step offset intact, while disabling the controller to
        /// write a position throws all three away. Only a gap wider than
        /// <see cref="correctionSnapDistance"/> — a rejoin, a respawn, a rejected move — is placed
        /// outright.
        /// </para>
        /// <para>
        /// Owner-simulated pawns are skipped: nothing predicts them, so the recorded state is only ever
        /// the last correction and following it would drag the actor back to where it stood packets ago.
        /// </para>
        /// </remarks>
        private void FollowPrediction() {
            if (!applyPredictedPosition || !_hasPredictedState) return;
            if (_view.Authority != AuthorityMode.Server) return;

            // The residual is what is left of a correction the pawn has not visually paid back yet, so the
            // target is drawn offset by it and the debt shrinks to nothing over the smoothing window.
            Vector3 target = _predictedState.Position.ToUnity() + _correctionResidual;
            Vector3 gap = target - transform.position;

            if (gap.sqrMagnitude > correctionSnapDistance * correctionSnapDistance) {
                _correctionResidual = Vector3.zero;
                Teleport(_predictedState.Position.ToUnity());
                SyncActorMotion(in _predictedState);
                return;
            }

            if (gap.sqrMagnitude < ResidualEpsilon * ResidualEpsilon) return;

            MoveBy(gap * ResolveFollowFraction());
        }

        /// <summary>
        /// The share of the outstanding gap to pay off this frame, from an exponential decay at
        /// <see cref="followSharpness"/> per second. A non-positive sharpness closes the gap whole.
        /// </summary>
        private float ResolveFollowFraction() {
            if (followSharpness <= 0f) return 1f;

            return 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
        }

        /// <summary>
        /// Displaces the actor by a delta the simulation asked for, through the character controller so
        /// collision and grounding survive the write.
        /// </summary>
        private void MoveBy(Vector3 delta) {
            if (_characterController == null || !_characterController.enabled) {
                transform.position += delta;
                return;
            }

            _characterController.Move(delta);
        }

        /// <summary>
        /// Places the actor at a position the simulation decided on, taking the character controller out
        /// of the way first.
        /// </summary>
        /// <remarks>
        /// A <see cref="CharacterController"/> caches its own position and overwrites a bare transform
        /// write on its next move, so a placement applied without this dance is undone within the frame.
        /// The dance is not free — cycling <c>enabled</c> clears the controller's grounding, which the
        /// actor's air model then has to be told to ignore — so it is reserved for genuine placements
        /// beyond <see cref="correctionSnapDistance"/>. Everything smaller goes through
        /// <see cref="MoveBy"/>.
        /// </remarks>
        private void Teleport(Vector3 position) {
            if (_characterController == null) {
                transform.position = position;
                return;
            }

            bool wasEnabled = _characterController.enabled;
            _characterController.enabled = false;
            transform.position = position;
            _characterController.enabled = wasEnabled;
        }

        /// <summary>
        /// Applies the state the client world resolved after rewinding and replaying pending inputs
        /// against the server's verdict.
        /// </summary>
        /// <remarks>
        /// Only a large disagreement is placed outright. Most corrections are centimetres of drift that
        /// prediction and authority will never agree on exactly, and teleporting for those makes a pawn
        /// that twitches every time a packet lands. Anything under
        /// <see cref="correctionSnapDistance" /> is therefore taken on as a residual and walked off over
        /// the smoothing window instead — a rejoin, a teleport or a rejected move is far enough out that
        /// walking it back would look worse than the jump.
        ///
        /// The resolved state also becomes what <see cref="FollowPrediction"/> aims at until the next
        /// send, all three axes of it: it is the client world's best account of where the pawn now is,
        /// and seeding the residual with the whole error means the target starts exactly where the actor
        /// already stands, so nothing moves on the frame the packet lands.
        ///
        /// A correction for a pawn on a carrier comes back in that carrier's frame — the validator's
        /// clamp preserves the frame it measured in — so it is converted before a single number is
        /// treated as a place. One whose carrier this client cannot resolve is dropped whole: the next
        /// tick produces another, while applying it would teleport a rider off a moving train to
        /// wherever the deck's origin coordinates happen to land in the world.
        /// </remarks>
        private void HandleAuthorityCorrected(NetEntity entity, PawnState state) {
            if (entity == null || !_view.IsBound || entity.Id != _view.EntityId) return;

            if (!NetCarrierFrame.TryToWorld(in state, out PawnState world)) {
                WarnOnceForCarrier(state.CarrierId);
                return;
            }

            if (applyPredictedYaw) {
                transform.rotation = Quaternion.Euler(0f, world.YawDegrees, 0f);
            }

            _predictedState = world;
            _hasPredictedState = true;

            Vector3 corrected = world.Position.ToUnity();
            Vector3 error = transform.position - corrected;

            if (!CanSmoothCorrection(error)) {
                _correctionResidual = Vector3.zero;
                Teleport(corrected);
                SyncActorMotion(in world);
                return;
            }

            _correctionResidual = error;
            SyncActorMotion(in world);
        }

        /// <summary>
        /// Reports an unresolvable carrier once per id, so a missing carrier is visible in the log
        /// without a pawn's own frame rate burying it.
        /// </summary>
        private void WarnOnceForCarrier(ushort carrierId) {
            if (!_warnedCarrierIds.Add(carrierId)) return;

            Debug.LogWarning($"NetActorSync::WarnOnceForCarrier->{name} was corrected on carrier {carrierId}, which no loaded carrier answers to; the correction was dropped and the pawn keeps its pose.");
        }

        /// <summary>
        /// Hands the simulation's velocity and grounding to the actor's own integrators, so they carry
        /// on from the state the pawn was just placed in rather than the one it was yanked out of.
        /// </summary>
        private void SyncActorMotion(in PawnState state) {
            if (_actor == null) return;

            _actor.SyncMotionState(state.Velocity.ToUnity(), state.IsGrounded);
        }

        /// <summary>
        /// Whether a correction of this size may be paid back gradually rather than placed.
        /// </summary>
        /// <remarks>
        /// Smoothing needs somewhere to apply the residual, and <see cref="FollowPrediction"/> is the only
        /// place it exists — with <see cref="applyPredictedPosition"/> off nothing here ever moves the
        /// actor except this correction, so the correction has to land whole.
        /// </remarks>
        private bool CanSmoothCorrection(Vector3 error) {
            if (!applyPredictedPosition) return false;
            if (correctionSmoothingSeconds <= 0f) return false;

            return error.sqrMagnitude <= correctionSnapDistance * correctionSnapDistance;
        }

        /// <summary>
        /// Shrinks the outstanding correction debt towards zero, exponentially, so the pawn closes the
        /// last of it slowly rather than arriving with a visible stop.
        /// </summary>
        private void DecayCorrectionResidual() {
            if (_correctionResidual == Vector3.zero) return;

            if (correctionSmoothingSeconds <= 0f) {
                _correctionResidual = Vector3.zero;
                return;
            }

            _correctionResidual *= Mathf.Exp(-Time.deltaTime / correctionSmoothingSeconds);

            if (_correctionResidual.sqrMagnitude > ResidualEpsilon * ResidualEpsilon) return;

            _correctionResidual = Vector3.zero;
        }

        private WireLocomotion ResolveGait() {
            if (_locomotion == null) return WireLocomotion.Walk;

            // The two enumerations are declared in the same order on purpose; the wire one is the
            // engine one's mirror, so the cast is the mapping.
            return (WireLocomotion)(byte)_locomotion.CurrentState;
        }

        private float ResolveSendInterval() {
            NetConfig config = _networkService?.Config;

            return config != null ? config.ClientSendInterval : 1f / 30f;
        }

        private ClientReplication ResolveReplication() {
            return _sessionService?.Replication;
        }

        private void BindReplication(ClientReplication replication) {
            if (ReferenceEquals(_boundReplication, replication)) return;

            UnbindReplication();
            _boundReplication = replication;
            _boundReplication.OnAuthorityCorrected += HandleAuthorityCorrected;
        }

        /// <remarks>
        /// The recorded prediction goes with the world that produced it: a pawn rebound to a fresh client
        /// world would otherwise spend its first frames being pulled towards a position from the previous
        /// session. The spawn placement goes with it for the same reason — the next binding is a new
        /// spawn and gets its own placement.
        /// </remarks>
        private void UnbindReplication() {
            if (_boundReplication == null) return;

            _boundReplication.OnAuthorityCorrected -= HandleAuthorityCorrected;
            _boundReplication = null;
            _hasPredictedState = false;
            _correctionResidual = Vector3.zero;
            _placedForEntityId = 0u;
            _hasSpawnState = false;
            _hasWarnedUnplaceableSpawn = false;
        }
    }
}
