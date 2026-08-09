using System;
using AlpineLib.Stats;
using UnityEngine;

namespace AlpineLib.Actors {
    /// <summary>
    /// Minimal contract a possessing <see cref="Controller"/> drives an actor through.
    /// </summary>
    public interface IActor {
        void Move(Vector3 direction);
        void LookAt(Vector3 position);
        void Possess(Controller controller);
        void ReleaseControl();
    }

    /// <summary>
    /// A <see cref="CharacterController"/> driven character that can be possessed by a
    /// <see cref="Controller"/> brain. Movement is either integrated in code or delivered by animator
    /// root motion scaled to the actor's current move speed, and locomotion intent is published to the
    /// animator every frame through the speed and turn parameters, plus the strafe axes when the
    /// controller declares them.
    /// </summary>
    /// <remarks>
    /// The actor owns movement and liveness only. Health, damage reactions and game specific idle
    /// behaviour belong on sibling components, which react to <see cref="OnDeath"/> rather than being
    /// switched off from here.
    /// </remarks>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(StatSheet))]
    public class Actor : MonoBehaviour, IActor, IMortal {
        /// <inheritdoc />
        public event Action OnDeath;

        /// <inheritdoc />
        public bool IsAlive { get; private set; } = true;

        protected CharacterController Controller;

        /// <summary>
        /// Controller currently possessing this actor, or null while it is unpossessed.
        /// </summary>
        public Controller Brain { get; protected set; }

        /// <summary>
        /// Animator driving this actor, taken from the first one found in the hierarchy.
        /// </summary>
        public Animator Animator { get; protected set; }

        /// <summary>
        /// Stats this actor reads its movement speeds from.
        /// </summary>
        public StatSheet Stats { get; private set; }

        /// <summary>
        /// World space velocity measured over the last frame.
        /// </summary>
        public Vector3 Velocity { get; private set; }

        /// <summary>
        /// True while the <see cref="CharacterController"/> reports ground contact from its last move.
        /// </summary>
        /// <remarks>
        /// False for every frame before <c>Start</c> has resolved the controller, so callers polling this
        /// from their own <c>Awake</c> see "airborne" rather than a null reference.
        /// </remarks>
        public bool IsGrounded => Controller != null && Controller.isGrounded;

        [SerializeField] private bool useRootMotion;

        [Header("Stats")]
        [SerializeField] private StatDefinition moveSpeedStat;
        [SerializeField] private StatDefinition rotationSpeedStat;

        [Header("Gravity")]
        [Tooltip("Downward acceleration in metres per second squared. Negative points at the floor.")]
        [SerializeField] private float gravity = -20f;
        [Tooltip("Upward speed in metres per second applied on the frame a jump starts.")]
        [SerializeField] private float jumpSpeed = 4.5f;

        [Header("Animator Parameters")]
        [SerializeField] private string speedParameter = "Speed";
        [SerializeField] private string turnParameter = "Turn";
        [SerializeField] private string jumpParameter = "Jump";

        private const string StrafeXParameter = "StrafeX";
        private const string StrafeYParameter = "StrafeY";
        private const float StrafeDampTime = 0.1f;

        private int _speedParameterHash;
        private int _turnParameterHash;
        private int _jumpParameterHash;
        private int _strafeXHash;
        private int _strafeYHash;
        private bool _hasStrafeParameters;
        private float _currentSpeed;
        private float _currentTurn;
        private Vector2 _currentStrafe;
        private float _verticalVelocity;
        private Vector3 _previousPosition;
        private bool _isLocomotionSuppressed;
        private bool _isRotationLocked;

