namespace AlpineLib.Procedural.Expressions {
    /// <summary>
    /// A one-shot reaction: from wherever it is, it rises over the attack to its peak plus a small overshoot
    /// (easing out), settles back to the peak over the hold, then eases to zero over the release.
    /// </summary>
    /// <remarks>
    /// A pulse cut short by <see cref="Clip"/> never rises above the cut again before it ends, so a reaction that
    /// lost its share to a newer one fades from what was shown instead of coming back.
    /// </remarks>
    public sealed class FacePulse {
        private float _start;
        private float _peak;
        private float _overshoot;
        private float _attack;
        private float _hold;
        private float _release;
        private float _elapsed;
        private float _ceiling = float.MaxValue;
        private bool _active;

        /// <summary>Current value, 0 when idle.</summary>
        public float Value { get; private set; }

        /// <summary>True from a trigger until the release ends.</summary>
        public bool IsActive => _active;

        /// <summary>Starts the pulse from its current value; a non-positive or NaN peak does nothing.</summary>
        public void Trigger(float peak, float attack, float hold, float release, float overshoot) {
            if (!(peak > 0f) || float.IsInfinity(peak)) return;

            _start = Value;
            _peak = peak;
            _overshoot = FaceMath.NonNegative(overshoot) * peak;
            _attack = FaceMath.NonNegative(attack);
            _hold = FaceMath.NonNegative(hold);
            _release = FaceMath.NonNegative(release);
            _elapsed = 0f;
            _ceiling = float.MaxValue;
            _active = true;
            Value = Evaluate();
        }

        /// <summary>Advances by <paramref name="deltaTime"/> seconds (a bad step counts as 0) and returns the new value.</summary>
        public float Step(float deltaTime) {
            if (!_active) return Value;

            _elapsed += FaceMath.StepSeconds(deltaTime);
            Value = Evaluate();
            if (_elapsed >= _attack + _hold + _release) Stop();
            return Value;
        }

        /// <summary>Lowers the pulse to <paramref name="shown"/> if above it, and keeps it there or below until it ends.</summary>
        public void Clip(float shown) {
            if (!_active || !(shown < Value)) return;

            float cut = FaceMath.NonNegative(shown);
            _ceiling = cut;
            Value = cut;
        }

        /// <summary>Drops straight to zero.</summary>
        public void Stop() {
            _active = false;
            _ceiling = float.MaxValue;
            Value = 0f;
        }

        private float Evaluate() {
            float value = Shape();
            return value < _ceiling ? value : _ceiling;
        }

        private float Shape() {
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
            float remaining = 1f - FaceMath.Clamp01(amount);
            return 1f - remaining * remaining * remaining;
        }

        private static float SmoothStep(float amount) {
            float clamped = FaceMath.Clamp01(amount);
            return clamped * clamped * (3f - 2f * clamped);
        }
    }
}
