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
    /// The brain that drives somebody else's pawn: samples the interpolator every frame and steers the
    /// possessed actor towards the pose the authority reported, through the same locomotion calls a
    /// player controller uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what makes a single prefab enough for both a local and a remote player. A remote pawn is
    /// not a stripped-down copy with its movement disabled — it is the same actor, possessed by this
    /// controller instead of the game's player controller, and it walks, crouches and turns through
    /// <see cref="Actor.Move"/>, <see cref="LocomotionSystem.SetState"/> and
    /// <see cref="CrouchSystem.SetCrouching"/> exactly as a local one does. Animation, footsteps, noise
    /// and every other system that watches locomotion therefore work on remote pawns for free.
    /// </para>
    /// <para>
    /// Movement is expressed as a direction whose magnitude closes the gap in one frame at the actor's
    /// current speed, rather than by writing the transform: writing it would bypass collision and the
    /// animator both. A pawn that is further out than <see cref="snapDistance"/> — a rejoin, a
    /// teleport, a long stall — is placed outright instead, because walking it back would take longer
    /// than the divergence it is fixing.
    /// </para>
    /// </remarks>
    public class NetController : Controller {
        [Header("Following")]
        [Tooltip("Metres of error beyond which the pawn is placed rather than walked to its reported pose.")]
        [SerializeField] private float snapDistance = 2f;
        [Tooltip("Metres of error below which no movement is requested at all, so a standing pawn does not jitter.")]
        [SerializeField] private float arrivalTolerance = 0.01f;
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

        /// <summary>Actor this controller is currently driving, or null while unpossessed.</summary>
        public Actor Character => _character;

        private Actor _character;
        private NetEntityView _view;
        private LocomotionSystem _locomotion;
        private CrouchSystem _crouch;
        private CharacterController _characterController;
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
            _characterController = character.GetComponent<CharacterController>();
        }

        /// <summary>
        /// Plays a discrete event the authority reported: jumps are applied to the actor, everything
        /// else is handed to whoever is listening.
        /// </summary>
        public void PlayEntityEvent(byte eventId, byte argument) {
            if (eventId == JumpEventId && _character != null) {
                _character.Jump();
            }

            OnEntityEvent?.Invoke(eventId, argument);
        }

        /// <summary>
        /// Places the pawn at a reported pose outright, without walking it there. Used for corrections
        /// too large to absorb and for the keyframe that follows a rejoin.
        /// </summary>
        public void SnapTo(in PawnState state) {
            if (_character == null) return;

            Teleport(state.Position.ToUnity());
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

            DriveTowards(in state);
        }

        /// <summary>
        /// Applies one sampled pose: gait and crouch first, because they decide how fast the actor may
        /// travel, then the movement that closes the gap, then the facing.
        /// </summary>
        private void DriveTowards(in PawnState state) {
            ApplyLocomotionState(in state);

            Vector3 targetPosition = state.Position.ToUnity();
            Vector3 offset = targetPosition - _character.transform.position;

            if (offset.sqrMagnitude > snapDistance * snapDistance) {
                Teleport(targetPosition);
            } else {
                ApplyMovement(offset);
            }

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
        /// Asks the actor to move along the gap, at a fraction of its current speed chosen so this
        /// frame's stride lands on the target instead of overshooting it.
        /// </summary>
        /// <remarks>
        /// Vertical error is left out: the actor's own gravity owns the vertical axis, and driving it
        /// from here would fight every landing. A pawn whose height is genuinely wrong is beyond
        /// <see cref="snapDistance"/> soon enough to be placed.
        /// </remarks>
        private void ApplyMovement(Vector3 offset) {
            offset.y = 0f;

            float distance = offset.magnitude;

            if (distance < arrivalTolerance) return;

            float reachableDistance = ResolveSpeed() * Time.deltaTime;

            if (reachableDistance <= 0f) return;

            float stride = Mathf.Min(distance / reachableDistance, 1f);
            _character.Move(offset / distance * stride);
        }

        private void ApplyFacing(float yawDegrees) {
            Vector3 forward = Quaternion.Euler(0f, yawDegrees, 0f) * Vector3.forward;
            _character.LookAt(_character.transform.position + forward, turnSpeed);
        }

        /// <summary>
        /// Speed the pawn is allowed to close the gap at this frame: the authored top speed of the gait
        /// it is currently in.
        /// </summary>
        /// <remarks>
        /// Taken from the movement profile rather than from the actor's measured velocity because the
        /// measurement lags a frame behind every gait change, and a pawn that has just started
        /// sprinting would be held to its walking speed for that frame. Falls back to the actor's own
        /// measured speed when no profile is configured — an offline scene, or a prefab id with no row.
        /// </remarks>
        private float ResolveSpeed() {
            if (_character == null) return 0f;

            MovementProfile profile = ResolveMovementProfile();

            if (profile != null) {
                return Mathf.Max(profile.GetSpeedForGait(ResolveGaitIndex()), 0.01f);
            }

            Vector3 velocity = _character.Velocity;
            velocity.y = 0f;

            return Mathf.Max(velocity.magnitude, 1f);
        }

        private int ResolveGaitIndex() {
            if (_locomotion == null) return (int)LocomotionState.Walk;

            return (int)_locomotion.CurrentState;
        }

        private MovementProfile ResolveMovementProfile() {
            if (_view == null || !_view.IsBound) return null;

            return _networkService?.Config?.GetMovementProfile(_view.PrefabId);
        }

        /// <remarks>
        /// A <see cref="CharacterController"/> caches its own position and would overwrite a bare
        /// transform write on its next move, so it is taken out of the way for the placement.
        /// </remarks>
        private void Teleport(Vector3 position) {
            if (_characterController == null) {
                _character.transform.position = position;
                return;
            }

            bool wasEnabled = _characterController.enabled;
            _characterController.enabled = false;
            _character.transform.position = position;
            _characterController.enabled = wasEnabled;
        }
    }
}
