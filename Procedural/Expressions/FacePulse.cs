using System;

namespace AlpineLib.Procedural.Expressions {
    /// <summary>
    /// A one-shot reaction: from wherever it is, it rises over the attack to its peak plus a small overshoot
    /// (easing out), settles back to the peak over the hold, then eases to zero over the release.
    /// </summary>
    public sealed class FacePulse {
        private float _start;
        private float _peak;
        private float _overshoot;
        private float _attack;
        private float _hold;
        private float _release;
        private float _elapsed;
        private bool _active;

        /// <summary>Current value, 0 when idle.</summary>
        public float Value { get; private set; }

        /// <summary>True from a trigger until the release ends.</summary>
        public bool IsActive => _active;

        /// <summary>Starts the pulse from its current value; a non-positive peak does nothing.</summary>
        public void Trigger(float peak, float attack, float hold, float release, float overshoot) {
            if (!(peak > 0f)) return;

            _start = Value;
            _peak = peak;
            _overshoot = Math.Max(overshoot, 0f) * peak;
            _attack = Math.Max(attack, 0f);
            _hold = Math.Max(hold, 0f);
            _release = Math.Max(release, 0f);
            _elapsed = 0f;
            _active = true;
            Value = Evaluate();
        }

        /// <summary>Advances by <paramref name="deltaTime"/> seconds and returns the new value.</summary>
        public float Step(float deltaTime) {
            if (!_active) return Value;

            _elapsed += Math.Max(deltaTime, 0f);
            Value = Evaluate();
            if (_elapsed >= _attack + _hold + _release) Stop();
            return Value;
        }

        /// <summary>Drops straight to zero.</summary>
        public void Stop() {
            _active = false;
            Value = 0f;
        }

        private float Evaluate() {
            float top = _peak + _overshoot;
            if (_elapsed < _attack) return Lerp(_start, top, EaseOut(_elapsed / _attack));

            float held = _elapsed - _attack;
            if (held < _hold) return Lerp(top, _peak, EaseOut(held / _hold));

            float released = held - _hold;
            if (released >= _release) return 0f;

            return _peak * (1f - SmoothStep(released / _release));
        }

        private static float Lerp(float from, float to, float amount) {
            return from + (to - from) * amount;
        }

        private static float EaseOut(float amount) {
            float remaining = 1f - Clamp01(amount);
            return 1f - remaining * remaining * remaining;
        }

        private static float SmoothStep(float amount) {
            float clamped = Clamp01(amount);
            return clamped * clamped * (3f - 2f * clamped);
        }

        private static float Clamp01(float value) {
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }
    }
}
