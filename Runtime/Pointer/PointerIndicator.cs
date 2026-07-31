using AlpineLib.DI;
using UnityEngine;

namespace AlpineLib.Pointer {
    /// <summary>
    /// Visual marker that sits on the world position of the pointer, lifted slightly so it does not
    /// z-fight with the ground it is drawn on. Starts hidden; the owner shows it while it is relevant.
    /// </summary>
    public class PointerIndicator : MonoBehaviour {
        [SerializeField] private float groundOffset = 0.05f;

        [Inject] private IPointerService _pointerService;

        private void Start() {
            Injector.Instance.InjectDependency(this);
            Hide();
        }

        private void Update() {
            Vector3 worldPosition = _pointerService.GetWorldPosition();
            worldPosition.y += groundOffset;
            transform.position = worldPosition;
        }

        /// <summary>
        /// Makes the indicator visible again.
        /// </summary>
        public void Show() {
            gameObject.SetActive(true);
        }

        /// <summary>
        /// Hides the indicator. It stops following the pointer while hidden.
        /// </summary>
        public void Hide() {
            gameObject.SetActive(false);
        }
    }
}
