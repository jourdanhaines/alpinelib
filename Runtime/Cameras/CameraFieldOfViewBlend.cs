using UnityEngine;

namespace AlpineLib.Cameras {
    /// <summary>
    /// Eases a camera's vertical field of view to a pushed value and back to where it was (smoothstep),
    /// e.g. zooming in on a prop raised into the hands. Lives on the camera.
    /// </summary>
    /// <remarks>
    /// The base is captured at the first push, so whatever set the field of view before (settings, a
    /// perspective blend) is what a release returns to. A push while pushed eases on from the current
    /// value; a release with nothing pushed does nothing. <see cref="Step"/> is public so an edit-mode gate
    /// can drive it; <c>LateUpdate</c> steps it by the frame time.
    /// </remarks>
    [RequireComponent(typeof(Camera))]
    public class CameraFieldOfViewBlend : MonoBehaviour {
        private Camera _camera;
        private float _base;
        private float _from;
        private float _to;
        private float _seconds;
        private float _elapsed;
        private bool _isPushed;
        private bool _isBlending;
        private bool _isReleasing;

        /// <summary>The field of view a release returns to: the captured base while pushed, else the camera's own.</summary>
        public float BaseFieldOfView => _isPushed ? _base : ResolveCamera().fieldOfView;

        /// <summary>True from a push until its release has finished.</summary>
        public bool IsPushed => _isPushed;

        /// <summary>True while easing toward <see cref="TargetFieldOfView"/>.</summary>
        public bool IsBlending => _isBlending;

        /// <summary>The field of view the running or last blend ends on: the pushed value, or the base once released.</summary>
        public float TargetFieldOfView => _isPushed || _isBlending ? _to : ResolveCamera().fieldOfView;

        /// <summary>The camera's current vertical field of view, in degrees.</summary>
        public float FieldOfView => ResolveCamera().fieldOfView;

        /// <summary>Eases the field of view to <paramref name="fieldOfViewDegrees"/> over <paramref name="seconds"/> (0 at once).</summary>
        public void Push(float fieldOfViewDegrees, float seconds) {
            Camera target = ResolveCamera();
            if (!_isPushed) _base = target.fieldOfView;

            _isPushed = true;
            _isReleasing = false;
            Begin(Mathf.Clamp(fieldOfViewDegrees, 1f, 179f), seconds);
        }

        /// <summary>Eases back to the base over <paramref name="seconds"/> (0 at once); nothing when not pushed.</summary>
        public void Release(float seconds) {
            if (!_isPushed) return;

            _isReleasing = true;
            Begin(_base, seconds);
        }

        /// <summary>Advances a running blend by <paramref name="deltaTime"/> seconds.</summary>
        public void Step(float deltaTime) {
            if (!_isBlending) return;

            _elapsed += Mathf.Max(deltaTime, 0f);
            float progress = _seconds <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_elapsed / _seconds));
            ResolveCamera().fieldOfView = Mathf.Lerp(_from, _to, progress);
            if (progress < 1f) return;

            _isBlending = false;
            if (_isReleasing) _isPushed = false;
            _isReleasing = false;
        }

        private void Begin(float to, float seconds) {
            _from = ResolveCamera().fieldOfView;
            _to = to;
            _seconds = Mathf.Max(seconds, 0f);
            _elapsed = 0f;
            _isBlending = true;
            if (_seconds <= 0f) Step(0f);
        }

        private void LateUpdate() {
            Step(Time.deltaTime);
        }

        private Camera ResolveCamera() {
            if (_camera == null) _camera = GetComponent<Camera>();
            return _camera;
        }
    }
}
