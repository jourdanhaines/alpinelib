using System;
using System.Collections.Generic;
using AlpineLib.Actors;
using AlpineLib.Actors.Locomotion;
using AlpineLib.DI;
using AlpineLib.Netcode.Protocol;
using AlpineLib.Netcode.Replication;
using AlpineLib.Sessions;
using UnityEngine;

namespace AlpineLib.Networking {
    /// <summary>
    /// The brain that drives somebody else's pawn: samples the interpolator every frame and places the
    /// possessed actor exactly on the pose the authority reported.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what makes a single prefab enough for both a local and a remote player. A remote pawn is
    /// not a stripped-down copy with its movement disabled — it is the same actor, possessed by this
    /// controller instead of the game's player controller. Gait and crouch still flow through
    /// <see cref="LocomotionSystem.SetState"/> and <see cref="CrouchSystem.SetCrouching"/>, and the
    /// animator still reads locomotion intent, so footsteps, noise and every system that watches
    /// locomotion works on remote pawns for free.
    /// </para>
    /// <para>
    /// Position, though, is placed outright through <see cref="Actor.PlaceAt(Vector3, Transform)"/>.
    /// The interpolated stream is already smooth, already collision-resolved by the authority, and
    /// already the truth; walking the actor toward it through its own motor would re-integrate that
    /// truth through a second movement model, and every seam between the two is visible as vibration.
    /// The actor's motor stands down while this brain possesses it (see
    /// <see cref="Controller.DrivesPawnExternally"/>); the animator is fed from the wire state instead
    /// of from displacement. Placing rather than writing the transform also parents the pawn under the
    /// carrier it rides, so a frame that produces no pose — a carrier not yet resolved — leaves it
    /// riding the deck at its last deck-local pose instead of standing still in the world while the
    /// deck moves on.
    /// </para>
    /// <para>
    /// <b>Carrier frames are resolved here.</b> A pawn riding a train replicates in that train's local
    /// frame, so the sampled state is metres from a deck's origin rather than the world's. Every sample
    /// is put back into world space through the carrier the state names, once per frame, because the
    /// carrier has moved since the snapshot was taken and the rider must be drawn on the deck as it is
    /// now — not where the deck was when the packet left. A state naming a carrier this client cannot
    /// resolve yet — the window between a join's first snapshot and the consist being built, or a car
    /// streamed out mid-session — leaves the pawn exactly where it already stood. Holding a stale pose
    /// for a few frames is invisible; drawing deck-local metres as world coordinates would put every
    /// rider on a train near the world origin and snap them back, on every single join.
    /// </para>
    /// <para>
    /// <b>A co-operative-play trust limitation, stated plainly.</b> The server accepts whatever carrier
    /// id a client claims — it does not know where any carrier is, by design — so an owner-authoritative
    /// client can claim an id nothing in the session holds. Every observer then holds that pawn's last
    /// pose for as long as the claim lasts, while the claiming client draws itself wherever it likes: a
    /// pawn that is a stationary decoy to everyone but its owner. That is not fixed here, and it is not
    /// fixable here — the server would have to know where carriers are. What this component does instead
    /// is stop the hold being silent: <see cref="IsCarrierUnresolved"/> goes true once a pawn has been
    /// held for <see cref="UnresolvedCarrierGraceSeconds"/>, long enough that ordinary join and streaming
    /// windows never raise it, so a game that cares can mark, hide or report such a pawn on its own
    /// terms. Adequate for the co-operative sessions this library is built for; not, as it stands, for
    /// play among strangers.
    /// </para>
    /// </remarks>
    [DefaultExecutionOrder(NetExecutionOrder.PawnDrivers)]
    public class NetController : Controller {
        [Header("Following")]
        [Tooltip("Degrees per second the pawn may turn towards its reported facing.")]
        [SerializeField] private float turnSpeed = 720f;

        /// <summary>
        /// Raised for every discrete event the authority reported for this pawn — jumps, emotes,
        /// anything a game defines — with the event id and its argument.
        /// </summary>
        /// <remarks>
        /// The library deliberately assigns no meaning to the ids: a game maps them onto its own
        /// animator triggers and effects. Jump is the one exception the base handles, because the jump
        /// impulse belongs to the actor rather than to any game's vocabulary.
        /// </remarks>
        public event Action<byte, byte> OnEntityEvent;

        /// <summary>Event id the actor's own jump is reported under.</summary>
        public const byte JumpEventId = 1;

