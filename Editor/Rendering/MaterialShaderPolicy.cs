using System;
using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// Which shaders a project's materials must not use, and which assets are exempt (third-party packs the
    /// project does not own). Read by <see cref="MaterialShaderValidator"/>; a project without one is not checked.
    /// </summary>
    [CreateAssetMenu(fileName = "MaterialShaderPolicy", menuName = "AlpineLib/Rendering/Material Shader Policy")]
    public sealed class MaterialShaderPolicy : ScriptableObject {
        [Tooltip("Exact shader names no material may use, e.g. 'Universal Render Pipeline/Lit'.")]
        [SerializeField] private List<string> forbiddenShaders = new List<string>();

        [Tooltip("Shader name prefixes no material may use, e.g. 'Legacy Shaders/'.")]
        [SerializeField] private List<string> forbiddenShaderPrefixes = new List<string>();

        [Tooltip("Asset path prefixes whose materials are exempt, e.g. 'Assets/ThirdParty/'.")]
        [SerializeField] private List<string> allowedPathPrefixes = new List<string>();

        [Tooltip("Exact asset paths whose materials are exempt.")]
        [SerializeField] private List<string> allowedPaths = new List<string>();

        /// <summary>Exact shader names no material may use.</summary>
        public IReadOnlyList<string> ForbiddenShaders => forbiddenShaders;

        /// <summary>Shader name prefixes no material may use.</summary>
        public IReadOnlyList<string> ForbiddenShaderPrefixes => forbiddenShaderPrefixes;

        /// <summary>Asset path prefixes whose materials are exempt.</summary>
        public IReadOnlyList<string> AllowedPathPrefixes => allowedPathPrefixes;

        /// <summary>Exact asset paths whose materials are exempt.</summary>
        public IReadOnlyList<string> AllowedPaths => allowedPaths;

        /// <summary>True when <paramref name="shader"/> is forbidden by name or by name prefix.</summary>
        public bool IsForbidden(Shader shader) {
            if (shader == null) return false;
            if (forbiddenShaders.Contains(shader.name)) return true;

            return StartsWithAny(shader.name, forbiddenShaderPrefixes);
        }

        /// <summary>True when the asset at <paramref name="assetPath"/> is exempt.</summary>
        public bool IsAllowed(string assetPath) {
            if (string.IsNullOrEmpty(assetPath)) return false;
            if (allowedPaths.Contains(assetPath)) return true;

            return StartsWithAny(assetPath, allowedPathPrefixes);
        }

        private static bool StartsWithAny(string value, List<string> prefixes) {
            foreach (string prefix in prefixes) {
                if (!string.IsNullOrEmpty(prefix) && value.StartsWith(prefix, StringComparison.Ordinal)) return true;
            }

            return false;
        }
    }
}
