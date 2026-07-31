using AlpineLib.DI;
using UnityEngine;

namespace AlpineLib.Pointer {
    /// <summary>
    /// World-space pointer read by gameplay code: where the pointer is, what it is aiming at, and whether
    /// the interact input fired this frame.
    /// </summary>
    public interface IPointerService : IDependencyProvider {
        /// <summary>
        /// Ray from the viewer through the current pointer position.
        /// </summary>
        Ray GetRay();

        /// <summary>
        /// World position the pointer resolves to.
        /// </summary>
        Vector3 GetWorldPosition();

        /// <summary>
        /// True on the frame the interact input was pressed.
        /// </summary>
        bool IsInteractPressed();
    }

    /// <summary>
    /// Default <see cref="IPointerService"/>. It reads a pluggable <see cref="IPointerSource"/> and owns
    /// the single pointer raycast for the whole scene: once per frame it picks the
    /// <see cref="PointerInteractable"/> under the pointer and dispatches enter, exit, and interact to it.
    /// </summary>
    /// <remarks>
    /// The interactable is resolved with <c>GetComponentInParent</c> from the collider that was hit, so an
    /// interactable may sit above the colliders that represent it.
    /// </remarks>
    public class PointerService : MonoBehaviour, IPointerService {
        [Tooltip("Layers the pointer can pick interactables on.")]
        [SerializeField] private LayerMask interactableLayers = ~0;

        /// <summary>
        /// Pointer device driving the service. Assign before or after <c>Awake</c> to swap devices; a
        /// <see cref="MousePointerSource"/> is installed on <c>Awake</c> when nothing was assigned.
        /// </summary>
        public IPointerSource Source { get; set; }

        private PointerInteractable _hovered;

        [Provide]
        public IPointerService ProvidePointerService() {
            return this;
        }

        private void Awake() {
            Source ??= new MousePointerSource();
        }

        private void Update() {
            var hovered = ResolveHovered();

            if (hovered != _hovered) {
                if (_hovered != null)
                    _hovered.OnPointerExit();

                _hovered = hovered;

                if (_hovered != null)
                    _hovered.OnPointerEnter();
            }

            if (_hovered == null) return;
            if (!Source.IsInteractPressed()) return;

            _hovered.OnPointerInteract();
        }

        private PointerInteractable ResolveHovered() {
            if (!Physics.Raycast(GetRay(), out RaycastHit hit, Mathf.Infinity, interactableLayers))
                return null;

            return hit.collider.GetComponentInParent<PointerInteractable>();
        }

        public Ray GetRay() {
            return Source.GetRay();
        }

        public Vector3 GetWorldPosition() {
            return Source.GetWorldPosition();
        }

        public bool IsInteractPressed() {
            return Source.IsInteractPressed();
        }
    }
}
