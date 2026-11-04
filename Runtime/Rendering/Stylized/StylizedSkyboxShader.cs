using UnityEngine;

namespace AlpineLib.Rendering {
    /// <summary>
    /// Name and property IDs of <c>AlpineLib/Stylized/Skybox</c>, a gradient sky with a horizon haze band and a sun
    /// that follows the <c>_AlpineSunDirection</c> global (<see cref="StylizedLightingGlobals.SetSunDirection"/>).
    /// </summary>
    public static class StylizedSkyboxShader {
        public const string Name = "AlpineLib/Stylized/Skybox";

        public static readonly int ZenithColor = Shader.PropertyToID("_ZenithColor");
        public static readonly int HorizonColor = Shader.PropertyToID("_HorizonColor");
        public static readonly int GroundColor = Shader.PropertyToID("_GroundColor");
        public static readonly int HazeColor = Shader.PropertyToID("_HazeColor");
        public static readonly int HazeHeight = Shader.PropertyToID("_HazeHeight");
        public static readonly int HazeSharpness = Shader.PropertyToID("_HazeSharpness");
        public static readonly int GradientExponent = Shader.PropertyToID("_GradientExponent");
        public static readonly int SunColor = Shader.PropertyToID("_SunColor");
        public static readonly int SunSize = Shader.PropertyToID("_SunSize");
        public static readonly int SunGlowSize = Shader.PropertyToID("_SunGlowSize");
        public static readonly int SunGlowStrength = Shader.PropertyToID("_SunGlowStrength");
        public static readonly int SunDirectionFallback = Shader.PropertyToID("_SunDirectionFallback");

        /// <summary>The shader, or null when it is not in the build or project.</summary>
        public static Shader Find() {
            return Shader.Find(Name);
        }
    }
}
