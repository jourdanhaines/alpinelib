using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Rendering {
    /// <summary>
    /// Committed fading templates for the materials that fade. The fade keyword is a shader_feature, so only variants
    /// some shipped material turns on reach a player build: referencing these templates from a built scene ships exactly
    /// the fading variants used. <see cref="DitherFadeMaterials"/> makes its twins from them.
    /// </summary>
    [CreateAssetMenu(fileName = "DitherFadeTemplates", menuName = "AlpineLib/Rendering/Dither Fade Templates")]
    public sealed class DitherFadeTemplates : ScriptableObject {
        [SerializeField] private List<DitherFadeTemplate> templates = new List<DitherFadeTemplate>();

        public IReadOnlyList<DitherFadeTemplate> Templates => templates;

        /// <summary>The template made for <paramref name="source"/>; false when none.</summary>
        public bool TryGet(Material source, out Material template) {
            template = null;
            if (source == null) return false;

            foreach (DitherFadeTemplate entry in templates) {
                if (entry.source != source || entry.template == null) continue;

                template = entry.template;
                return true;
            }
            return false;
        }

        /// <summary>Replaces every template (editor tooling).</summary>
        public void SetTemplates(IEnumerable<DitherFadeTemplate> entries) {
            templates = new List<DitherFadeTemplate>(entries);
        }

        /// <summary>
        /// Null when <paramref name="template"/> draws what <paramref name="source"/> does with the fade on (same shader,
        /// the source's keywords plus the fade's, same instancing and property values); otherwise why not.
        /// </summary>
        public static string Mismatch(Material source, Material template) {
            if (source == null || template == null) return "missing material";
            if (template.shader != source.shader) return $"shader {template.shader?.name} vs {source.shader?.name}";
            if (!template.IsKeywordEnabled(DitherFade.Keyword)) return $"{DitherFade.Keyword} is off";
            // Instancing variants ship only for materials that allow instancing.
            if (template.enableInstancing != source.enableInstancing) return "GPU instancing differs";

            string keywords = MismatchedKeywords(source, template);
            if (keywords != null) return keywords;

            return MismatchedProperty(source, template);
        }

        private static string MismatchedKeywords(Material source, Material template) {
            var expected = new HashSet<string> { DitherFade.Keyword };
            foreach (string keyword in source.shaderKeywords) expected.Add(keyword);

            var actual = new HashSet<string>(template.shaderKeywords);
            return expected.SetEquals(actual) ? null : $"keywords [{string.Join(" ", actual)}] vs [{string.Join(" ", expected)}]";
        }

        // Every property but the fade's own toggle and amount.
        private static string MismatchedProperty(Material source, Material template) {
            Shader shader = source.shader;
            for (int index = 0; index < shader.GetPropertyCount(); index++) {
                string name = shader.GetPropertyName(index);
                if (name == DitherFade.ToggleName || name == DitherFade.AmountName) continue;
                if (!IsSame(source, template, name, shader.GetPropertyType(index))) return $"property {name} differs";
            }
            return null;
        }

        private static bool IsSame(Material source, Material template, string name, UnityEngine.Rendering.ShaderPropertyType type) {
            switch (type) {
                case UnityEngine.Rendering.ShaderPropertyType.Color: return source.GetColor(name) == template.GetColor(name);
                case UnityEngine.Rendering.ShaderPropertyType.Vector: return source.GetVector(name) == template.GetVector(name);
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    return source.GetTexture(name) == template.GetTexture(name)
                        && source.GetTextureScale(name) == template.GetTextureScale(name)
                        && source.GetTextureOffset(name) == template.GetTextureOffset(name);
                case UnityEngine.Rendering.ShaderPropertyType.Int: return source.GetInteger(name) == template.GetInteger(name);
                default: return Mathf.Approximately(source.GetFloat(name), template.GetFloat(name));
            }
        }
    }
}
