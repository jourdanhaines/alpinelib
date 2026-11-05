using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AlpineLib.Rendering;
using UnityEditor;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// Writes the committed fade templates (<see cref="DitherFadeTemplates"/>): one "&lt;source&gt;Fade" material per
    /// source asset, the source with the fade keyword on, refreshed in place so its GUID stays.
    /// </summary>
    public static class DitherFadeTemplateTools {
        /// <summary>Creates or refreshes a template per source asset in <paramref name="folder"/> and lists them in <paramref name="set"/>; returns how many.</summary>
        public static int Refresh(DitherFadeTemplates set, IEnumerable<Material> sources, string folder) {
            if (set == null) throw new ArgumentNullException(nameof(set));
            if (string.IsNullOrEmpty(folder)) throw new ArgumentException("A templates folder is needed.", nameof(folder));

            Directory.CreateDirectory(folder);
            var entries = new List<DitherFadeTemplate>();
            IEnumerable<Material> persistent = sources.Where((source) => source != null && EditorUtility.IsPersistent(source) && DitherFade.Supports(source));
            foreach (Material source in persistent.Distinct().OrderBy((source) => source.name, StringComparer.Ordinal)) {
                entries.Add(new DitherFadeTemplate { source = source, template = WriteTemplate(source, folder) });
            }

            set.SetTemplates(entries);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return entries.Count;
        }

        private static Material WriteTemplate(Material source, string folder) {
            string path = $"{folder.TrimEnd('/')}/{source.name}Fade.mat";
            Material template = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (template == null) {
                template = new Material(source);
                AssetDatabase.CreateAsset(template, path);
            } else {
                template.shader = source.shader;
                template.CopyPropertiesFromMaterial(source);
            }

            template.shaderKeywords = source.shaderKeywords;
            template.EnableKeyword(new UnityEngine.Rendering.LocalKeyword(source.shader, DitherFade.Keyword));
            template.SetFloat(DitherFade.ToggleId, 1f);
            template.SetFloat(DitherFade.AmountId, 1f);
            template.enableInstancing = source.enableInstancing;
            template.renderQueue = source.renderQueue;
            EditorUtility.SetDirty(template);
            return template;
        }
    }
}
