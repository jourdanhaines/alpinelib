using LitMotion;
using UnityEngine;

namespace AlpineLib.Motion {
    /// <summary>
    /// Slides its transform between a captured closed pose and that pose plus a local offset.
    /// </summary>
    /// <remarks>
    /// The owner calls <see cref="Initialise"/> rather than <c>Awake</c> capturing the closed pose,
    /// because spawned pieces such as a door leaf are positioned by their builder after <c>Awake</c>
    /// has already run. Outside play mode every change snaps (see <see cref="ToggleMotion"/>), so
    /// editor tooling and batchmode tests see the final pose synchronously.
    /// </remarks>
    public class SlideMotion : MonoBehaviour {
        [Tooltip("Local offset from the closed pose to the open pose.")]
        [SerializeField] private Vector3 travel;

        [Tooltip("Seconds for a full closed-to-open slide; partial moves take proportionally less.")]
        [SerializeField] private float fullTravelSeconds = 1f;

        [Tooltip("Easing applied to each individual move.")]
        [SerializeField] private Ease ease = Ease.InOutSine;

        private Vector3 _closedLocal;
        private ToggleMotion _toggle;

        public bool IsInitialised => _toggle != null;

        /// <summary>The target state: true once asked to open, even while still sliding.</summary>
        public bool IsOpen => _toggle != null && _toggle.Target >= 0.5f;

        /// <summary>Current travel, 0 closed through 1 open.</summary>
        public float Fraction => _toggle?.Fraction ?? 0f;

        /// <summary>
        /// Captures the current local position as the closed pose. Call after the owner has placed
        /// the object; calling again re-captures from wherever it now sits.
        /// </summary>
        public void Initialise() {
            _toggle?.Cancel();
            _closedLocal = transform.localPosition;
            _toggle = new ToggleMotion(Apply, fullTravelSeconds, ease);
        }

        /// <summary>Opens or closes, tweening from the current fraction when animated.</summary>
        public void SetOpen(bool open, bool animate) {
            if (_toggle == null) {
                Debug.LogError($"{nameof(SlideMotion)} on '{name}' used before {nameof(Initialise)}.", this);
                return;
            }

            float target = open ? 1f : 0f;
            if (animate) {
                _toggle.MoveTo(target);
                return;
            }

            _toggle.Snap(target);
        }

        /// <summary>Jumps straight to the open or closed pose.</summary>
        public void Snap(bool open) {
            SetOpen(open, false);
        }

        /// <summary>Test seam: snaps to an arbitrary fraction and applies it immediately.</summary>
        public void SetFraction(float fraction) {
            if (_toggle == null) {
                Debug.LogError($"{nameof(SlideMotion)} on '{name}' used before {nameof(Initialise)}.", this);
                return;
            }

            _toggle.Snap(fraction);
        }

        private void Apply(float fraction) {
            transform.localPosition = _closedLocal + travel * fraction;
        }

        private void OnDestroy() {
            _toggle?.Cancel();
        }
    }
}
