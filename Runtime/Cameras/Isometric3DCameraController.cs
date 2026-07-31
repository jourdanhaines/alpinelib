using UnityEngine;

namespace AlpineLib.Cameras {
    /// <summary>
    /// Holds the camera at a fixed offset from a target and keeps it aimed at that target, giving the
    /// rigid isometric framing of a top-down game. The offset alone defines the angle, so there is no
    /// smoothing or collision handling.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class Isometric3DCameraController : MonoBehaviour {
        public Transform target;

        [Tooltip("Offset from the target. Default provides an isometric view (~45 degrees) at 45-degree Y rotation.")]
        public Vector3 offset = new Vector3(10f, 10f, -10f);

        private void Start() {
            if (target == null) return;

            transform.LookAt(target);
        }

        private void LateUpdate() {
            if (target == null) return;

            transform.position = target.position + offset;
            transform.LookAt(target);
        }

        /// <summary>
        /// Retargets the camera. Passing null parks it where it is.
        /// </summary>
        public void SetTarget(Transform newTarget) {
            target = newTarget;
        }
    }
}