        /// <summary>Speed below which a pawn is animated as standing rather than travelling, in m/s.</summary>
        public const float RestSpeedThreshold = 0.05f;

        /// <summary>
        /// How long this pawn may be held on an unresolvable carrier before
        /// <see cref="IsCarrierUnresolved"/> admits it.
        /// </summary>
        /// <remarks>
        /// Comfortably longer than the honest windows — a join's first snapshot arriving before the
        /// scene's carriers register, a car streamed out and back — so raising it means something has
        /// genuinely gone wrong rather than that a frame or two went by.
        /// </remarks>
        public const float UnresolvedCarrierGraceSeconds = 2f;

        /// <summary>Actor this controller is currently driving, or null while unpossessed.</summary>
        public Actor Character => _character;

        /// <summary>
        /// True while this pawn has been stuck on a carrier id nothing answers to for longer than
        /// <see cref="UnresolvedCarrierGraceSeconds"/> — it is frozen at its last pose and staying there.
        /// </summary>
        /// <remarks>
        /// Exposed rather than acted on, because what a game should do about it is a game's decision: hide
        /// the pawn, tag it in a debug overlay, tell the host. See the trust-limitation note on the type
        /// for how a pawn gets into this state and why the library cannot get it out.
        /// </remarks>
        public bool IsCarrierUnresolved { get; private set; }

        /// <inheritdoc />
        /// <remarks>The whole point of this brain: the interpolator owns the transform.</remarks>
        public override bool DrivesPawnExternally => true;

        private Actor _character;
        private NetEntityView _view;
        private LocomotionSystem _locomotion;
        private CrouchSystem _crouch;
        private ISessionService _sessionService;
        private INetworkService _networkService;
        private float _heldSinceTime;
        private bool _isHeld;
        private readonly HashSet<ushort> _warnedCarrierIds = new HashSet<ushort>();

        /// <inheritdoc />
        public override void Possess(Actor character) {
            if (character == null) {
                Debug.LogError("NetController::Possess->No actor to possess.");
                return;
            }

            if (_character != null) {
                _character.ReleaseControl();
            }

            _character = character;
            _character.Possess(this);
            _isHeld = false;
            IsCarrierUnresolved = false;

            _view = character.GetComponent<NetEntityView>();
            _locomotion = character.GetComponent<LocomotionSystem>();
            _crouch = character.GetComponent<CrouchSystem>();
        }

        /// <summary>
        /// Plays a discrete event the authority reported: jumps are applied to the actor, everything
        /// else is handed to whoever is listening.
        /// </summary>
        /// <remarks>
        /// The jump here is animation only — an externally driven actor integrates no vertical velocity,
        /// so <see cref="Actor.Jump"/> reduces to its animator trigger while the arc itself arrives
        /// through the replicated positions.
        /// </remarks>
        public void PlayEntityEvent(byte eventId, byte argument) {
            if (eventId == JumpEventId && _character != null) {
                _character.Jump();
            }

            OnEntityEvent?.Invoke(eventId, argument);
        }

        /// <summary>
        /// Places the pawn at a reported pose outright. Used for the keyframe that follows a rejoin and
        /// for any caller holding an authoritative pose outside the interpolated stream.
        /// </summary>
        /// <remarks>
        /// A pose whose carrier is not loaded is not a pose, so nothing is placed and the pawn keeps
        /// what it had. The interpolated stream will place it the moment the carrier registers.
        ///
        /// Resolved without the stream's clock: <see cref="IsCarrierUnresolved"/> answers "is the
        /// replicated stream stuck", and one scripted call with an unresolvable frame is not evidence
        /// that it is. The warning is still throttled the same way.
        ///
        /// Nothing in the library calls this — a remote pawn is placed by <c>Update</c> every frame, and
        /// an owned one is placed by <see cref="NetActorSync"/>, which possesses it instead. It is here
        /// for a game holding an authoritative pose of its own: a scripted teleport, a cutscene start.
        /// </remarks>
        public void SnapTo(in PawnState state) {
            if (_character == null) return;

            if (!NetCarrierFrame.TryToWorld(in state, out PawnState world)) {
                WarnOnceForCarrier(state.CarrierId);
                return;
            }

            _character.PlaceAt(world.Position.ToUnity(), world.YawDegrees, ResolveCarrierRoot(state.CarrierId));
        }

