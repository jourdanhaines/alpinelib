using UnityEngine;

namespace AlpineLib.Rendering {
    /// <summary>
    /// Names, property IDs and keywords of <c>AlpineLib/Stylized/Lit</c>. Material properties follow URP Lit, so a
    /// URP Lit material keeps its values when its shader is swapped; the <c>_Alpine*</c> globals carry the style.
    /// </summary>
    public static class StylizedLitShader {
        public const string Name = "AlpineLib/Stylized/Lit";

        public const string VertexColorKeyword = "_VERTEX_COLOR";
        public const string FlatShadingKeyword = "_FLAT_SHADING";
        public const string EmissionKeyword = "_EMISSION";
        public const string AlphaTestKeyword = "_ALPHATEST_ON";
        public const string ReceiveShadowsOffKeyword = "_RECEIVE_SHADOWS_OFF";
        public const string NoLightCookieKeyword = "_NO_LIGHT_COOKIE";

        /// <summary>Fog distance scale runs from <see cref="FogScale"/> to <see cref="FogScaleAlpha"/> by vertex alpha.</summary>
        public const string FogScaleBlendKeyword = "_FOG_SCALE_BLEND";

        /// <summary>Fog by the distance to the camera, not the view depth (sky objects, so they fog alike across the screen).</summary>
        public const string FogRadialKeyword = "_FOG_RADIAL";

        /// <summary>Every material keyword the shader declares.</summary>
        public static readonly string[] MaterialKeywords = {
            VertexColorKeyword, FlatShadingKeyword, EmissionKeyword, AlphaTestKeyword, ReceiveShadowsOffKeyword, NoLightCookieKeyword, FogScaleBlendKeyword, FogRadialKeyword
        };

        public static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        public static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        public static readonly int Cutoff = Shader.PropertyToID("_Cutoff");
        public static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        public static readonly int Metallic = Shader.PropertyToID("_Metallic");
        public static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        public static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        public static readonly int RimStrength = Shader.PropertyToID("_RimStrength");
        public static readonly int VertexColorStrength = Shader.PropertyToID("_VertexColorStrength");
        public static readonly int FogScale = Shader.PropertyToID("_FogScale");
        public static readonly int FogScaleAlpha = Shader.PropertyToID("_FogScaleAlpha");
        public static readonly int Cull = Shader.PropertyToID("_Cull");

        // Inspector toggles that mirror the keywords (Toggle / ToggleOff drawers).
        public static readonly int VertexColorToggle = Shader.PropertyToID("_VertexColor");
        public static readonly int FlatShadingToggle = Shader.PropertyToID("_FlatShading");
        public static readonly int EmissionToggle = Shader.PropertyToID("_Emission");
        public static readonly int AlphaClipToggle = Shader.PropertyToID("_AlphaClip");
        public static readonly int ReceiveShadowsToggle = Shader.PropertyToID("_ReceiveShadows");
        public static readonly int NoLightCookieToggle = Shader.PropertyToID("_NoLightCookie");
        public static readonly int FogScaleBlendToggle = Shader.PropertyToID("_FogScaleBlend");
        public static readonly int FogRadialToggle = Shader.PropertyToID("_FogRadial");

        public static readonly int ShadowTintGlobal = Shader.PropertyToID("_AlpineShadowTint");
        public static readonly int RimColorGlobal = Shader.PropertyToID("_AlpineRimColor");
        public static readonly int LightParamsGlobal = Shader.PropertyToID("_AlpineLightParams");
        public static readonly int SunDirectionGlobal = Shader.PropertyToID("_AlpineSunDirection");

        /// <summary>The shader, or null when it is not in the build or project.</summary>
        public static Shader Find() {
            return Shader.Find(Name);
        }

        /// <summary>True when <paramref name="material"/> uses this shader.</summary>
        public static bool Uses(Material material) {
            return material != null && material.shader != null && material.shader.name == Name;
        }

        /// <summary>Sets a keyword and the inspector toggle that mirrors it, so the two never disagree.</summary>
        public static void SetKeyword(Material material, string keyword, bool enabled) {
            if (enabled) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);

            int toggle = ToggleFor(keyword);
            if (toggle == 0 || !material.HasProperty(toggle)) return;

            // Receive Shadows is a ToggleOff: the property reads 1 while the keyword is off.
            bool isInverted = keyword == ReceiveShadowsOffKeyword;
            material.SetFloat(toggle, enabled != isInverted ? 1f : 0f);
        }

        /// <summary>The inspector toggle property mirroring <paramref name="keyword"/>, or 0 for none.</summary>
        public static int ToggleFor(string keyword) {
            switch (keyword) {
                case VertexColorKeyword: return VertexColorToggle;
                case FlatShadingKeyword: return FlatShadingToggle;
                case EmissionKeyword: return EmissionToggle;
                case AlphaTestKeyword: return AlphaClipToggle;
                case ReceiveShadowsOffKeyword: return ReceiveShadowsToggle;
                case NoLightCookieKeyword: return NoLightCookieToggle;
                case FogScaleBlendKeyword: return FogScaleBlendToggle;
                case FogRadialKeyword: return FogRadialToggle;
                default: return 0;
            }
        }
    }
}
