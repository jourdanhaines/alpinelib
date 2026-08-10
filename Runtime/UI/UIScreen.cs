using UnityEngine;

namespace AlpineLib.UI {
    /// <summary>
    /// A fadeable screen backed by a <see cref="CanvasGroup"/>. Games derive from this for menus,
    /// overlays and popups, override <see cref="OnShown"/> / <see cref="OnHidden"/> for their own
    /// wiring, and drive visibility through <see cref="Show"/> and <see cref="Hide"/> rather than
    /// toggling the GameObject.
    /// </summary>
    /// <remarks>
    /// The fade is stepped from <c>Update</c> against <see cref="Time.unscaledDeltaTime"/> rather than
    /// scaled time. Menus are exactly the thing that gets opened while the rest of the game is slowed
    /// or frozen, so a scaled fade would crawl or stall precisely when the player is waiting on it.
    /// Unscaled time keeps the animation identical whether the owning game pauses by timescale, by
    /// suppressing input, or not at all.
    ///
    /// The GameObject is deliberately left active while hidden: an inactive object stops receiving
    /// <c>Update</c>, which would strand a fade mid-way, and re-activation would re-run <c>Awake</c>
    /// ordering for anything a derived screen owns. A hidden screen is instead fully inert — zero
    /// alpha, no raycasts, not interactable — which costs a canvas that draws nothing.
    /// </remarks>
    [RequireComponent(typeof(CanvasGroup))]
    public class UIScreen : MonoBehaviour {
        [Tooltip("Seconds a full fade between hidden and visible takes. Zero makes every show and hide instant.")]
        [SerializeField] private float fadeDuration = 0.15f;

        /// <summary>
        /// True from the moment <see cref="Show"/> is called until <see cref="Hide"/> is called,
        /// including while a fade in either direction is still running.
        /// </summary>
        /// <remarks>
        /// This tracks intent, not alpha, so callers that ask "is this screen up?" during a fade get the
        /// answer the player is about to see rather than the frame's transient opacity.
        /// </remarks>
        public bool IsVisible { get; private set; }

        private CanvasGroup _canvasGroup;
        private float _targetAlpha;
        private bool _isFading;

        private void Awake() {
            ResolveCanvasGroup();
        }

        /// <summary>
        /// Caches the canvas group and seeds the visibility state from whatever alpha the screen was
        /// authored at, so a prefab saved fully opaque starts interactive and one saved at zero starts
        /// inert.
        /// </summary>
        /// <remarks>
        /// Called from <c>Awake</c> and again at the top of <see cref="Show"/> and <see cref="Hide"/>
        /// because a scene composition root commonly pushes a screen from its own <c>Awake</c>, which may
        /// run before this component's. Making resolution idempotent is cheaper than forcing every caller
        /// to care about component ordering.
        /// </remarks>
        private void ResolveCanvasGroup() {
            if (_canvasGroup != null) return;

            _canvasGroup = GetComponent<CanvasGroup>();
            IsVisible = _canvasGroup.alpha > 0f;
            _targetAlpha = IsVisible ? 1f : 0f;
            _canvasGroup.interactable = IsVisible;
            _canvasGroup.blocksRaycasts = IsVisible;
        }

        /// <summary>
        /// Brings the screen up, fading in over <c>fadeDuration</c> unless <paramref name="instant"/> is
        /// set. Does nothing when the screen is already fully shown.
        /// </summary>
        /// <remarks>
        /// Raycast blocking and interactivity are enabled on the first frame of the fade rather than at
        /// the end of it. A screen that is visibly arriving must already be swallowing clicks, otherwise
        /// a fast player can press a world button through a menu that is halfway on screen.
        /// </remarks>
        public void Show(bool instant = false) {
            ResolveCanvasGroup();

            if (IsVisible && !_isFading) return;

            IsVisible = true;
            _targetAlpha = 1f;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;

            if (!instant && fadeDuration > 0f) {
                _isFading = true;
                return;
            }

            CompleteFadeImmediately();
        }

        /// <summary>
        /// Takes the screen down, fading out over <c>fadeDuration</c> unless <paramref name="instant"/>
        /// is set. Does nothing when the screen is already fully hidden.
        /// </summary>
        /// <remarks>
        /// Interactivity and raycast blocking are dropped at the start of the fade, the mirror of
        /// <see cref="Show"/>: a screen that is on its way out must stop eating input immediately, so a
        /// resume button cannot be clicked twice while the menu dissolves.
        /// </remarks>
        public void Hide(bool instant = false) {
            ResolveCanvasGroup();

            if (!IsVisible && !_isFading) return;

            IsVisible = false;
            _targetAlpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            if (!instant && fadeDuration > 0f) {
                _isFading = true;
                return;
            }

            CompleteFadeImmediately();
        }

        private void Update() {
            if (!_isFading) return;

            StepFade();
        }

        /// <summary>
        /// Advances the alpha towards its target for one frame and settles the fade once it arrives.
        /// </summary>
        /// <remarks>
        /// <see cref="Mathf.MoveTowards"/> is used instead of an eased lerp so <c>fadeDuration</c> means
        /// what it says: a full zero-to-one fade takes exactly that many seconds, and a fade reversed
        /// part way through finishes proportionally sooner rather than dragging a decaying tail.
        /// </remarks>
        private void StepFade() {
            float alphaStep = Time.unscaledDeltaTime / fadeDuration;
            _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, _targetAlpha, alphaStep);

            if (!Mathf.Approximately(_canvasGroup.alpha, _targetAlpha)) return;

            CompleteFadeImmediately();
        }

        /// <summary>
        /// Snaps the alpha to its target, ends any running fade and raises the matching completion hook.
        /// </summary>
        private void CompleteFadeImmediately() {
            _canvasGroup.alpha = _targetAlpha;
            _isFading = false;

            if (IsVisible) {
                OnShown();
                return;
            }

            OnHidden();
        }

        /// <summary>
        /// Called once the screen has finished fading in, or immediately on an instant show. Override to
        /// select a default control, start music, or refresh contents that are only worth building while
        /// the screen is up.
        /// </summary>
        protected virtual void OnShown() { }

        /// <summary>
        /// Called once the screen has finished fading out, or immediately on an instant hide. Override to
        /// release whatever <see cref="OnShown"/> took.
        /// </summary>
        protected virtual void OnHidden() { }
    }
}
