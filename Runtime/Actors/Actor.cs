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
    /// animator every frame through the speed and turn parameters.
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

        [SerializeField] private bool useRootMotion;

        [Header("Stats")]
        [SerializeField] private StatDefinition moveSpeedStat;
        [SerializeField] private StatDefinition rotationSpeedStat;

        [Header("Animator Parameters")]
        [SerializeField] private string speedParameter = "Speed";
        [SerializeField] private string turnParameter = "Turn";

        private int _speedParameterHash;
        private int _turnParameterHash;
        private float _currentSpeed;
        private float _currentTurn;
        private Vector3 _previousPosition;
        private bool _isLocomotionSuppressed;
        private bool _isRotationLocked;

        protected virtual void Awake() {
            _speedParameterHash = UnityEngine.Animator.StringToHash(speedParameter);
            _turnParameterHash = UnityEngine.Animator.StringToHash(turnParameter);
        }

        protected virtual void Start() {
            Controller = GetComponent<CharacterController>();
            Controller.minMoveDistance = 0f;
            Stats = GetComponent<StatSheet>();

            _previousPosition = transform.position;

            Animator = GetComponentInChildren<Animator>();
            if (Animator != null) {
                Animator.applyRootMotion = useRootMotion;
                if (useRootMotion && Animator.gameObject != gameObject && Animator.GetComponent<RootMotionForwarder>() == null) {
                    Animator.gameObject.AddComponent<RootMotionForwarder>();
                }
            }
        }

        protected virtual void LateUpdate() {
            if (!_isLocomotionSuppressed) {
                Animator.SetFloat(_speedParameterHash, _currentSpeed);
                Animator.SetFloat(_turnParameterHash, _currentTurn);
            }

            Velocity = (transform.position - _previousPosition) / Time.deltaTime;
            _previousPosition = transform.position;

            _currentSpeed = 0f;
            _currentTurn = 0f;
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
        /// to the animator. With root motion enabled the animation supplies the displacement instead.
        /// </summary>
        public virtual void Move(Vector3 direction) {
            if (!IsAlive) return;

            float baseSpeed = Stats.GetBase(moveSpeedStat);
            float effectiveSpeed = Stats.Get(moveSpeedStat);

            if (!useRootMotion) {
                Controller.Move(direction * (Time.deltaTime * effectiveSpeed));
            }

            _currentSpeed = direction.magnitude * (effectiveSpeed / baseSpeed);
        }

        /// <summary>
        /// Turns the actor towards a world position at its rotation speed, ignoring height.
        /// </summary>
        public virtual void LookAt(Vector3 position) {
            if (_isRotationLocked) return;

            Vector3 lookDirection = position - transform.position;
            lookDirection.y = 0;

            if (lookDirection.sqrMagnitude > 0.01f) {
                Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
                float signedAngle = Vector3.SignedAngle(transform.forward, lookDirection, Vector3.up);
                _currentTurn = Mathf.Clamp(signedAngle / 90f, -1f, 1f);
                float rotationSpeed = Stats.Get(rotationSpeedStat);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
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
        /// Makes <see cref="LookAt"/> a no-op until <see cref="UnlockRotation"/> is called.
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
