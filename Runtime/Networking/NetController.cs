using System;
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
    /// Position, though, is written to the transform outright. The interpolated stream is already
    /// smooth, already collision-resolved by the authority, and already the truth; walking the actor
    /// toward it through its own motor re-integrates that truth through a second movement model — one
    /// whose grounded flag flickers, whose gravity writes the same controller, and whose closing speed
    /// caps into a deadband — and every one of those seams is visible as vibration. The actor's own
    /// integrators stand down while this brain possesses it (see
    /// <see cref="Controller.DrivesPawnExternally"/>); the animator is fed from the wire state instead
    /// of from displacement.
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

        /// <summary>Actor this controller is currently driving, or null while unpossessed.</summary>
        public Actor Character => _character;

        /// <inheritdoc />
        /// <remarks>The whole point of this brain: the interpolator owns the transform.</remarks>
        public override bool DrivesPawnExternally => true;

        private Actor _character;
        private NetEntityView _view;
        private LocomotionSystem _locomotion;
        private CrouchSystem _crouch;
        private ISessionService _sessionService;
        private INetworkService _networkService;

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
        public void SnapTo(in PawnState state) {
            if (_character == null) return;

            _character.transform.position = state.Position.ToUnity();
            _character.transform.rotation = Quaternion.Euler(0f, state.YawDegrees, 0f);
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
            if (!replication.SampleRemote(_view.EntityId, out PawnState state)) return;

            Drive(in state);
        }

        /// <summary>
        /// Applies one sampled pose: gait and crouch first, then the transform, then grounding and the
        /// animator's view of the motion.
        /// </summary>
        private void Drive(in PawnState state) {
            ApplyLocomotionState(in state);

            _character.transform.position = state.Position.ToUnity();
            _character.SetExternalGrounded(state.IsGrounded);

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
        /// Feeds the animator the motion the wire reports — direction of travel and speed as a fraction
        /// of the current gait — rather than any locally measured displacement. Measured displacement of
        /// an externally placed pawn is the chase error of whatever wrote the transform last, and legs
        /// driven by it flicker between idle and locomotion.
        /// </summary>
        private void AnimateFromState(in PawnState state) {
            Vector3 horizontalVelocity = state.HorizontalVelocity.ToUnity();
            float speed = horizontalVelocity.magnitude;

            if (speed < RestSpeedThreshold) {
                _character.AnimateLocomotion(Vector3.zero, 0f);
                return;
            }

            float gaitSpeed = ResolveGaitSpeed(in state);
            float speedRatio = gaitSpeed > 0f ? Mathf.Clamp01(speed / gaitSpeed) : 1f;

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

        private MovementProfile ResolveMovementProfile() {
            if (_view == null || !_view.IsBound) return null;

            return _networkService?.Config?.GetMovementProfile(_view.PrefabId);
        }
    }
}
