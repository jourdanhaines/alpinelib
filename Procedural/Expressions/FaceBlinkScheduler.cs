namespace AlpineLib.Procedural.Expressions {
    /// <summary>
    /// When a face blinks: a random pause inside the tuning's interval, now and then a second blink right
    /// after the first, each blink closing fast, staying shut a moment and opening a little slower.
    /// </summary>
    /// <remarks>
    /// Seeded, so two faces given the same seed and tuning blink together and a test can replay a run; a game
    /// that wants every client to blink on its own seeds each face differently.
    /// </remarks>
    public sealed class FaceBlinkScheduler {
        private const uint FallbackSeed = 0x9E3779B9u;
        private const float UnitScale = 1f / 16777216f;

        private uint _state;
        private float _untilNext;
        private float _blinkElapsed = -1f;
        private bool _doublePending;

        /// <summary>A scheduler whose first pause is drawn from <paramref name="tuning"/>; seed 0 is replaced by a fixed one.</summary>
        public FaceBlinkScheduler(uint seed, FaceExpressionTuning tuning) {
            _state = seed != 0u ? seed : FallbackSeed;
            _untilNext = NextPause(tuning);
        }

        /// <summary>Lid closure, 0 open to 1 shut.</summary>
        public float Value { get; private set; }

        /// <summary>True while a blink is under way.</summary>
        public bool IsBlinking => _blinkElapsed >= 0f;

        /// <summary>Blinks started so far.</summary>
        public int BlinkCount { get; private set; }

        /// <summary>Blinks that followed the one before as a double.</summary>
        public int DoubleBlinkCount { get; private set; }

        /// <summary>Seconds until the next blink starts; 0 while one is under way.</summary>
        public float SecondsUntilNext => IsBlinking ? 0f : _untilNext;

        /// <summary>Advances by <paramref name="deltaTime"/> seconds (a bad step counts as 0) and returns the lid closure.</summary>
        public float Step(float deltaTime, FaceExpressionTuning tuning) {
            float step = FaceMath.StepSeconds(deltaTime);
            if (IsBlinking) return AdvanceBlink(step, tuning);

            _untilNext -= step;
            if (_untilNext > 0f) return Value = 0f;

            StartBlink();
            return AdvanceBlink(-_untilNext, tuning);
        }

        private void StartBlink() {
            _blinkElapsed = 0f;
            BlinkCount++;
        }

        private float AdvanceBlink(float step, FaceExpressionTuning tuning) {
            _blinkElapsed += step;
            float close = FaceMath.NonNegative(tuning.blinkCloseSeconds);
            float shut = FaceMath.NonNegative(tuning.blinkShutSeconds);
            float open = FaceMath.NonNegative(tuning.blinkOpenSeconds);
            if (_blinkElapsed >= close + shut + open) {
                EndBlink(_blinkElapsed - (close + shut + open), tuning);
                return Value = 0f;
            }

            return Value = Closure(_blinkElapsed, close, shut, open);
        }

        private void EndBlink(float overrun, FaceExpressionTuning tuning) {
            _blinkElapsed = -1f;
            if (_doublePending) {
                _doublePending = false;
                DoubleBlinkCount++;
                _untilNext = FaceMath.NonNegative(tuning.doubleBlinkGap) - overrun;
                return;
            }

            _doublePending = NextUnit() < tuning.doubleBlinkChance;
            _untilNext = NextPause(tuning) - overrun;
        }

        // Ease in to shut (lids accelerate down), hold, ease out of shut (lids decelerate up).
        private static float Closure(float elapsed, float close, float shut, float open) {
            if (elapsed < close) {
                float closing = elapsed / close;
                return closing * closing;
            }

            if (elapsed < close + shut) return 1f;

            float opening = (elapsed - close - shut) / open;
            float remaining = 1f - opening;
            return remaining * remaining;
        }

        private float NextPause(FaceExpressionTuning tuning) {
            float shortest = FaceMath.NonNegative(tuning.blinkIntervalMin);
            float longest = tuning.blinkIntervalMax > shortest ? tuning.blinkIntervalMax : shortest;
            return shortest + (longest - shortest) * NextUnit();
        }

        // xorshift32: small, fast and the same on every platform.
        private float NextUnit() {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (_state >> 8) * UnitScale;
        }
    }
}
