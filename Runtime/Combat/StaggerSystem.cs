using System;
using AlpineLib.Actors;
using UnityEngine;

namespace AlpineLib.Combat {
    /// <summary>
    /// Contact stagger, both halves in one component: the mover side sweeps for nearby actors each
    /// physics step and shoves whatever it runs into, the target side plays a stagger animation and
    /// stays locked down until that animation ends.
    /// </summary>
    /// <remarks>
    /// A stagger only lands when the mover is above <c>speedThreshold</c> and its layer is in the
    /// target's <c>canBeStaggeredBy</c> mask, which is how a charging zombie knocks a player around
    /// without the two shoving each other apart while standing still. Recovery is tracked through the
    /// animator: the controller needs a state tagged with <c>animationTag</c> reachable from
    /// <c>animationTrigger</c>, otherwise the stagger never resolves.
    /// </remarks>
    public class StaggerSystem : ActorSubsystem {
        [Header("Detection (mover side)")]
        [SerializeField] private bool canCauseStagger = true;
        [SerializeField] private float detectionRadius = 1f;
        [SerializeField] private LayerMask detectionLayers;

        [Header("Stagger (target side)")]
        [SerializeField] private float speedThreshold = 2f;
        [SerializeField] private float cooldown = 1f;
        [SerializeField] private LayerMask canBeStaggeredBy;
        [SerializeField] private string animationTrigger = "Stagger";
        [SerializeField] private string animationTag = "Stagger";

        /// <summary>
        /// True while a stagger animation is playing. Controllers should hold off on input until it clears.
        /// </summary>
        public bool IsStaggered { get; private set; }

        /// <summary>
        /// Raised when a stagger lands.
        /// </summary>
        public event Action OnStaggerStarted;

        /// <summary>
        /// Raised when the stagger animation finishes and control returns.
        /// </summary>
        public event Action OnStaggerFinished;

        private Actor _actor;
        private CombatSystem _combat;
        private float _cooldownTimer;
        private bool _enteredStaggerState;

        private static readonly Collider[] _overlapBuffer = new Collider[8];

        protected override void Start() {
            base.Start();

            _actor = GetComponent<Actor>();
            _combat = GetComponent<CombatSystem>();
        }

        private void Update() {
            if (_cooldownTimer > 0f)
                _cooldownTimer -= Time.deltaTime;

            if (!IsStaggered) return;

            var stateInfo = _actor.Animator.GetCurrentAnimatorStateInfo(0);
            bool inStaggerState = stateInfo.IsTag(animationTag);

            if (inStaggerState)
                _enteredStaggerState = true;

            if (_enteredStaggerState && !inStaggerState) {
                FinishStagger();
                return;
            }

            if (!inStaggerState) return;

            if (stateInfo.normalizedTime >= 1f)
                FinishStagger();
        }

        private void FixedUpdate() {
            if (!canCauseStagger) return;

            float speed = _actor.Velocity.magnitude;
            int count = Physics.OverlapSphereNonAlloc(transform.position, detectionRadius, _overlapBuffer, detectionLayers);

            for (int i = 0; i < count; i++) {
                var targetStagger = _overlapBuffer[i].GetComponentInParent<StaggerSystem>();
                if (targetStagger == null) continue;
                if (targetStagger == this) continue;

                targetStagger.TriggerStagger(gameObject.layer, speed);
            }
        }

        /// <summary>
        /// Attempts to stagger this actor. Ignored while already staggered, during the cooldown,
        /// below the speed threshold, or when the source layer is not allowed to stagger it.
        /// </summary>
        /// <param name="sourceLayer">Layer of the object doing the shoving.</param>
        /// <param name="impactSpeed">How fast that object is moving, in units per second.</param>
        public void TriggerStagger(int sourceLayer, float impactSpeed) {
            if (IsStaggered) return;
            if (_cooldownTimer > 0f) return;
            if (impactSpeed < speedThreshold) return;
            if (((1 << sourceLayer) & canBeStaggeredBy) == 0) return;

            if (_combat != null)
                _combat.CancelAttack();

            IsStaggered = true;
            _enteredStaggerState = false;

            _actor.SuppressLocomotion();
            _actor.LockRotation();
            _actor.Animator.SetTrigger(animationTrigger);

            OnStaggerStarted?.Invoke();
        }

        private void FinishStagger() {
            IsStaggered = false;
            _cooldownTimer = cooldown;

            _actor.ResumeLocomotion();
            _actor.UnlockRotation();

            OnStaggerFinished?.Invoke();
        }
    }
}
