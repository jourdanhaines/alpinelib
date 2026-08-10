using UnityEngine;

namespace AlpineLib.Pointer {
    /// <summary>
    /// Mouse implementation of <see cref="IPointerSource"/>. The ray comes from the cursor position and
    /// the world position is where that ray meets a flat ground plane, so a pointer position exists even
    /// when the cursor is over empty space.
    /// </summary>
    /// <remarks>
    /// Reads the legacy <see cref="UnityEngine.Input"/> API, which requires the old input handling to
    /// stay enabled in the player settings.
    ///
    /// Every call is fully qualified as <c>UnityEngine.Input</c> rather than left bare. A bare
    /// <c>Input</c> inside <c>AlpineLib.Pointer</c> now resolves to the sibling
    /// <see cref="AlpineLib.Input"/> namespace — namespace lookup walks outwards through
    /// <c>AlpineLib</c> and finds it before ever reaching the <c>using UnityEngine;</c> — so the
    /// unqualified form stopped compiling the moment the input module was added.
    /// </remarks>
    public sealed class MousePointerSource : IPointerSource {
        private readonly Camera _camera;
        private readonly Plane _groundPlane;

        /// <param name="camera">
        /// Camera the cursor is projected through. When null, <see cref="Camera.main"/> is resolved on
        /// every call, which keeps working across scene loads that replace the camera.
        /// </param>
        /// <param name="groundHeight">Distance of the ground plane from the origin along its normal.</param>
        /// <param name="groundNormal">Ground plane normal; defaults to <see cref="Vector3.up"/>.</param>
        public MousePointerSource(Camera camera = null, float groundHeight = 0f, Vector3 groundNormal = default) {
            _camera = camera;

            Vector3 normal = groundNormal == Vector3.zero ? Vector3.up : groundNormal.normalized;
            _groundPlane = new Plane(normal, normal * groundHeight);
        }

        public Ray GetRay() {
            var camera = _camera == null ? Camera.main : _camera;
            return camera.ScreenPointToRay(UnityEngine.Input.mousePosition);
        }

        /// <summary>
        /// Cursor ray projected onto the ground plane, or <see cref="Vector3.zero"/> when the ray runs
        /// parallel to it or points away from it.
        /// </summary>
        public Vector3 GetWorldPosition() {
            var ray = GetRay();
            if (_groundPlane.Raycast(ray, out float distance))
                return ray.GetPoint(distance);
            return Vector3.zero;
        }

        public bool IsInteractPressed() {
            return UnityEngine.Input.GetMouseButtonDown(0);
        }
    }
}
