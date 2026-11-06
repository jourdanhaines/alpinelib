using System;

namespace AlpineLib.Procedural.Expressions {
    /// <summary>
    /// How strongly and how quickly a face reacts: the blink rhythm, the take-off and landing pulses, and the
    /// rise and fall times of the held expressions. Weights are channel weights (0..1); times are seconds.
    /// </summary>
    /// <remarks>
    /// Pulses rise fast with a small overshoot, hold, then ease out slowly; held expressions follow their target
    /// with a short time constant going up and a longer one coming down. Public fields so engines can serialize it.
    /// </remarks>
    [Serializable]
    public sealed class FaceExpressionTuning {
        public float blinkIntervalMin = 2f;
        public float blinkIntervalMax = 6f;
        public float doubleBlinkChance = 0.2f;
        public float doubleBlinkGap = 0.1f;
        public float blinkCloseSeconds = 0.07f;
        public float blinkShutSeconds = 0.04f;
        public float blinkOpenSeconds = 0.12f;

        public float jumpWide = 0.55f;
        public float jumpAttack = 0.06f;
        public float jumpHold = 0.15f;
        public float jumpRelease = 0.45f;

        public float landSquashMin = 0.35f;
        public float landSquashMax = 0.8f;
        public float landShortAirTime = 0.3f;
        public float landLongAirTime = 1.2f;
        public float landMinAirTime = 0.08f;
        public float landAttack = 0.04f;
        public float landHold = 0.1f;
        public float landRelease = 0.5f;

        public float pulseOvershoot = 0.12f;

        public float worriedAirTime = 0.75f;
        public float worriedRampSeconds = 0.35f;
        public float worriedPeak = 0.7f;
        public float worriedAttack = 0.1f;
        public float worriedRelease = 0.3f;

        public float exertionSquint = 0.6f;
        public float squintAttack = 0.25f;
        public float squintRelease = 0.4f;

        public float glanceLookDown = 0.6f;
        public float glanceAttack = 0.15f;
        public float glanceRelease = 0.25f;

        public float gazeAttack = 0.08f;
        public float gazeRelease = 0.15f;

        public float expressionBudget = 1f;

        /// <summary>A fresh copy, so a caller can tweak values without touching a shared instance.</summary>
        public FaceExpressionTuning Clone() {
            return (FaceExpressionTuning)MemberwiseClone();
        }
    }
}
