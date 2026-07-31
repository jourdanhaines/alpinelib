using UnityEngine;
using UnityEngine.Events;

namespace AlpineLib.Pointer {
    /// <summary>
    /// Pointer callbacks a component can implement to react to the pointer. A
    /// <see cref="PointerInteractable"/> forwards to the first implementation found on itself or a parent.
    /// </summary>
    public interface IInteractable {
        void OnPointerEnter();
        void OnPointerExit();
        void OnPointerInteract();
    }

    /// <summary>
    /// Marks a hierarchy as pointer-interactive. It is passive: <see cref="PointerService"/> owns the
    /// raycast and calls the handlers here, so any collider under this object makes it hoverable and no
    /// per-object raycast is spent.
    /// </summary>
    public class PointerInteractable : MonoBehaviour {
        [SerializeField] private UnityEvent onPointerEnter = new UnityEvent();
        [SerializeField] private UnityEvent onPointerExit = new UnityEvent();
        [SerializeField] private UnityEvent onInteract = new UnityEvent();

        /// <summary>
        /// Code-side listener, resolved from this object or the nearest parent that implements it.
        /// </summary>
        protected IInteractable Interactor;

        private void Awake() {
            Interactor = GetComponentInParent<IInteractable>();
        }

        /// <summary>
        /// Called by <see cref="PointerService"/> when the pointer starts hovering this interactable.
        /// </summary>
        public virtual void OnPointerEnter() {
            Interactor?.OnPointerEnter();
            onPointerEnter.Invoke();
        }

        /// <summary>
        /// Called by <see cref="PointerService"/> when the pointer stops hovering this interactable.
        /// </summary>
        public virtual void OnPointerExit() {
            Interactor?.OnPointerExit();
            onPointerExit.Invoke();
        }

        /// <summary>
        /// Called by <see cref="PointerService"/> when the interact input fires while hovering.
        /// </summary>
        public virtual void OnPointerInteract() {
            Interactor?.OnPointerInteract();
            onInteract.Invoke();
        }
    }
}
