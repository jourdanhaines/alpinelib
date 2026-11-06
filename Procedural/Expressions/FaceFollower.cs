using System;

namespace AlpineLib.Procedural.Expressions {
    /// <summary>
    /// A held expression that chases a target: exponentially, with one time constant rising and another
    /// falling, and settling exactly on the target once within half a percent of it, so rest reads as a true zero.
    /// </summary>
    public sealed class FaceFollower {
        private const float SnapDistance = 5e-3f;

        /// <summary>Current value.</summary>
        public float Value { get; private set; }

        /// <summary>Moves toward <paramref name="target"/> over <paramref name="deltaTime"/> seconds and returns the new value.</summary>
        public float Step(float target, float deltaTime, float riseSeconds, float fallSeconds) {
            float timeConstant = target > Value ? riseSeconds : fallSeconds;
            float share = timeConstant > 0f ? 1f - (float)Math.Exp(-Math.Max(deltaTime, 0f) / timeConstant) : 1f;
            Value += (target - Value) * share;
            if (Math.Abs(target - Value) < SnapDistance) Value = target;

            return Value;
        }

        /// <summary>Jumps to zero.</summary>
        public void Reset() {
            Value = 0f;
        }
    }
}