        /// <remarks>
        /// Component references are resolved here rather than in <c>Start</c> so systems configuring a
        /// freshly spawned actor — equipment, passives, loadout appliers — can reach
        /// <see cref="Animator"/> and <see cref="Stats"/> immediately. Spawners commonly instantiate
        /// and configure an actor from another component's <c>Start</c>, which runs before this
        /// actor's own <c>Start</c> would have.
        /// </remarks>
        protected virtual void Awake() {
            _speedParameterHash = UnityEngine.Animator.StringToHash(speedParameter);
            _turnParameterHash = UnityEngine.Animator.StringToHash(turnParameter);
            _jumpParameterHash = UnityEngine.Animator.StringToHash(jumpParameter);
            _strafeXHash = UnityEngine.Animator.StringToHash(StrafeXParameter);
            _strafeYHash = UnityEngine.Animator.StringToHash(StrafeYParameter);

            Controller = GetComponent<CharacterController>();
            Controller.minMoveDistance = 0f;
            Stats = GetComponent<StatSheet>();

            _previousPosition = transform.position;

            Animator = GetComponentInChildren<Animator>();
            if (Animator == null) return;

            Animator.applyRootMotion = useRootMotion;
            _hasStrafeParameters = DeclaresStrafeParameters();

            if (useRootMotion && Animator.gameObject != gameObject && Animator.GetComponent<RootMotionForwarder>() == null) {
                Animator.gameObject.AddComponent<RootMotionForwarder>();
            }
        }

        /// <summary>
        /// Reports whether the resolved animator's controller declares both strafe parameters.
        /// </summary>
        /// <remarks>
        /// Resolved once, at the moment the animator is resolved, because
        /// <see cref="UnityEngine.Animator.parameters"/> allocates and scanning it every frame would be
        /// wasteful. Controllers that do not declare the pair — zombies, and every actor in games that
        /// never blend a strafe — are then never written to, so Unity never logs a missing-parameter
        /// warning for them. Animators with no controller assigned report no parameters and are treated
        /// the same way.
        /// </remarks>
        private bool DeclaresStrafeParameters() {
            if (Animator.runtimeAnimatorController == null) return false;

            bool hasStrafeX = false;
            bool hasStrafeY = false;

            foreach (AnimatorControllerParameter parameter in Animator.parameters) {
                hasStrafeX |= parameter.name == StrafeXParameter;
                hasStrafeY |= parameter.name == StrafeYParameter;
            }

            return hasStrafeX && hasStrafeY;
        }

        protected virtual void LateUpdate() {
            ApplyGravity();

            if (!_isLocomotionSuppressed) {
                WriteLocomotionParameters();
            }

            Velocity = (transform.position - _previousPosition) / Time.deltaTime;
            _previousPosition = transform.position;

            _currentSpeed = 0f;
            _currentTurn = 0f;
            _currentStrafe = Vector2.zero;
        }

        /// <summary>
        /// Publishes this frame's locomotion intent — speed, turn and, where the controller declares
        /// them, the local-space strafe axes — to the animator.
        /// </summary>
        /// <remarks>
        /// The strafe axes are damped rather than written raw: they are the only parameters a controller
        /// blends in two dimensions, and an undamped write makes the blend tree pop when the movement
        /// direction flips relative to a facing the camera is steering. Speed and turn keep their
        /// existing undamped writes so nothing that already tunes damping inside the controller changes
        /// behaviour.
        /// </remarks>
        private void WriteLocomotionParameters() {
            Animator.SetFloat(_speedParameterHash, _currentSpeed);
            Animator.SetFloat(_turnParameterHash, _currentTurn);

            if (!_hasStrafeParameters) return;

            Animator.SetFloat(_strafeXHash, _currentStrafe.x, StrafeDampTime, Time.deltaTime);
            Animator.SetFloat(_strafeYHash, _currentStrafe.y, StrafeDampTime, Time.deltaTime);
        }

