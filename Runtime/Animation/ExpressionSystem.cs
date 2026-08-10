using UnityEngine;

namespace AlpineLib.Animation {
    /// <summary>
    /// Fires expression animations — blink first among them — as triggers on a masked animator
    /// layer, so an expression plays over whatever the base layer is doing.
    /// </summary>
    /// <remarks>
    /// The animator controller must expose one trigger per expression reaching a state on an
    /// expressions layer whose avatar mask covers only the face bones and whose default state is
    /// empty. Auto-blink fires <c>blinkParameter</c> at a random interval inside
    /// [<c>blinkIntervalMin</c>, <c>blinkIntervalMax</c>]; scripted moments call <see cref="Play"/>
    /// with any expression trigger name. Actors without an animator, or whose controller does not
    /// declare <c>blinkParameter</c>, are never written to — same convention as the actor's
    /// optional parameters.
    /// </remarks>
    public class ExpressionSystem : MonoBehaviour {
        [Tooltip("Fire the blink trigger automatically at a random interval")]
        [SerializeField] private bool autoBlink = true;
        [SerializeField] private string blinkParameter = "Blink";
        [Tooltip("Shortest pause between automatic blinks, seconds")]
        [SerializeField] private float blinkIntervalMin = 2f;
        [Tooltip("Longest pause between automatic blinks, seconds")]
        [SerializeField] private float blinkIntervalMax = 6f;

        private Animator _animator;
        private int _blinkParameterHash;
        private bool _hasBlinkParameter;
        private float _nextBlinkTime;

        private void Start() {
            _animator = GetComponentInChildren<Animator>();
            _blinkParameterHash = Animator.StringToHash(blinkParameter);
            _hasBlinkParameter = DeclaresBlinkParameter();
            ScheduleNextBlink();
        }

        private void Update() {
            if (!autoBlink) return;
            if (!_hasBlinkParameter) return;
            if (Time.time < _nextBlinkTime) return;

            _animator.SetTrigger(_blinkParameterHash);
            ScheduleNextBlink();
        }

        /// <summary>
        /// Fires the named expression trigger immediately, independent of the auto-blink timer.
        /// </summary>
        public void Play(string expressionTrigger) {
            if (_animator == null) return;

            _animator.SetTrigger(expressionTrigger);
        }

        private void ScheduleNextBlink() {
            _nextBlinkTime = Time.time + Random.Range(blinkIntervalMin, blinkIntervalMax);
        }

        /// <remarks>
        /// Resolved once, in <c>Start</c>, because <see cref="Animator.parameters"/> allocates on
        /// every access; controllers without expressions are then never written to, so Unity never
        /// logs a missing-parameter warning for them.
        /// </remarks>
        private bool DeclaresBlinkParameter() {
            if (_animator == null) return false;
            if (_animator.runtimeAnimatorController == null) return false;

            foreach (AnimatorControllerParameter parameter in _animator.parameters) {
                if (parameter.name == blinkParameter) return true;
            }

            return false;
        }
    }
}
