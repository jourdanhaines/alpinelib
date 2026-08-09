using System;
using AlpineLib.Actors;
using AlpineLib.Body;
using AlpineLib.Perception;
using AlpineLib.Utilities;
using UnityEngine;

namespace AlpineLib.Combat {
    /// <summary>
    /// Drives melee attacks off the animator: fires an attack's trigger, opens and closes its
    /// <see cref="HitBox"/> inside the attack's normalized-time damage window, enforces the cooldown
    /// and the per-attack rotation budget, and resolves whatever the hit box connects with.
    /// </summary>
    /// <remarks>
    /// The state machine tracks the animator rather than a timer, so attacks stay in sync with the
    /// clip that is actually playing. The animator must reach a state tagged "Attack" from the
    /// attack's trigger; the attack ends when that state is left or its normalized time passes 1.
    /// Locomotion is suppressed for the duration and root motion is skipped through
    /// <see cref="IRootMotionSuppressor"/>, so attack animations move the actor only if their own
    /// clip does.
    /// </remarks>
    [RequireComponent(typeof(Actor))]
    public class CombatSystem : ActorSubsystem, IRootMotionSuppressor, IHitBoxOwner {
        [Tooltip("Attacks this actor can perform, indexed in order")]
        [SerializeField] private AttackDefinition[] attacks;

        [Tooltip("Hit box opened during the damage window")]
        [SerializeField] private HitBox hitBox;

        /// <summary>
        /// True from the moment an attack starts until it finishes or is cancelled.
        /// </summary>
        public bool IsAttacking { get; private set; }

        /// <summary>
        /// Degrees of turn the current attack still allows. Zero when no attack is running, so
        /// controllers can use it to decide whether they may keep tracking a moving target.
        /// </summary>
        public float RemainingAttackRotation => _currentAttack != null ? _currentAttack.maxRotation - _rotationUsed : 0f;

        /// <inheritdoc />
        public bool IsSuppressingRootMotion => IsAttacking;

        /// <summary>
        /// Raised when an attack begins, before any damage window opens.
        /// </summary>
        public event Action<AttackDefinition> OnAttackStarted;

        /// <summary>
        /// Raised for each hit that lands, after the injury has been applied.
        /// </summary>
        public event Action<AttackDefinition, HurtBox> OnAttackHit;

        /// <summary>
        /// Raised when an attack ends, whether it landed, missed, or was cancelled.
        /// </summary>
        public event Action<AttackDefinition> OnAttackFinished;

        private Actor _actor;
        private AttackDefinition _currentAttack;
        private float _cooldownTimer;
        private bool _hitBoxActive;
        private bool _hasLanded;
        private bool _enteredAttackState;
        private float _rotationUsed;
        private Quaternion _lastRotation;

        protected override void Start() {
            base.Start();

            _actor = GetComponent<Actor>();
            if (hitBox != null)
                hitBox.Init(this);
        }

        protected override void OnOwnerDeath() {
            CancelAttack();
            base.OnOwnerDeath();
        }

        private void Update() {
            if (_cooldownTimer > 0f)
                _cooldownTimer -= Time.deltaTime;

            if (!IsAttacking) return;

            float delta = Quaternion.Angle(_lastRotation, transform.rotation);
            _rotationUsed += delta;
            _lastRotation = transform.rotation;

            var stateInfo = _actor.Animator.GetCurrentAnimatorStateInfo(0);
            bool inAttackState = stateInfo.IsTag("Attack");

            if (inAttackState)
                _enteredAttackState = true;

            if (_enteredAttackState && !inAttackState) {
                FinishAttack();
                return;
            }

            if (!inAttackState) return;

            float t = stateInfo.normalizedTime % 1f;

            if (!_hasLanded && !_hitBoxActive && t >= _currentAttack.damageWindowStart && t < _currentAttack.damageWindowEnd) {
                _hitBoxActive = true;
                hitBox.Activate();
            } else if (_hitBoxActive && t >= _currentAttack.damageWindowEnd) {
                _hitBoxActive = false;
                hitBox.Deactivate();
            }

            if (stateInfo.normalizedTime >= 1f)
                FinishAttack();
        }

        /// <summary>
        /// Starts the given attack if nothing is already running and the cooldown has elapsed.
        /// </summary>
        /// <returns>True when the attack started.</returns>
        public bool StartAttack(AttackDefinition attack) {
            if (IsAttacking) return false;
            if (_cooldownTimer > 0f) return false;
            if (attack == null) return false;

            _currentAttack = attack;
            IsAttacking = true;
            _hitBoxActive = false;
            _hasLanded = false;
            _enteredAttackState = false;

            _rotationUsed = 0f;
            _lastRotation = transform.rotation;

            _actor.SuppressLocomotion();
            _actor.Animator.SetTrigger(attack.animationTrigger);

            if (attack.attackNoiseRadius > 0f)
                NoiseEmitter.Emit(gameObject, transform.position, attack.attackNoiseRadius);

            OnAttackStarted?.Invoke(attack);
            return true;
        }

        /// <summary>
        /// Starts one of the serialized attacks by index.
        /// </summary>
        /// <returns>True when the attack started; false when the index is out of range.</returns>
        public bool StartAttack(int index = 0) {
            if (index < 0 || index >= attacks.Length) return false;
            return StartAttack(attacks[index]);
        }

        /// <summary>
        /// Called by <see cref="HitBox"/> for every hurt box the live damage window overlaps.
        /// Rolls an outcome, applies the injury to the struck body, and closes the window.
        /// </summary>
        public void OnHitBoxContact(HurtBox hurtBox) {
            if (hurtBox.Owner.transform.root == transform.root) return;

            var outcome = WeightedRandom.Pick(_currentAttack.outcomes, candidate => candidate.weight);

            if (outcome.injuryDefinition == null) return;

            var injury = new Injury(outcome.injuryDefinition, outcome.severity);
            var bodySystem = hurtBox.GetComponentInParent<BodySystem>();
            if (bodySystem != null)
                bodySystem.ApplyInjury(hurtBox.BodyPart, injury, outcome.baseDamage);

            hurtBox.Flash();
            hurtBox.GetComponentInParent<IHitReceiver>()?.NotifyHit(gameObject, hurtBox.BodyPart);
            OnAttackHit?.Invoke(_currentAttack, hurtBox);

            _hasLanded = true;
            _hitBoxActive = false;
            hitBox.Deactivate();
        }

        /// <summary>
        /// Interrupts the running attack, if any. The cooldown still applies.
        /// </summary>
        public void CancelAttack() {
            if (!IsAttacking) return;
            FinishAttack();
        }

        private void FinishAttack() {
            if (_hitBoxActive) {
                _hitBoxActive = false;
                hitBox.Deactivate();
            }

            IsAttacking = false;
            _actor.ResumeLocomotion();
            _cooldownTimer = _currentAttack.cooldown;

            var attack = _currentAttack;
            _currentAttack = null;
            OnAttackFinished?.Invoke(attack);
        }
    }
}