        /// <summary>
        /// Integrates vertical velocity and feeds it to the controller once per frame, so actors fall off
        /// ledges and land instead of walking on air.
        /// </summary>
        /// <remarks>
        /// Grounded actors are parked at a small negative velocity rather than zero: a
        /// <see cref="CharacterController"/> only reports <c>isGrounded</c> after a move that pushes it
        /// into the floor, so a true zero makes ground contact flicker on slopes and steps. The
        /// controller reference is still guarded for objects whose <c>Awake</c> has not run; dead
        /// actors are skipped because <see cref="Kill"/> disables the controller entirely.
        /// </remarks>
        private void ApplyGravity() {
            if (!IsAlive) return;
            if (Controller == null) return;

            if (Controller.isGrounded && _verticalVelocity < 0f) {
                _verticalVelocity = -2f;
            } else {
                _verticalVelocity += gravity * Time.deltaTime;
            }

            Controller.Move(Vector3.up * (_verticalVelocity * Time.deltaTime));
        }

        /// <summary>
        /// Launches the actor upwards at <c>jumpSpeed</c>, if it is alive and standing on something.
        /// </summary>
        /// <remarks>
        /// The jump trigger is only ever set from here, so animator controllers that do not declare the
        /// parameter — zombies and every actor in games that never call this — never see it and never log
        /// a missing-parameter warning.
        /// </remarks>
        public void Jump() {
            if (!IsAlive) return;
            if (!IsGrounded) return;

            _verticalVelocity = jumpSpeed;

            if (Animator == null) return;
            Animator.SetTrigger(_jumpParameterHash);
        }

        /// <summary>
        /// Applies one frame of animator root motion, scaled by the ratio between the actor's current
        /// and base move speed so that stat changes speed the same animation up or down.
        /// </summary>
        public void ApplyRootMotion(Vector3 deltaPosition) {
            float baseSpeed = Stats.GetBase(moveSpeedStat);
            float effectiveSpeed = Stats.Get(moveSpeedStat);
            float speedRatio = baseSpeed > 0f ? effectiveSpeed / baseSpeed : 1f;

            Controller.Move(deltaPosition * speedRatio);
        }

        /// <summary>
        /// Moves the actor along a world space direction for one frame and reports the resulting speed
        /// and strafe direction to the animator. With root motion enabled the animation supplies the
        /// displacement instead.
        /// </summary>
        public virtual void Move(Vector3 direction) {
            if (!IsAlive) return;

            float baseSpeed = Stats.GetBase(moveSpeedStat);
            float effectiveSpeed = Stats.Get(moveSpeedStat);

            if (!useRootMotion) {
                Controller.Move(direction * (Time.deltaTime * effectiveSpeed));
            }

            RecordLocomotionIntent(direction, baseSpeed > 0f ? effectiveSpeed / baseSpeed : 1f);
        }

        /// <summary>
        /// Displaces the actor along a world space direction in code at its current move speed, whether
        /// or not the actor is configured for root motion, and reports the resulting locomotion intent
        /// to the animator.
        /// </summary>
        /// <remarks>
        /// This exists for skill stages that must steer or carry momentum while a non-locomotion clip is
        /// playing: such a clip either has no root motion to give or has motion authored for a different
        /// speed, so the displacement has to come from code even on an actor whose ordinary movement is
        /// root driven. It is not a replacement for <see cref="Move"/> — calling it for regular
        /// locomotion on a root motion actor double-drives the controller, since root motion is still
        /// being forwarded through <see cref="ApplyRootMotion"/> in the same frame.
        ///
        /// Guarded against a dead actor and an unresolved controller so a stage that outlives the actor
        /// — a skill still ticking on the frame the actor is killed, or one driven before <c>Awake</c>
        /// has run — degrades to doing nothing rather than throwing.
        /// </remarks>
        public void MoveKinematic(Vector3 direction) {
            if (!IsAlive) return;
            if (Controller == null) return;

            float baseSpeed = Stats.GetBase(moveSpeedStat);
            float effectiveSpeed = Stats.Get(moveSpeedStat);

            Controller.Move(direction * (effectiveSpeed * Time.deltaTime));

            RecordLocomotionIntent(direction, baseSpeed > 0f ? effectiveSpeed / baseSpeed : 1f);
        }

