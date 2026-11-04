using System.Numerics;

namespace AlpineLib.Procedural.Sky {
    /// <summary>One gradient strip of an atlas: the UV of its light end and of its dark end, linear between.</summary>
    public readonly struct SkyAtlasBand {
        public SkyAtlasBand(Vector2 light, Vector2 dark) {
            Light = light;
            Dark = dark;
        }

        public Vector2 Light { get; }
        public Vector2 Dark { get; }

        /// <summary>The UV at <paramref name="t"/> (0 = light end, 1 = dark end, clamped).</summary>
        public Vector2 At(double t) {
            float clamped = (float)System.Math.Min(1.0, System.Math.Max(0.0, t));
            return Vector2.Lerp(Light, Dark, clamped);
        }
    }
}
