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
        [Tooltip("Metres the actor may differ from the prediction before it is moved. Below this the write is skipped so the two do not fight over millimetres.")]
        [SerializeField] private float positionTolerance = 0.05f;

        [Header("Corrections")]
        [Tooltip("Metres the server's verdict may differ from the actor before it is placed outright. Below this the difference is paid back gradually instead of popping.")]
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
        /// offset nobody could see for the rest of the session.
        /// </summary>
        private const float ResidualEpsilon = 0.001f;

        private NetEntityView _view;
        private Actor _actor;
        private CharacterController _characterController;
        private LocomotionSystem _locomotion;
        private CrouchSystem _crouch;
        private INetworkService _networkService;
        private ISessionService _sessionService;
        private ClientReplication _boundReplication;
        private float _sendAccumulatorSeconds;
        private bool _jumpQueued;
        private Vector3 _correctionResidual;

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

        private void Update() {
            DecayCorrectionResidual();

            ClientReplication replication = ResolveReplication();

            if (replication == null) return;
            if (!_view.IsBound || !_view.IsOwned) return;

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
                ApplyDeferredJump();
                replication.SubmitOwnerPawnState(_view.EntityId, CaptureState());
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
        /// penguin actually play the jump, because a remote pawn is driven from interpolated state and an
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

        /// <summary>Reads the actor's current pose as an authoritative state, for owner-simulated pawns.</summary>
        private PawnState CaptureState() {
            bool isGrounded = _actor != null && _actor.IsGrounded;
            bool isCrouching = _crouch != null && _crouch.IsCrouching;
            byte flags = PawnState.PackFlags(ResolveGait(), isCrouching, isGrounded);

            return new PawnState(
                transform.position.ToNumerics(),
                transform.eulerAngles.y,
                _actor != null ? _actor.Velocity.ToNumerics() : Numerics.Vector3.Zero,
                flags);
        }

        /// <summary>
        /// Moves the actor onto the predicted pose, skipping writes smaller than
        /// <see cref="positionTolerance"/>.
        /// </summary>
        /// <remarks>
        /// Horizontal only. The simulation stands on the ground seam — a flat plane until a real
        /// heightfield provider exists — while the visible actor stands on actual scene geometry, so its
        /// own gravity and collision own the vertical axis. Writing the simulated Y here would teleport
        /// the pawn into or above every floor the seam does not know about, thirty times a second.
        /// </remarks>
        private void ApplyPredictedState(in PawnState predicted) {
            if (applyPredictedYaw) {
                transform.rotation = Quaternion.Euler(0f, predicted.YawDegrees, 0f);
            }

            if (!applyPredictedPosition) return;

            // The residual is what is left of a correction the pawn has not visually paid back yet, so the
            // prediction is drawn offset by it and the debt shrinks to nothing over the smoothing window.
            Vector3 predictedPosition = predicted.Position.ToUnity() + _correctionResidual;
            predictedPosition.y = transform.position.y;

            if ((predictedPosition - transform.position).sqrMagnitude < positionTolerance * positionTolerance) return;

            Teleport(predictedPosition);
            SyncActorMotion(in predicted);
        }

        /// <summary>
        /// Places the actor at a position the simulation decided on, taking the character controller out
        /// of the way first.
        /// </summary>
        /// <remarks>
        /// A <see cref="CharacterController"/> caches its own position and overwrites a bare transform
        /// write on its next move, so a correction applied without this dance is undone within the
        /// frame.
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
        /// </remarks>
        private void HandleAuthorityCorrected(NetEntity entity, PawnState state) {
            if (entity == null || !_view.IsBound || entity.Id != _view.EntityId) return;

            if (applyPredictedYaw) {
                transform.rotation = Quaternion.Euler(0f, state.YawDegrees, 0f);
            }

            Vector3 corrected = state.Position.ToUnity();
            corrected.y = transform.position.y;
            Vector3 error = transform.position - corrected;

            if (!CanSmoothCorrection(error)) {
                _correctionResidual = Vector3.zero;
                Teleport(corrected);
                SyncActorMotion(in state);
                return;
            }

            _correctionResidual = error;
            SyncActorMotion(in state);
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
        /// Smoothing needs somewhere to apply the residual, and the predicted write is the only place it
        /// exists — with <see cref="applyPredictedPosition"/> off nothing here ever moves the actor except
        /// this correction, so the correction has to land whole.
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

        private void UnbindReplication() {
            if (_boundReplication == null) return;

            _boundReplication.OnAuthorityCorrected -= HandleAuthorityCorrected;
            _boundReplication = null;
        }
    }
}
