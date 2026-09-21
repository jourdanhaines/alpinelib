using System;
using AlpineLib.Actors.Locomotion;
using AlpineLib.Cameras;
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
    /// A character simulated by a fixed-step <see cref="CapsuleMotor"/> and possessed by a
    /// <see cref="Controller"/> brain. The transform is presentation: it holds the pose interpolated
    /// between the last two steps, parented under whatever the actor stands on, while the simulated state
    /// lives in <see cref="MotorState"/> in that carrier's frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The actor owns movement and liveness only. Health, damage reactions and game specific idle
    /// behaviour belong on sibling components, which react to <see cref="OnDeath"/>.
    /// </para>
    /// <para>
    /// Brains hand over intent — a direction, a gait through the stats, a jump — and the motor spends it
    /// on its own clock, at <c>stepRate</c> steps a second, however the render frames fall. A brain that
    /// declares <see cref="Actors.Controller.DrivesPawnExternally"/> places the transform itself through
    /// <see cref="PlaceAt(Vector3, float, Transform)"/> and the motor stands down.
    /// </para>
    /// <para>
    /// The pawn's collider is a <see cref="CapsuleCollider"/> on a kinematic <see cref="Rigidbody"/>: the
    /// motor never asks the physics engine to move it, only to answer sweeps at explicit poses, so the
    /// collider exists for triggers and for other pawns. Its centre is expected at half its height, so
    /// the transform origin is the capsule's foot.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(StatSheet))]
    [DefaultExecutionOrder(ActorExecutionOrder.Motor)]
    public class Actor : MonoBehaviour, IActor, IMortal, ICameraTarget {
        /// <inheritdoc />
        public event Action OnDeath;

        /// <summary>
        /// Raised when the actor starts standing on a different carrier, with the previous and current
        /// carrier roots (null for the world).
        /// </summary>
        public event Action<Transform, Transform> OnCarrierChanged;

        /// <inheritdoc />
        public bool IsAlive { get; private set; } = true;

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
        /// World space velocity, including the motion of whatever carries the actor. From the simulation
        /// for a self-driven actor; measured off the transform for an externally driven one.
        /// </summary>
        public Vector3 Velocity => IsExternallyDriven ? _measuredVelocity : _carrierVelocity + RelativeVelocity;

        /// <summary>
        /// The actor's own velocity in world axes, without the carrier's: what its legs are doing.
        /// </summary>
        public Vector3 RelativeVelocity => IsExternallyDriven
            ? Vector3.zero
            : CarrierFrame.ToWorldPlanar(_current.CarrierRoot, _current.LocalPlanarVelocity) + Vector3.up * _current.VerticalVelocity;

        /// <summary>
        /// The world-space direction the possessing brain asked for this frame through <see cref="Move"/>,
        /// before any of it was executed. Zero on frames nothing drove the actor. Cleared every
        /// <c>LateUpdate</c>.
        /// </summary>
        /// <remarks>
        /// The commanded intent, not the achieved motion — the two differ whenever a wall, a slope or a
        /// correction interferes — and it exists because anything replicating intent must read it from
        /// here rather than derive it from measured velocity.
        /// </remarks>
        public Vector3 CommandedMoveDirection { get; private set; }

        /// <summary>
        /// The simulation's one grounding answer for this step — or, on an externally driven actor,
        /// whatever the driver last declared through <see cref="SetExternalGrounded"/>.
        /// </summary>
        public bool IsGrounded => IsExternallyDriven ? _externalGrounded : _current.Grounded;

        /// <summary>
        /// True while the possessing brain declares that it places the transform itself; the motor stands
        /// down for such a pawn. See <see cref="Actors.Controller.DrivesPawnExternally"/>.
        /// </summary>
        public bool IsExternallyDriven => Brain != null && Brain.DrivesPawnExternally;

        /// <summary>
        /// The kinematic body this actor stands on and is parented under, or null on the ground. Changes
        /// only on landing; a jump keeps the deck it was taken from.
        /// </summary>
        public Transform CarrierRoot => _current.CarrierRoot;

        /// <summary>Where the simulation has the actor's feet, in world space.</summary>
        public Vector3 SimulatedWorldPosition => CarrierFrame.ToWorldPoint(_current.CarrierRoot, _current.LocalPosition);

        /// <summary>
        /// How far above or below level the actor is looking, in degrees; positive looks down, the way
        /// a camera rig reports pitch. Yaw needs no twin: it is the transform's own facing.
        /// </summary>
        /// <remarks>
        /// Held here rather than on a camera so everything that needs it reads one place whoever the
        /// brain is: a player's controller copies its rig, a networked proxy copies the wire, and the
        /// body pose and the replicated state both read it back.
        /// </remarks>
        public float LookPitch { get; private set; }

        public float CapsuleHeight => _capsule != null ? _capsule.height : 0f;
        public float CapsuleRadius => _capsule != null ? _capsule.radius : 0f;

        /// <inheritdoc />
        Transform ICameraTarget.YawFrame => _current.CarrierRoot;

        /// <inheritdoc />
        float ICameraTarget.Height => CapsuleHeight;

        [SerializeField] private bool useRootMotion;

        [Header("Stats")]
        [SerializeField] private StatDefinition moveSpeedStat;
        [SerializeField] private StatDefinition rotationSpeedStat;

        [Header("Gravity")]
        [Tooltip("Downward acceleration in metres per second squared. Negative points at the floor.")]
        [SerializeField] private float gravity = -20f;
        [Tooltip("Upward speed in metres per second applied on the step a jump starts.")]
        [SerializeField] private float jumpSpeed = 4.5f;

        [Header("Air Locomotion")]
        [Tooltip("Horizontal steering acceleration while airborne, in metres per second squared.")]
        [SerializeField] private float airAcceleration = 10f;
        [Tooltip("Exponential decay per second applied to horizontal air velocity while no move input is held.")]
        [SerializeField] private float airDrag;

        [Header("Simulation")]
        [Tooltip("Motor steps per second. The render pose is interpolated between the last two steps.")]
        [SerializeField] private float stepRate = 60f;
        [Tooltip("Most steps one frame may run; time beyond that is dropped rather than caught up.")]
        [SerializeField] private int maxStepsPerFrame = 4;

        [Header("Capsule Motor")]
        [Tooltip("Layers the capsule collides with. May include the pawn's own layer; the pawn's own collider is always skipped.")]
        [SerializeField] private LayerMask collisionMask = ~0;
        [Tooltip("Metres every sweep stops short of geometry.")]
        [SerializeField] private float skinWidth = 0.02f;
        [Tooltip("Tallest edge a grounded actor rides over, and how far down it follows a step.")]
        [SerializeField] private float stepOffset = 0.3f;
        [Tooltip("Steepest surface that counts as ground, in degrees.")]
        [SerializeField] private float slopeLimit = 45f;
        [Tooltip("How far below the feet an airborne actor looks for a landing each step.")]
        [SerializeField] private float landingProbeDistance = 0.05f;

        [Header("Animator Parameters")]
        [SerializeField] private string speedParameter = "Speed";
        [SerializeField] private string turnParameter = "Turn";
        [SerializeField] private string jumpParameter = "Jump";
        [Tooltip("Seconds of damping applied to the speed parameter, so digital keys ease a blend tree instead of snapping it.")]
        [SerializeField] private float speedDampTime = 0.12f;

        /// <summary>Furthest a look can pitch either way, in degrees: straight up or straight down.</summary>
        public const float MaxLookPitch = 90f;

        private const string StrafeXParameter = "StrafeX";
        private const string StrafeYParameter = "StrafeY";
        private const string GroundedParameter = "Grounded";
        private const float StrafeDampTime = 0.1f;

        private CapsuleCollider _capsule;
        private Rigidbody _body;
        private PhysicsCapsuleQuery _query;
        private CapsuleMotor _motor;
        private MotorState _previous;
        private MotorState _current;
        private float _accumulator;
        private Vector3 _moveDirection;
        private bool _jumpLatched;
        private Vector3 _pendingNudgeLocal;
        private float _pendingNudgeSeconds;
        private Vector3 _carrierVelocity;
        private Vector3 _carrierPreviousPosition;
        private bool _hasCarrierPrevious;
        private Vector3 _measuredVelocity;
        private Vector3 _previousRenderPosition;
        private int _speedParameterHash;
        private int _turnParameterHash;
        private int _jumpParameterHash;
        private int _strafeXHash;
        private int _strafeYHash;
        private int _groundedParameterHash;
        private bool _hasStrafeParameters;
        private bool _hasGroundedParameter;
        private float _currentSpeed;
        private float _currentTurn;
        private Vector2 _currentStrafe;
        private bool _isLocomotionSuppressed;
        private bool _isRotationLocked;
        private bool _externalGrounded;

        /// <remarks>
        /// Component references are resolved here rather than in <c>Start</c> so systems configuring a
        /// freshly spawned actor can reach <see cref="Animator"/> and <see cref="Stats"/> immediately. The
        /// simulated state starts wherever the transform was placed, in the world frame.
        /// </remarks>
        protected virtual void Awake() {
            _speedParameterHash = UnityEngine.Animator.StringToHash(speedParameter);
            _turnParameterHash = UnityEngine.Animator.StringToHash(turnParameter);
            _jumpParameterHash = UnityEngine.Animator.StringToHash(jumpParameter);
            _strafeXHash = UnityEngine.Animator.StringToHash(StrafeXParameter);
            _strafeYHash = UnityEngine.Animator.StringToHash(StrafeYParameter);
            _groundedParameterHash = UnityEngine.Animator.StringToHash(GroundedParameter);

            Stats = GetComponent<StatSheet>();
            _capsule = GetComponent<CapsuleCollider>();
            _body = GetComponent<Rigidbody>();
            _body.isKinematic = true;
            _body.useGravity = false;
            _body.interpolation = RigidbodyInterpolation.None;
            _query = new PhysicsCapsuleQuery(collisionMask, _capsule, IsCarrierBody);
            _motor = new CapsuleMotor(_query);

            _current = MotorState.AtWorld(transform.position, transform.eulerAngles.y);
            _previous = _current;
            _previousRenderPosition = transform.position;

            Animator = GetComponentInChildren<Animator>();
            if (Animator == null) return;

            Animator.applyRootMotion = useRootMotion;
            _hasStrafeParameters = DeclaresStrafeParameters();
            _hasGroundedParameter = DeclaresGroundedParameter();

            if (useRootMotion && Animator.gameObject != gameObject && Animator.GetComponent<RootMotionForwarder>() == null) {
                Animator.gameObject.AddComponent<RootMotionForwarder>();
            }
        }

        /// <summary>
        /// A kinematic body is a carrier unless it is another actor: standing on a pawn is a collision, not
        /// a ride.
        /// </summary>
        private static bool IsCarrierBody(Rigidbody body) {
            return body.GetComponent<Actor>() == null;
        }

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

        private bool DeclaresGroundedParameter() {
            if (Animator.runtimeAnimatorController == null) return false;

            foreach (AnimatorControllerParameter parameter in Animator.parameters) {
                if (parameter.name == GroundedParameter) return true;
            }

            return false;
        }

        /// <remarks>
        /// Runs after the brains have handed over this frame's intent and after every carrier has written
        /// its transform, so the physics scene is synced once and the steps sweep against colliders that
        /// are where they are drawn.
        /// </remarks>
        private void Update() {
            if (!IsAlive) return;

            if (IsExternallyDriven) {
                MeasureCarrierVelocity(Time.deltaTime);
                return;
            }

            PhysicsTransformSync.EnsureSyncedThisFrame();
            Simulate(Time.deltaTime);
        }

        /// <summary>
        /// Advances the motor by a span of time in whole steps and writes the interpolated render pose.
        /// Public so a gate can drive the actor without a player loop; the carrier's velocity is measured
        /// over the same span.
        /// </summary>
        public void Simulate(float deltaTime) {
            MeasureCarrierVelocity(deltaTime);
            float interval = StepInterval();
            _accumulator = Mathf.Min(_accumulator + deltaTime, Mathf.Max(maxStepsPerFrame, 1) * interval);

            while (_accumulator >= interval) {
                RunStep(interval);
                _accumulator -= interval;
            }

            WriteRenderPose(_accumulator / interval);
        }

        private float StepInterval() {
            return 1f / Mathf.Max(stepRate, 1f);
        }

        private void RunStep(float deltaTime) {
            _previous = _current;
            Transform carrierBefore = _current.CarrierRoot;
            MotorInput input = BuildInput(deltaTime);
            MotorSettings settings = BuildSettings();

            _motor.Step(ref _current, in input, in settings, _carrierVelocity, deltaTime);
            _jumpLatched = false;

            if (_current.JumpedThisStep) TriggerJumpAnimation();
            if (!ReferenceEquals(_current.CarrierRoot, carrierBefore)) AdoptCarrier(carrierBefore, _current.CarrierRoot);
        }

        private MotorInput BuildInput(float deltaTime) {
            return new MotorInput {
                MoveDirection = _moveDirection,
                MoveSpeed = Stats.Get(moveSpeedStat),
                Jump = _jumpLatched,
                PendingDisplacement = TakeNudge(deltaTime)
            };
        }

        private MotorSettings BuildSettings() {
            return new MotorSettings {
                Capsule = new CapsuleShape(_capsule.radius, _capsule.height),
                Gravity = gravity,
                JumpSpeed = jumpSpeed,
                AirAcceleration = airAcceleration,
                AirDrag = airDrag,
                StepOffset = stepOffset,
                SlopeLimitDegrees = slopeLimit,
                SkinWidth = skinWidth,
                LandingProbeDistance = landingProbeDistance
            };
        }

        /// <summary>
        /// The share of the outstanding nudge this step pays back, in world space.
        /// </summary>
        private Vector3 TakeNudge(float deltaTime) {
            if (_pendingNudgeLocal == Vector3.zero) return Vector3.zero;

            float fraction = _pendingNudgeSeconds <= deltaTime ? 1f : deltaTime / _pendingNudgeSeconds;
            Vector3 portion = _pendingNudgeLocal * fraction;
            _pendingNudgeLocal -= portion;
            _pendingNudgeSeconds = Mathf.Max(_pendingNudgeSeconds - deltaTime, 0f);
            if (fraction >= 1f) _pendingNudgeLocal = Vector3.zero;

            return CarrierFrame.ToWorldPlanar(_current.CarrierRoot, portion);
        }

        /// <summary>
        /// Moves the transform under the new carrier and puts the previous state into the same frame, so
        /// the interpolation across the switch is exact between two carriers and a single-step snap
        /// between a carrier and the world.
        /// </summary>
        private void AdoptCarrier(Transform previous, Transform current) {
            if (previous != null && current != null) {
                Vector3 world = CarrierFrame.ToWorldPoint(previous, _previous.LocalPosition);
                float worldYaw = CarrierFrame.Heading(previous) + _previous.LocalYaw;
                _previous.LocalPosition = CarrierFrame.ToLocalPoint(current, world);
                _previous.LocalYaw = worldYaw - CarrierFrame.Heading(current);
                _previous.LocalPlanarVelocity = CarrierFrame.ToLocalPlanar(current, CarrierFrame.ToWorldPlanar(previous, _previous.LocalPlanarVelocity));
                _previous.CarrierRoot = current;
            } else {
                _previous = _current;
            }

            transform.SetParent(current, true);
            _hasCarrierPrevious = false;
            _carrierVelocity = Vector3.zero;
            OnCarrierChanged?.Invoke(previous, current);
        }

        private void WriteRenderPose(float alpha) {
            Transform root = _current.CarrierRoot;
            Vector3 local = Vector3.Lerp(_previous.LocalPosition, _current.LocalPosition, Mathf.Clamp01(alpha));
            float yaw = CarrierFrame.Heading(root) + _current.LocalYaw;
            transform.SetPositionAndRotation(CarrierFrame.ToWorldPoint(root, local), Quaternion.Euler(0f, yaw, 0f));
        }

        /// <summary>
        /// The carrier's world velocity over the last frame, measured the same way the networking side
        /// measures it, so the two agree about what part of the actor's motion is the ride.
        /// </summary>
        private void MeasureCarrierVelocity(float deltaTime) {
            Transform root = _current.CarrierRoot;
            if (root == null) {
                _carrierVelocity = Vector3.zero;
                _hasCarrierPrevious = false;
                return;
            }

            Vector3 position = root.position;
            if (!_hasCarrierPrevious || deltaTime <= 0f) {
                _carrierPreviousPosition = position;
                _hasCarrierPrevious = true;
                _carrierVelocity = Vector3.zero;
                return;
            }

            _carrierVelocity = (position - _carrierPreviousPosition) / deltaTime;
            _carrierPreviousPosition = position;
        }

        protected virtual void LateUpdate() {
            if (!_isLocomotionSuppressed) {
                WriteLocomotionParameters();
            }

            float deltaTime = Time.deltaTime;
            _measuredVelocity = deltaTime > 0f ? (transform.position - _previousRenderPosition) / deltaTime : Vector3.zero;
            _previousRenderPosition = transform.position;

            _currentSpeed = 0f;
            _currentTurn = 0f;
            _currentStrafe = Vector2.zero;
            _moveDirection = Vector3.zero;
            CommandedMoveDirection = Vector3.zero;
        }

        private void WriteLocomotionParameters() {
            if (Animator == null) return;

            Animator.SetFloat(_speedParameterHash, _currentSpeed, speedDampTime, Time.deltaTime);
            Animator.SetFloat(_turnParameterHash, _currentTurn);

            if (_hasGroundedParameter) {
                Animator.SetBool(_groundedParameterHash, IsGrounded);
            }

            if (!_hasStrafeParameters) return;

            Animator.SetFloat(_strafeXHash, _currentStrafe.x, StrafeDampTime, Time.deltaTime);
            Animator.SetFloat(_strafeYHash, _currentStrafe.y, StrafeDampTime, Time.deltaTime);
        }

        /// <summary>
        /// Asks for a jump on the next step. Taken only while alive and grounded; an externally driven
        /// actor plays the animation alone, its arc arriving through whoever places it.
        /// </summary>
        /// <returns>True when the jump was latched.</returns>
        public bool Jump() {
            if (!IsAlive) return false;

            if (IsExternallyDriven) {
                TriggerJumpAnimation();
                return false;
            }

            if (!_current.Grounded) return false;

            _jumpLatched = true;
            return true;
        }

        private void TriggerJumpAnimation() {
            if (Animator == null) return;

            Animator.SetTrigger(_jumpParameterHash);
        }

        /// <summary>
        /// Applies one frame of animator root motion, scaled by the ratio between the actor's current and
        /// base move speed, as a swept displacement on the next step.
        /// </summary>
        public void ApplyRootMotion(Vector3 deltaPosition) {
            float baseSpeed = Stats.GetBase(moveSpeedStat);
            float effectiveSpeed = Stats.Get(moveSpeedStat);
            float speedRatio = baseSpeed > 0f ? effectiveSpeed / baseSpeed : 1f;

            Nudge(deltaPosition * speedRatio, 0f);
        }

        /// <summary>
        /// Asks the motor to move along a world space direction, at the current move speed, until told
        /// otherwise this frame. Reports the intent to the animator.
        /// </summary>
        public virtual void Move(Vector3 direction) {
            if (!IsAlive) return;

            CommandedMoveDirection = direction;
            Vector3 planar = new Vector3(direction.x, 0f, direction.z);
            _moveDirection = useRootMotion ? Vector3.zero : Vector3.ClampMagnitude(planar, 1f);

            float baseSpeed = Stats.GetBase(moveSpeedStat);
            float effectiveSpeed = Stats.Get(moveSpeedStat);
            RecordLocomotionIntent(direction, baseSpeed > 0f ? effectiveSpeed / baseSpeed : 1f);
        }

        private void RecordLocomotionIntent(Vector3 direction, float speedRatio) {
            _currentSpeed = direction.magnitude * speedRatio;

            Vector3 localDirection = Quaternion.Inverse(transform.rotation) * direction;
            _currentStrafe = new Vector2(localDirection.x, localDirection.z) * speedRatio;
        }

        /// <summary>
        /// Declares where the actor is looking above or below level; read back through
        /// <see cref="LookPitch"/>. Clamped to straight up and straight down.
        /// </summary>
        public void SetLookPitch(float pitchDegrees) {
            LookPitch = Mathf.Clamp(pitchDegrees, -MaxLookPitch, MaxLookPitch);
        }

        /// <summary>
        /// Declares whether an externally driven actor is standing on ground; read back through
        /// <see cref="IsGrounded"/>.
        /// </summary>
        public void SetExternalGrounded(bool isGrounded) {
            _externalGrounded = isGrounded;
        }

        /// <summary>
        /// Publishes locomotion intent to the animator without moving anything: the external driver's way
        /// of making the legs match motion it has already applied to the transform.
        /// </summary>
        public void AnimateLocomotion(Vector3 worldDirection, float speedRatio) {
            RecordLocomotionIntent(worldDirection, speedRatio);
        }

        /// <summary>
        /// Overwrites the motor's velocities and grounding with an authoritative account, leaving the
        /// position alone, so the simulation carries on from what it was told rather than what it had.
        /// </summary>
        /// <param name="velocity">The actor's own velocity in world axes, without any carrier's.</param>
        public void SyncMotionState(Vector3 velocity, bool isGrounded) {
            Vector3 planar = new Vector3(velocity.x, 0f, velocity.z);
            _current.LocalPlanarVelocity = CarrierFrame.ToLocalPlanar(_current.CarrierRoot, planar);
            _current.VerticalVelocity = isGrounded ? 0f : velocity.y;
            _current.Grounded = isGrounded;
            _current.GroundNormal = Vector3.up;
            _previous.LocalPlanarVelocity = _current.LocalPlanarVelocity;
            _previous.VerticalVelocity = _current.VerticalVelocity;
            _previous.Grounded = isGrounded;
        }

        /// <summary>
        /// Places the actor outright at a world pose on a carrier (null for the world) and restarts the
        /// simulation there: no interpolation crosses a placement. For an externally driven actor this is
        /// the per-frame write.
        /// </summary>
        public void PlaceAt(Vector3 worldPosition, float yawDegrees, Transform carrierRoot) {
            Transform root = CarrierFrame.IsUsable(carrierRoot) ? carrierRoot : null;
            Transform previousRoot = _current.CarrierRoot;

            _current.CarrierRoot = root;
            _current.LocalPosition = CarrierFrame.ToLocalPoint(root, worldPosition);
            _current.LocalYaw = yawDegrees - CarrierFrame.Heading(root);
            _current.LocalPlanarVelocity = Vector3.zero;
            _current.VerticalVelocity = 0f;
            _current.Grounded = false;
            _current.GroundNormal = Vector3.up;
            _current.JumpedThisStep = false;
            _previous = _current;
            _pendingNudgeLocal = Vector3.zero;
            _pendingNudgeSeconds = 0f;

            if (!ReferenceEquals(root, previousRoot)) {
                transform.SetParent(root, true);
                _hasCarrierPrevious = false;
                _carrierVelocity = Vector3.zero;
                OnCarrierChanged?.Invoke(previousRoot, root);
            }

            transform.SetPositionAndRotation(worldPosition, Quaternion.Euler(0f, yawDegrees, 0f));
        }

        /// <summary>Places the actor at a world position on a carrier, keeping its facing.</summary>
        public void PlaceAt(Vector3 worldPosition, Transform carrierRoot) {
            PlaceAt(worldPosition, transform.eulerAngles.y, carrierRoot);
        }

        /// <summary>
        /// Displaces the actor by a world-space delta, swept in over the coming steps so it cannot pass
        /// through a wall. Zero seconds pays it back whole on the next step. Stored in the carrier's frame,
        /// so a nudge on a turning deck stays deck-relative.
        /// </summary>
        public void Nudge(Vector3 worldDelta, float overSeconds) {
            _pendingNudgeLocal += CarrierFrame.ToLocalPlanar(_current.CarrierRoot, worldDelta);
            _pendingNudgeSeconds = Mathf.Max(_pendingNudgeSeconds, overSeconds);
        }

        /// <summary>
        /// Leaves the carrier without moving: the state is re-expressed in the world and the transform
        /// unparented. For a game about to destroy the thing the actor stands on.
        /// </summary>
        public void DetachFromCarrier() {
            Transform root = _current.CarrierRoot;
            if (root == null) return;

            Vector3 world = CarrierFrame.ToWorldPoint(root, _current.LocalPosition);
            float yaw = CarrierFrame.Heading(root) + _current.LocalYaw;
            Vector3 planar = CarrierFrame.ToWorldPlanar(root, _current.LocalPlanarVelocity) + _carrierVelocity;
            _current.CarrierRoot = null;
            _current.LocalPosition = world;
            _current.LocalYaw = yaw;
            _current.LocalPlanarVelocity = planar;
            _previous = _current;
            _hasCarrierPrevious = false;
            _carrierVelocity = Vector3.zero;
            transform.SetParent(null, true);
            OnCarrierChanged?.Invoke(root, null);
        }

        /// <summary>
        /// Resizes the capsule, keeping its foot planted, so a crouch shrinks it from the top.
        /// </summary>
        public void SetCapsuleHeight(float height) {
            if (_capsule == null) return;

            float clamped = Mathf.Max(height, _capsule.radius * 2f);
            _capsule.height = clamped;
            _capsule.center = Vector3.up * (clamped * 0.5f);
        }

        /// <summary>True when the capsule could grow to the given height where it stands.</summary>
        public bool HasHeadroom(float height) {
            if (_motor == null) return true;

            CapsuleShape current = new CapsuleShape(_capsule.radius, _capsule.height);
            return _motor.HasHeadroom(SimulatedWorldPosition, in current, height - _capsule.height, skinWidth);
        }

        /// <summary>
        /// Turns the actor towards a world position at its rotation speed stat, ignoring height.
        /// </summary>
        public virtual void LookAt(Vector3 position) {
            LookAt(position, Stats.Get(rotationSpeedStat));
        }

        /// <summary>
        /// Turns the actor towards a world position at an explicit maximum turn rate, ignoring height.
        /// Facing is written to the transform at once and mirrored into the simulation, because the
        /// camera owns it and never wants it interpolated. Rotation locks still win.
        /// </summary>
        public virtual void LookAt(Vector3 position, float maxDegreesPerSecond) {
            if (_isRotationLocked) return;

            Vector3 lookDirection = position - transform.position;
            lookDirection.y = 0;
            if (lookDirection.sqrMagnitude <= 0.01f) return;

            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
            float signedAngle = Vector3.SignedAngle(transform.forward, lookDirection, Vector3.up);
            _currentTurn = Mathf.Clamp(signedAngle / 90f, -1f, 1f);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, maxDegreesPerSecond * Time.deltaTime);

            float localYaw = transform.eulerAngles.y - CarrierFrame.Heading(_current.CarrierRoot);
            _current.LocalYaw = localYaw;
            _previous.LocalYaw = localYaw;
        }

        /// <summary>
        /// Hands control of this actor to a brain. The previous brain is simply replaced.
        /// </summary>
        public virtual void Possess(Controller controller) {
            Brain = controller;
        }

        /// <summary>
        /// Drops the brain. An actor that was placed externally resumes simulating from wherever the
        /// driver left its transform, on the carrier it was placed under.
        /// </summary>
        public virtual void ReleaseControl() {
            bool wasExternal = IsExternallyDriven;
            Brain = null;
            if (!wasExternal) return;

            ResumeFromTransform();
        }

        private void ResumeFromTransform() {
            Transform root = _current.CarrierRoot;
            _current.LocalPosition = CarrierFrame.ToLocalPoint(root, transform.position);
            _current.LocalYaw = transform.eulerAngles.y - CarrierFrame.Heading(root);
            _current.LocalPlanarVelocity = Vector3.zero;
            _current.VerticalVelocity = 0f;
            _current.Grounded = _externalGrounded;
            _current.GroundNormal = Vector3.up;
            _previous = _current;
            _accumulator = 0f;
            _pendingNudgeLocal = Vector3.zero;
            _pendingNudgeSeconds = 0f;
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
        /// Kills the actor: simulation and collision are switched off, any brain is released and
        /// <see cref="OnDeath"/> is raised once. The corpse stays parented, riding whatever it fell on.
        /// </summary>
        public void Kill() {
            if (!IsAlive) return;

            IsAlive = false;
            foreach (Collider collider in GetComponentsInChildren<Collider>()) {
                collider.enabled = false;
            }

            ReleaseControl();
            OnDeath?.Invoke();
        }
    }
}
