using System;

namespace AlpineLib.Procedural.Expressions {
    /// <summary>
    /// A signed lead that keeps up with its target while the target heads out from centre, and once the target falls
    /// back or flips side eases after it exponentially until it has caught up again, so the return reads as one
    /// smooth curve: no stall, no kink, no jump through centre, and the same shape at any frame rate.
    /// </summary>
    /// <remarks>
    /// Each step is solved exactly for a target moving in a straight line from the last step's, so the curve does
    /// not depend on how the time is sliced. Within half a percent of the target it lands on it exactly.
    /// </remarks>
    public sealed class FaceLeadFollower {
        private const float SnapDistance = 5e-3f;

        // A settle time covers this many time constants: 95 % of the way back.
        private const float SettleTimeConstants = 3f;

        private bool _chasing;
        private float _lastTarget;

        /// <summary>Current value.</summary>
        public float Value { get; private set; }

        /// <summary>
        /// Moves toward <paramref name="target"/> over <paramref name="deltaTime"/> seconds and returns the new value:
        /// with time constant <paramref name="outSeconds"/> (0 follows exactly) while it keeps up with a target heading
        /// out, otherwise covering 95 % of the gap in <paramref name="settleSeconds"/>. A NaN target or bad step holds still.
        /// </summary>
        public float Step(float target, float deltaTime, float outSeconds, float settleSeconds) {
            float step = FaceMath.StepSeconds(deltaTime);
            if (float.IsNaN(target) || float.IsInfinity(target) || step <= 0f) return Value;

            bool leading = !_chasing && target * Value >= 0f && Math.Abs(target) >= Math.Abs(Value);
            _chasing = !leading;
            float timeConstant = leading ? FaceMath.NonNegative(outSeconds) : FaceMath.NonNegative(settleSeconds) / SettleTimeConstants;
            Value = Chase(Value, _lastTarget, target, step, timeConstant);
            _lastTarget = target;
            if (Math.Abs(target - Value) >= SnapDistance) return Value;

            Value = target;
            _chasing = false;
            return Value;
        }

        /// <summary>Jumps to zero.</summary>
        public void Reset() {
            Value = 0f;
            _lastTarget = 0f;
            _chasing = false;
        }

        // First-order lag behind a target ramping from `from` to `to` over the step, solved in closed form.
        private static float Chase(float value, float from, float to, float step, float timeConstant) {
            if (timeConstant <= 0f) return to;

            float lag = (to - from) / step * timeConstant;
            float decay = (float)Math.Exp(-step / timeConstant);
            return to - lag + (value - from + lag) * decay;
        }
    }
}
