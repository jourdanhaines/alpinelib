using UnityEngine;

namespace AlpineLib.Rendering {
    /// <summary>The raw values of the four <c>_Alpine*</c> style globals, as <see cref="StylizedLightingGlobals"/> reads and restores them.</summary>
    public readonly struct StylizedLightingSnapshot {
        /// <summary>Linear rgb shadow tint, a strength.</summary>
        public readonly Vector4 ShadowTint;
        /// <summary>Linear rgb rim colour, a strength.</summary>
        public readonly Vector4 RimColor;
        /// <summary>x wrap, y ramp softness, z rim power, w metal reflection.</summary>
        public readonly Vector4 LightParams;
        /// <summary>xyz toward the sun, w 1 when set.</summary>
        public readonly Vector4 SunDirection;

        public StylizedLightingSnapshot(Vector4 shadowTint, Vector4 rimColor, Vector4 lightParams, Vector4 sunDirection) {
            ShadowTint = shadowTint;
            RimColor = rimColor;
            LightParams = lightParams;
            SunDirection = sunDirection;
        }

        /// <summary>True when every value is zero: plain Lambert, no sun.</summary>
        public bool IsNeutral => ShadowTint == Vector4.zero && RimColor == Vector4.zero && LightParams == Vector4.zero
            && SunDirection == Vector4.zero;
    }
}
