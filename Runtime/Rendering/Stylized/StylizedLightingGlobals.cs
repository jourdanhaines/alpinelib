using UnityEngine;

namespace AlpineLib.Rendering {
    /// <summary>
    /// Writes the shader globals every <c>AlpineLib/Stylized/Lit</c> material reads. Neutral (all zero) is plain
    /// Lambert, which is what captures want; a lighting preset applies the styled values.
    /// </summary>
    public static class StylizedLightingGlobals {
        /// <summary>
        /// Applies a style. Colours are given in sRGB with alpha as strength; <paramref name="wrap"/> lifts N·L
        /// round the terminator, <paramref name="rampSoftness"/> is the two-tone edge half-width (0 keeps the
        /// linear response), <paramref name="rimPower"/> narrows the rim and <paramref name="metalReflection"/>
        /// adds to the ambient reflection metallic materials always get.
        /// </summary>
        public static void Apply(Color shadowTint, Color rimColor, float wrap, float rampSoftness, float rimPower, float metalReflection) {
            Shader.SetGlobalVector(StylizedLitShader.ShadowTintGlobal, LinearWithAlpha(shadowTint));
            Shader.SetGlobalVector(StylizedLitShader.RimColorGlobal, LinearWithAlpha(rimColor));
            Shader.SetGlobalVector(StylizedLitShader.LightParamsGlobal, new Vector4(wrap, rampSoftness, rimPower, metalReflection));
        }

        /// <summary>Points the sky and stylized materials at the sun; <paramref name="towardSun"/> need not be normalised.</summary>
        public static void SetSunDirection(Vector3 towardSun) {
            Vector3 direction = towardSun.sqrMagnitude > 0f ? towardSun.normalized : Vector3.up;
            Shader.SetGlobalVector(StylizedLitShader.SunDirectionGlobal, new Vector4(direction.x, direction.y, direction.z, 1f));
        }

        /// <summary>
        /// Hands <paramref name="skybox"/>'s gradient (an <c>AlpineLib/Stylized/Skybox</c> material) to materials that fog
        /// toward the sky (<see cref="StylizedLitShader.FogSkyKeyword"/>); any other material, or null, unsets it so they
        /// fog toward the flat fog colour.
        /// </summary>
        public static void SetSky(Material skybox) {
            if (skybox == null || skybox.shader == null || skybox.shader.name != StylizedSkyboxShader.Name) {
                Shader.SetGlobalVector(StylizedLitShader.SkyZenithGlobal, Vector4.zero);
                return;
            }

            Color zenith = Linear(skybox.GetColor(StylizedSkyboxShader.ZenithColor));
            Shader.SetGlobalVector(StylizedLitShader.SkyZenithGlobal, new Vector4(zenith.r, zenith.g, zenith.b, 1f));
            Shader.SetGlobalVector(StylizedLitShader.SkyHorizonGlobal, (Vector4)Linear(skybox.GetColor(StylizedSkyboxShader.HorizonColor)));
            Shader.SetGlobalVector(StylizedLitShader.SkyGroundGlobal, (Vector4)Linear(skybox.GetColor(StylizedSkyboxShader.GroundColor)));
            Shader.SetGlobalVector(StylizedLitShader.SkyHazeGlobal, (Vector4)Linear(skybox.GetColor(StylizedSkyboxShader.HazeColor)));
            Shader.SetGlobalVector(StylizedLitShader.SkyShapeGlobal, new Vector4(skybox.GetFloat(StylizedSkyboxShader.GradientExponent),
                skybox.GetFloat(StylizedSkyboxShader.HazeHeight), skybox.GetFloat(StylizedSkyboxShader.HazeSharpness), skybox.GetFloat(StylizedSkyboxShader.SunGlowSize)));
            Shader.SetGlobalVector(StylizedLitShader.SkyGlowGlobal, (Vector4)(Linear(skybox.GetColor(StylizedSkyboxShader.SunColor)) * skybox.GetFloat(StylizedSkyboxShader.SunGlowStrength)));
        }

        /// <summary>Zeroes every global: plain Lambert, no sun (the skybox falls back to its own direction).</summary>
        public static void Neutral() {
            Restore(new StylizedLightingSnapshot(Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero));
        }

        /// <summary>The current values, for <see cref="Restore"/>.</summary>
        public static StylizedLightingSnapshot Snapshot() {
            return new StylizedLightingSnapshot(
                Shader.GetGlobalVector(StylizedLitShader.ShadowTintGlobal),
                Shader.GetGlobalVector(StylizedLitShader.RimColorGlobal),
                Shader.GetGlobalVector(StylizedLitShader.LightParamsGlobal),
                Shader.GetGlobalVector(StylizedLitShader.SunDirectionGlobal));
        }

        /// <summary>Writes back values taken by <see cref="Snapshot"/>.</summary>
        public static void Restore(StylizedLightingSnapshot snapshot) {
            Shader.SetGlobalVector(StylizedLitShader.ShadowTintGlobal, snapshot.ShadowTint);
            Shader.SetGlobalVector(StylizedLitShader.RimColorGlobal, snapshot.RimColor);
            Shader.SetGlobalVector(StylizedLitShader.LightParamsGlobal, snapshot.LightParams);
            Shader.SetGlobalVector(StylizedLitShader.SunDirectionGlobal, snapshot.SunDirection);
        }

        private static Color Linear(Color color) {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
        }

        private static Vector4 LinearWithAlpha(Color color) {
            Color linear = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
            return new Vector4(linear.r, linear.g, linear.b, color.a);
        }
    }
}