        private static Transform ResolveCarrierRoot(ushort carrierId) {
            return NetCarrierRegistry.TryResolve(carrierId, out NetCarrier carrier) ? carrier.transform : null;
        }

        /// <summary>
        /// Puts a sampled state into world space, warning once per unresolvable carrier id.
        /// </summary>
        /// <remarks>
        /// The conversion itself lives on <see cref="NetCarrierFrame"/> so that every consumer of an
        /// inbound state resolves the same way; what is local to a controller is the warning, which is
        /// throttled per id because a pawn's own frame rate would otherwise bury the log.
        /// </remarks>
        /// <returns>False when the state named a carrier no loaded object answers to.</returns>
        private bool TryResolveWorldFrame(in PawnState state, out PawnState world) {
            if (NetCarrierFrame.TryToWorld(in state, out world)) {
                _isHeld = false;
                IsCarrierUnresolved = false;
                return true;
            }

            WarnOnceForCarrier(state.CarrierId);
            NoteHeldOnUnresolvedCarrier();
            return false;
        }

        /// <summary>
        /// Runs the clock on a pawn held for want of a carrier, raising <see cref="IsCarrierUnresolved"/>
        /// once the grace has passed.
        /// </summary>
        /// <remarks>
        /// Timed rather than flagged on the first failure because the first failures are all honest: a
        /// join whose snapshot beats the consist, a car streamed out for a moment. What the grace
        /// separates out is a hold that is not going to end.
        ///
        /// On unscaled time, because what is being measured is how long the network has been naming
        /// something this client cannot resolve: packets keep arriving through a game paused with a
        /// <c>timeScale</c> write, and a clock that stopped with them would never raise the flag.
        /// </remarks>
        private void NoteHeldOnUnresolvedCarrier() {
            if (!_isHeld) {
                _isHeld = true;
                _heldSinceTime = Time.unscaledTime;
                return;
            }

            IsCarrierUnresolved = Time.unscaledTime - _heldSinceTime >= UnresolvedCarrierGraceSeconds;
        }

        private void WarnOnceForCarrier(ushort carrierId) {
            if (!_warnedCarrierIds.Add(carrierId)) return;

            Debug.LogWarning($"NetController::WarnOnceForCarrier->{name} sampled a state on carrier {carrierId}, which no loaded carrier answers to; holding the pawn's last pose until it registers.");
        }

        /// <remarks>
        /// Resolved through <see cref="Injector.TryResolve{T}"/> rather than injected: a scene opened
        /// with no networking installed must still load a prefab carrying this component, where it
        /// simply never finds a session and drives nothing.
        /// </remarks>
        private void Start() {
            if (!Injector.HasInstance) return;

            Injector.Instance.TryResolve(out _sessionService);
            Injector.Instance.TryResolve(out _networkService);
        }

        private void Update() {
            if (_character == null) return;
            if (!_character.IsAlive) return;
            if (_view == null || !_view.IsBound) return;

            ClientReplication replication = _sessionService?.Replication;

            if (replication == null) return;
            if (ReleaseIfLocallyOwned(replication)) return;
            if (!replication.SampleRemote(_view.EntityId, out PawnState sampled)) return;

            bool wasCarrierRelative = sampled.IsCarrierRelative;
            ushort carrierId = sampled.CarrierId;

            if (!TryResolveWorldFrame(in sampled, out PawnState state)) return;

            // A pawn standing on a mover drawn off the interpolation timeline (the local player is
            // riding it, so the platform renders at the predicted tick) must be re-anchored by the same
            // offset, or it trails across the deck by the interpolation delay's worth of travel. Only
            // the drawn position moves; velocity, yaw and flags stay the sampled ones. A carrier's rider
            // is already anchored to the carrier's own transform, so the mover probe has nothing to add.
            if (!wasCarrierRelative && replication.TryGetMoverRenderOffset(in state, out System.Numerics.Vector3 moverOffset)) {
                state.Position += moverOffset;
            }

            Drive(in state, ResolveCarrierRoot(carrierId));
        }

