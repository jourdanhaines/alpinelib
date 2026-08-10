using UnityEngine;

namespace AlpineLib.Animation {
    /// <summary>
    /// Fires a random idle variation — a fidget, a look-around — after the actor has stood idle
    /// for a random stretch of time.
    /// </summary>
    /// <remarks>
    /// A variation only fires while the animator is genuinely at rest: <c>Speed</c> near zero,
    /// <c>Grounded</c> true, and the base layer sitting in its locomotion state outside any
    /// transition. The state check is what keeps the trigger honest — a trigger fired while a jump
    /// or landing is playing would go stale and fire the variation at the wrong moment later.
    /// Any movement resets the countdown, and the countdown re-arms after each variation, so
    /// variations recur only through sustained stillness.
    ///
    /// The controller opts in by declaring <c>Speed</c> and <c>Grounded</c> and giving each
    /// variation a base-layer state reachable by its trigger; controllers that lack the parameters
    /// are never written to, same convention as the actor's optional parameters. An entry's
    /// expression trigger rides the <see cref="ExpressionSystem"/> layer contract: it plays over
    /// the variation on the masked expressions layer, and stays independently playable through
    /// <see cref="ExpressionSystem.Play"/>.
    /// </remarks>
    public class IdleVariationSystem : MonoBehaviour {
        [Tooltip("Shortest idle stretch before a variation can fire, seconds")]
        [SerializeField] private float minIdleTime = 5f;
        [Tooltip("Longest idle stretch before a variation fires, seconds")]
        [SerializeField] private float maxIdleTime = 10f;
        [Tooltip("Base-layer state the animator must be resting in for a variation to fire")]
        [SerializeField] private string locomotionStateName = "Locomotion";
        [Tooltip("Variations drawn from uniformly each time the idle countdown elapses")]
        [SerializeField] private IdleVariation[] variations;

        /// <summary>
        /// Speed parameter values below this count as standing still. Matches the damped tail of
        /// the actor's Speed write, which never quite reaches zero.
        /// </summary>
        private const float IdleSpeedThreshold = 0.05f;

        private Animator _animator;
        private int _locomotionStateHash;
        private bool _hasRequiredParameters;
        private float _fireTime;

        private void Start() {
            _animator = GetComponentInChildren<Animator>();
            _locomotionStateHash = Animator.StringToHash(locomotionStateName);
            _hasRequiredParameters = DeclaresRequiredParameters();
            ScheduleNextVariation();
        }

        private void Update() {
            if (!_hasRequiredParameters) return;
            if (variations == null || variations.Length == 0) return;

            if (!IsIdle()) {
                ScheduleNextVariation();
                return;
            }

            if (Time.time < _fireTime) return;

            FireRandomVariation();
            ScheduleNextVariation();
        }

        private bool IsIdle() {
            if (_animator.GetFloat(AnimatorParameters.Speed) > IdleSpeedThreshold) return false;
            if (!_animator.GetBool(AnimatorParameters.Grounded)) return false;
            if (_animator.IsInTransition(0)) return false;

            return _animator.GetCurrentAnimatorStateInfo(0).shortNameHash == _locomotionStateHash;
        }

        private void FireRandomVariation() {
            IdleVariation variation = variations[Random.Range(0, variations.Length)];
            if (string.IsNullOrEmpty(variation.variationTrigger)) return;

            _animator.SetTrigger(variation.variationTrigger);

            if (string.IsNullOrEmpty(variation.expressionTrigger)) return;

            _animator.SetTrigger(variation.expressionTrigger);
        }

        private void ScheduleNextVariation() {
            _fireTime = Time.time + Random.Range(minIdleTime, maxIdleTime);
        }

        /// <remarks>
        /// Resolved once, in <c>Start</c>, because <see cref="Animator.parameters"/> allocates on
        /// every access — same convention as the actor's optional parameter scans.
        /// </remarks>
        private bool DeclaresRequiredParameters() {
            if (_animator == null) return false;
            if (_animator.runtimeAnimatorController == null) return false;

            bool hasSpeed = false;
            bool hasGrounded = false;
            foreach (AnimatorControllerParameter parameter in _animator.parameters) {
                if (parameter.nameHash == AnimatorParameters.Speed) hasSpeed = true;
                if (parameter.nameHash == AnimatorParameters.Grounded) hasGrounded = true;
            }

            return hasSpeed && hasGrounded;
        }
    }
}
