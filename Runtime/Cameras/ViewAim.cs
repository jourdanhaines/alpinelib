using UnityEngine;

namespace AlpineLib.Cameras {
    /// <summary>
    /// A camera-only pitch offset that turns the view onto a target point, eased in and out (smoothstep)
    /// and smoothed so a bobbing target does not jitter the view, for a rig to add to its published pose.
    /// </summary>
    /// <remarks>
    /// Pure and time-stepped by the caller like <see cref="PitchNudge"/>: the rig works out each frame how
    /// far its own pitch is from the target and hands that in; this says how much of it to apply. The
    /// rig's pitch itself never changes, so nothing that reads it (the body's look, replication) sees the aim.
    /// </remarks>
    public class ViewAim {
        /// <summary>Largest offset applied either way, in degrees.</summary>
        public const float MaxDegrees = 45f;

        /// <summary>Seconds the offset's exponential smoothing takes to close about two thirds of a change.</summary>
        public const float SmoothingSeconds = 0.05f;

        private Transform _target;
        private float _weight;
        private float _fromWeight;
        private float _toWeight;
        private float _seconds;
        private float _elapsed;
        private float _smoothed;
        private bool _hasSample;

        /// <summary>The point the view turns onto, or null when none is set or the release has finished.</summary>
        public Transform Target => _target;

        /// <summary>How much of the aim applies, 0 off to 1 fully on.</summary>
        public float Weight => _weight;

        /// <summary>True while a target is set, including while the aim eases out.</summary>
        public bool IsActive => _target != null;

        /// <summary>Turns the view onto <paramref name="target"/>, easing in over <paramref name="seconds"/> (0 at once).</summary>
        public void Set(Transform target, float seconds) {
            if (target == null) {
                Clear(seconds);
                return;
            }

            if (_target != target) _hasSample = false;
            _target = target;
            Blend(1f, seconds);
        }

        /// <summary>Eases the aim back out over <paramref name="seconds"/> (0 at once); the target is dropped once off.</summary>
        public void Clear(float seconds) {
            if (_target == null) return;

            Blend(0f, seconds);
            if (_weight <= 0f) _target = null;
        }

        /// <summary>
        /// Advances the blend by <paramref name="deltaTime"/> and returns the offset to apply this frame given
        /// the full <paramref name="desiredDegrees"/> between the rig's pitch and the target; 0 with no target.
        /// </summary>
        public float Step(float deltaTime, float desiredDegrees) {
            if (_target == null) return 0f;

            AdvanceWeight(deltaTime);
            float desired = Mathf.Clamp(desiredDegrees, -MaxDegrees, MaxDegrees);
            if (!_hasSample || (deltaTime <= 0f && _seconds <= 0f)) {
                _smoothed = desired;
                _hasSample = true;
            } else {
                _smoothed = Mathf.Lerp(_smoothed, desired, 1f - Mathf.Exp(-Mathf.Max(deltaTime, 0f) / SmoothingSeconds));
            }

            float applied = _smoothed * _weight;
            if (_weight <= 0f && _toWeight <= 0f) _target = null;

            return applied;
        }

        private void Blend(float toWeight, float seconds) {
            _fromWeight = _weight;
            _toWeight = toWeight;
            _seconds = Mathf.Max(seconds, 0f);
            _elapsed = 0f;
            if (_seconds <= 0f) _weight = toWeight;
        }

        private void AdvanceWeight(float deltaTime) {
            if (_weight == _toWeight) return;

            _elapsed += Mathf.Max(deltaTime, 0f);
            float progress = _seconds <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_elapsed / _seconds));
            _weight = Mathf.Lerp(_fromWeight, _toWeight, progress);
        }
    }
}