        /// <summary>
        /// Refuses to drive a pawn this client owns, handing the body back and standing down.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Only the spawner decides which brain a pawn gets, and it decides from the same ownership flag
        /// this asks about — so reaching this is a bug, and one that hides well. An owned pawn driven from
        /// here is placed on the interpolated stream, which lags the owner's own prediction by the render
        /// delay: the pawn still walks, still animates, and simply feels heavy, while every input the
        /// player gives is overwritten a frame later. Reporting it as an error rather than quietly
        /// correcting it is deliberate, because the mispossession itself is what needs fixing.
        /// </para>
        /// <para>
        /// Control is released rather than merely dropped. Leaving the actor possessed by a disabled brain
        /// would keep <see cref="DrivesPawnExternally"/> true and its own integrators stood down, which
        /// turns a pawn that felt heavy into one that cannot move at all; releasing lets the game's
        /// possession flow hand it to the controller it should have had.
        /// </para>
        /// </remarks>
        /// <returns>True when the pawn was owned and this controller has stood down.</returns>
        private bool ReleaseIfLocallyOwned(ClientReplication replication) {
            if (!replication.IsOwned(_view.Entity)) return false;

            Debug.LogError($"NetController::ReleaseIfLocallyOwned->{name} was possessing entity {_view.EntityId}, which this client owns; releasing it and disabling this brain.");

            _character.ReleaseControl();
            _character = null;
            enabled = false;

            // The hold belongs to the pawn that was being driven, not to this brain: a pooled controller
            // that kept the flag would report a stream stuck on a pawn it no longer touches.
            _isHeld = false;
            IsCarrierUnresolved = false;
            return true;
        }

        /// <summary>
        /// Applies one sampled pose: gait and crouch first, then the transform, then grounding and the
        /// animator's view of the motion.
        /// </summary>
        private void Drive(in PawnState state, Transform carrierRoot) {
            ApplyLocomotionState(in state);

            _character.PlaceAt(state.Position.ToUnity(), carrierRoot);
            _character.SetExternalGrounded(state.IsGrounded);
            _character.SetLookPitch(state.LookPitchDegrees);

            AnimateFromState(in state);
            ApplyFacing(state.YawDegrees);
        }

        private void ApplyLocomotionState(in PawnState state) {
            if (_locomotion != null) {
                // The wire gaits mirror the engine ones in the same order, so the cast is the mapping.
                _locomotion.SetState((LocomotionState)(byte)state.Locomotion);
            }

            if (_crouch == null) return;

            _crouch.SetCrouching(state.IsCrouching);
        }

        /// <summary>
        /// Feeds the animator the motion the wire reports — direction of travel and speed as a multiple
        /// of the walk speed, the scale a locally moved actor reports — rather than any locally measured
        /// displacement. Measured displacement of an externally placed pawn is the chase error of
        /// whatever wrote the transform last, and legs driven by it flicker between idle and locomotion.
        /// </summary>
        private void AnimateFromState(in PawnState state) {
            Vector3 horizontalVelocity = state.HorizontalVelocity.ToUnity();
            float speed = horizontalVelocity.magnitude;

            if (speed < RestSpeedThreshold) {
                _character.AnimateLocomotion(Vector3.zero, 0f);
                return;
            }

            float walkSpeed = ResolveWalkSpeed();
            float gaitSpeed = ResolveGaitSpeed(in state);
            float reportedSpeed = gaitSpeed > 0f ? Mathf.Min(speed, gaitSpeed) : speed;
            float speedRatio = walkSpeed > 0f ? reportedSpeed / walkSpeed : 1f;

            _character.AnimateLocomotion(horizontalVelocity / speed * speedRatio, 1f);
        }

        private void ApplyFacing(float yawDegrees) {
            Vector3 forward = Quaternion.Euler(0f, yawDegrees, 0f) * Vector3.forward;
            _character.LookAt(_character.transform.position + forward, turnSpeed);
        }

        /// <summary>
        /// Top speed of the gait the sampled state is in, from the movement profile; falls back to the
        /// sampled speed itself — ratio one — when no profile is configured for this prefab.
        /// </summary>
        private float ResolveGaitSpeed(in PawnState state) {
            MovementProfile profile = ResolveMovementProfile();

            if (profile == null) return 0f;

            return profile.GetSpeedForGait((int)state.Locomotion);
        }

        /// <summary>
        /// Speed the animator reads as ratio one; zero when no profile is configured for this prefab.
        /// </summary>
        private float ResolveWalkSpeed() {
            MovementProfile profile = ResolveMovementProfile();

            if (profile == null) return 0f;

            return profile.GetSpeedForGait((int)WireLocomotion.Walk);
        }

        private MovementProfile ResolveMovementProfile() {
            if (_view == null || !_view.IsBound) return null;

            return _networkService?.Config?.GetMovementProfile(_view.PrefabId);
        }
    }
}
