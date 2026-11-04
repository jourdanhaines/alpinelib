using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Rendering {
    /// <summary>
    /// The ambient probe Unity derives from a Trilight (gradient) ambient, built directly so edit mode, batch
    /// renders and players all light the same without waiting on <c>DynamicGI.UpdateEnvironment</c>.
    /// </summary>
    /// <remarks>
    /// The weights were measured from Unity 6000.3's own Trilight probe (it is linear in the three linear
    /// colours and only uses the constant, Y and the two zonal L2 terms of Unity's coefficient order).
    /// </remarks>
    public static class TrilightProbe {
        private const int ConstantTerm = 0;
        private const int UpTerm = 1;
        private const int ZonalTerm = 6;
        private const int SquareTerm = 8;
        private const float SkyGroundConstant = 1f / 6f;
        private const float EquatorConstant = 2f / 3f;
        private const float UpWeight = 0.27216553f; // sqrt(6) / 9
        private const float ZonalWeight = 5f / 192f;
        private const float SquareWeight = 5f / 64f;

        /// <summary>The probe for sRGB-authored <paramref name="sky"/>, <paramref name="equator"/> and <paramref name="ground"/> colours.</summary>
        public static SphericalHarmonicsL2 Build(Color sky, Color equator, Color ground, float intensity = 1f) {
            Color skyLinear = ToLinear(sky) * intensity;
            Color equatorLinear = ToLinear(equator) * intensity;
            Color groundLinear = ToLinear(ground) * intensity;
            var probe = new SphericalHarmonicsL2();
            for (int channel = 0; channel < 3; channel++) {
                float skyValue = skyLinear[channel];
                float equatorValue = equatorLinear[channel];
                float groundValue = groundLinear[channel];
                float horizonExcess = 2f * equatorValue - skyValue - groundValue;
                probe[channel, ConstantTerm] = (skyValue + groundValue) * SkyGroundConstant + equatorValue * EquatorConstant;
                probe[channel, UpTerm] = (skyValue - groundValue) * UpWeight;
                probe[channel, ZonalTerm] = horizonExcess * ZonalWeight;
                probe[channel, SquareTerm] = horizonExcess * SquareWeight;
            }

            return probe;
        }

        /// <summary>Sets Trilight ambient with these colours and the matching probe.</summary>
        public static void Apply(Color sky, Color equator, Color ground) {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = equator;
            RenderSettings.ambientGroundColor = ground;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.ambientProbe = Build(sky, equator, ground);
        }

        private static Color ToLinear(Color color) {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
        }
    }
}
