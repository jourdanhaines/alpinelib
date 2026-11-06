using System;

namespace AlpineLib.Procedural.Expressions {
    /// <summary>
    /// A signed look that eases toward its target with a critically damped spring: no overshoot, no jerk at the
    /// start of a move, one smoothing time heading out from centre and another settling back toward it, and exactly
    /// on the target once within half a percent of it, so rest reads as a true zero.
    /// </summary>
    /// <remarks>
    /// Smoothing times are roughly the time to cover most of a move; a zero time jumps straight there. The spring
    /// never carries speed away from its target or past it, so a target that drops mid-move is followed from rest.
    /// With a track band, small dips are followed at the out time and only a real drop settles at the back time.
    /// </remarks>
    public sealed class FaceSpring {
        private const float SnapDistance = 5e-3f;

        private bool _settling;

        /// <summary>Current value.</summary>
        public float Value { get; private set; }

        /// <summary>Current speed, units per second.</summary>
        public float Velocity { get; private set; }

        /// <summary>
        /// Eases toward <paramref name="target"/> over <paramref name="deltaTime"/> seconds and returns the new value:
        /// <paramref name="outSeconds"/> while the target lies further from 0 than the value (or across it),
        /// <paramref name="backSeconds"/> while it lies nearer. A NaN target or a zero or bad step holds still.
        /// </summary>
        /// <param name="trackBand">
        /// How far the target may dip below the value and still be followed at the out time, as long as it stays
        /// further than this from 0; past that the spring settles at the back time until the target heads out again.
        /// </param>
        public float Step(float target, float deltaTime, float outSeconds, float backSeconds, float trackBand = 0f) {
            float step = FaceMath.StepSeconds(deltaTime);
            if (float.IsNaN(target) || float.IsInfinity(target) || step <= 0f) return Value;

            bool settling = ChooseSettling(target, FaceMath.NonNegative(trackBand));
            float smoothing = FaceMath.NonNegative(settling ? backSeconds : outSeconds);
            if (smoothing <= 0f) return Settle(target);

            float offset = Value - target;
            if (Velocity * offset > 0f) Velocity = 0f;
            Advance(target, step, smoothing);
            bool crossed = (Value - target) * offset < 0f;
            if (crossed || Math.Abs(target - Value) < SnapDistance) Settle(target);
            return Value;
        }

        /// <summary>Jumps to zero, at rest.</summary>
        public void Reset() {
            Value = 0f;
            Velocity = 0f;
            _settling = false;
        }

        // Out while the target leads away from centre; once it has dropped past the band, back until it leads again.
        private bool ChooseSettling(float target, float trackBand) {
            bool outward = Math.Abs(target) >= Math.Abs(Value) || target * Value < 0f;
            if (outward) {
                _settling = false;
                return false;
            }

            if (Math.Abs(Value) - Math.Abs(target) > trackBand || Math.Abs(target) <= trackBand) _settling = true;
            return _settling;
        }

        // The closed-form critically damped step (as in Game Programming Gems 4, 1.10).
        private void Advance(float target, float step, float smoothing) {
            float omega = 2f / smoothing;
            float scaled = omega * step;
            float decay = 1f / (1f + scaled + 0.48f * scaled * scaled + 0.235f * scaled * scaled * scaled);
            float offset = Value - target;
            float pull = (Velocity + omega * offset) * step;
            Velocity = (Velocity - omega * pull) * decay;
            Value = target + (offset + pull) * decay;
        }

        private float Settle(float target) {
            Value = target;
            Velocity = 0f;
            return Value;
        }
    }
}
