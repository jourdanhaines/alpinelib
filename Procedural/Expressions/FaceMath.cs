namespace AlpineLib.Procedural.Expressions {
    /// <summary>The clamps the face classes share; every one maps NaN to a safe value so a bad input cannot stick.</summary>
    internal static class FaceMath {
        /// <summary>A usable time step: positive and finite, otherwise 0.</summary>
        public static float StepSeconds(float deltaTime) {
            return deltaTime > 0f && !float.IsInfinity(deltaTime) ? deltaTime : 0f;
        }

        /// <summary><paramref name="value"/> when positive, 0 when negative or NaN.</summary>
        public static float NonNegative(float value) {
            return value > 0f ? value : 0f;
        }

        /// <summary><paramref name="value"/> clamped to 0..1, 0 for NaN.</summary>
        public static float Clamp01(float value) {
            if (float.IsNaN(value)) return 0f;

            return value < 0f ? 0f : value > 1f ? 1f : value;
        }

        /// <summary><paramref name="value"/> clamped to -1..1, 0 for NaN.</summary>
        public static float ClampSigned(float value) {
            if (float.IsNaN(value)) return 0f;

            return value < -1f ? -1f : value > 1f ? 1f : value;
        }
    }
}