        /// <summary>
        /// Stores this frame's locomotion intent for <c>LateUpdate</c> to publish: overall speed, and the
        /// same direction resolved into the actor's local space for the strafe axes.
        /// </summary>
        /// <remarks>
        /// Both values are scaled by the ratio between effective and base move speed so a stat buff
        /// pushes the blend tree further into its faster gaits rather than only moving the actor
        /// further. The strafe axes are recorded even when the controller does not declare them; the
        /// write is what is gated, which keeps this shared between <see cref="Move"/> and
        /// <see cref="MoveKinematic"/> without either needing to know about the animator.
        /// </remarks>
        private void RecordLocomotionIntent(Vector3 direction, float speedRatio) {
            _currentSpeed = direction.magnitude * speedRatio;

            Vector3 localDirection = Quaternion.Inverse(transform.rotation) * direction;
            _currentStrafe = new Vector2(localDirection.x, localDirection.z) * speedRatio;
        }

        /// <summary>
        /// Turns the actor towards a world position at its rotation speed stat, ignoring height.
        /// </summary>
        public virtual void LookAt(Vector3 position) {
            LookAt(position, Stats.Get(rotationSpeedStat));
        }

        /// <summary>
        /// Turns the actor towards a world position at an explicit maximum turn rate, ignoring height.
        /// </summary>
        /// <remarks>
        /// The core implementation both <see cref="LookAt(Vector3)"/> and rotation capped callers run
        /// through. Skill stages use it to let the camera keep steering an attack's facing while capping
        /// how far that facing may swing per second, which the rotation speed stat alone cannot express:
        /// the stat is the actor's own agility, the cap is the stage's authored commitment. Passing zero
        /// freezes the facing while still reporting the turn intent to the animator.
        ///
        /// Rotation locks still win, so a system that has taken the facing with
        /// <see cref="LockRotation"/> is not overridden by a stage steering underneath it.
        /// </remarks>
        public virtual void LookAt(Vector3 position, float maxDegreesPerSecond) {
            if (_isRotationLocked) return;

            Vector3 lookDirection = position - transform.position;
            lookDirection.y = 0;

            if (lookDirection.sqrMagnitude > 0.01f) {
                Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
                float signedAngle = Vector3.SignedAngle(transform.forward, lookDirection, Vector3.up);
                _currentTurn = Mathf.Clamp(signedAngle / 90f, -1f, 1f);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, maxDegreesPerSecond * Time.deltaTime);
            }
        }

        /// <summary>
        /// Hands control of this actor to a brain. The previous brain is simply replaced.
        /// </summary>
        public virtual void Possess(Controller controller) {
            Brain = controller;
        }

        public virtual void ReleaseControl() {
            Brain = null;
        }

        /// <summary>
        /// Stops animator locomotion parameters from being written, so another system can own the
        /// animator state until <see cref="ResumeLocomotion"/> is called.
        /// </summary>
        public void SuppressLocomotion() {
            _isLocomotionSuppressed = true;
        }

        public void ResumeLocomotion() {
            _isLocomotionSuppressed = false;
        }

        /// <summary>
        /// Makes <see cref="LookAt(Vector3)"/> and <see cref="LookAt(Vector3, float)"/> no-ops until
        /// <see cref="UnlockRotation"/> is called.
        /// </summary>
        public void LockRotation() {
            _isRotationLocked = true;
        }

        public void UnlockRotation() {
            _isRotationLocked = false;
        }

        /// <summary>
        /// Kills the actor: movement and collision are switched off, any brain is released and
        /// <see cref="OnDeath"/> is raised once. Subsequent calls do nothing.
        /// </summary>
        public void Kill() {
            if (!IsAlive) return;
            IsAlive = false;
            Controller.enabled = false;
            foreach (var collider in GetComponentsInChildren<Collider>())
                collider.enabled = false;
            ReleaseControl();
            OnDeath?.Invoke();
        }

        protected virtual void FixedUpdate() { }
    }
}
