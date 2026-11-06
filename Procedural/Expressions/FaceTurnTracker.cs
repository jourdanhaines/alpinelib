using System;

namespace AlpineLib.Procedural.Expressions {
    /// <summary>
    /// How fast a body is turning, measured from its facing one sample to the next: degrees per second, positive
    /// turning right (clockwise seen from above), wrapped across ±180 so a turn through south reads as the turn it is.
    /// </summary>
    /// <remarks>
    /// The first sample after <see cref="Restart"/> only sets the baseline. A jump in facing larger than
    /// <see cref="SnapDegrees"/> in one sample is a snap (a teleport, a respawn), not a turn: it restarts. Samples
    /// closer together than <see cref="MinStepSeconds"/> are pooled until they span it (the rate holds meanwhile), so
    /// any frame rate measures the same turn; a bad step holds the last rate and the rate never exceeds
    /// <see cref="MaxRate"/>, so a frame-time hitch cannot spike it.
    /// </remarks>
    public sealed class FaceTurnTracker {
        /// <summary>Largest facing change in one sample read as turning, degrees.</summary>
        public const float SnapDegrees = 90f;

        /// <summary>Shortest span a rate is measured over, seconds; shorter samples are pooled up to it.</summary>
        public const float MinStepSeconds = 1f / 240f;

        /// <summary>Fastest rate reported, degrees per second.</summary>
        public const float MaxRate = 1440f;

        private float _yaw;
        private bool _hasBaseline;
        private float _pendingDegrees;
        private float _pendingSeconds;

        /// <summary>Rate after the last sample, degrees per second; 0 until two samples have been taken.</summary>
        public float Rate { get; private set; }

        /// <summary>
        /// Takes the facing <paramref name="yawDegrees"/> after <paramref name="deltaTime"/> seconds and returns the turn rate.
        /// </summary>
        public float Sample(float yawDegrees, float deltaTime) {
            if (float.IsNaN(yawDegrees) || float.IsInfinity(yawDegrees)) {
                Restart();
                return Rate;
            }

            if (!_hasBaseline) return Rebase(yawDegrees);

            float step = FaceMath.StepSeconds(deltaTime);
            if (step <= 0f) return Rate;

            float turned = DeltaDegrees(_yaw, yawDegrees);
            if (Math.Abs(turned) > SnapDegrees) return Rebase(yawDegrees);

            _yaw = yawDegrees;
            _pendingDegrees += turned;
            _pendingSeconds += step;
            if (_pendingSeconds < MinStepSeconds) return Rate;

            float rate = _pendingDegrees / _pendingSeconds;
            _pendingDegrees = 0f;
            _pendingSeconds = 0f;
            Rate = rate > MaxRate ? MaxRate : rate < -MaxRate ? -MaxRate : rate;
            return Rate;
        }

        /// <summary>Forgets the facing: the next sample is a new baseline and the rate is 0 until then.</summary>
        public void Restart() {
            _hasBaseline = false;
            ClearPending();
            Rate = 0f;
        }

        /// <summary>Shortest signed turn from <paramref name="from"/> to <paramref name="to"/>, -180..180 degrees.</summary>
        public static float DeltaDegrees(float from, float to) {
            float delta = (to - from) % 360f;
            if (delta > 180f) delta -= 360f;
            if (delta < -180f) delta += 360f;
            return delta;
        }

        private float Rebase(float yawDegrees) {
            _yaw = yawDegrees;
            _hasBaseline = true;
            ClearPending();
            Rate = 0f;
            return Rate;
        }

        private void ClearPending() {
            _pendingDegrees = 0f;
            _pendingSeconds = 0f;
        }
    }
}
