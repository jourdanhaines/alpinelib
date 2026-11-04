using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// Fails every material on a shader the project's <see cref="MaterialShaderPolicy"/> forbids: material
    /// assets, materials embedded in model files, and whatever prefab renderers reference (built-in and package
    /// materials included). Materials under the policy's allowed paths are exempt. Does nothing when the project
    /// has no policy asset. <see cref="AssetValidator.ValidateAll"/> runs it.
    /// </summary>
    public static class MaterialShaderValidator {
        private const string logPrefix = "[AlpineLib] MaterialShaderValidator";
        private const string policyFilter = "t:" + nameof(MaterialShaderPolicy);
        private static readonly string[] modelExtensions = { ".fbx", ".obj", ".dae", ".3ds", ".blend" };

        /// <summary>Runs the check interactively and logs each failure.</summary>
        [MenuItem("AlpineLib/Rendering/Validate Material Shaders")]
        public static void ValidateFromMenu() {
            var failures = new List<string>();
            Validate(failures);
            foreach (string failure in failures) {
                Debug.LogError($"{logPrefix}: {failure}");
            }

            Debug.Log($"{logPrefix}: {failures.Count} problem(s) found.");
        }

        /// <summary>Appends one entry per offending material to <paramref name="failures"/>.</summary>
        public static void Validate(List<string> failures) {
            MaterialShaderPolicy policy = FindPolicy(failures);
            if (policy == null) return;

            var checkedMaterials = new HashSet<Material>();
            foreach (string assetPath in AssetDatabase.GetAllAssetPaths()) {
                if (!AssetValidator.IsValidatedPath(assetPath)) continue;

                ValidateAsset(policy, assetPath, checkedMaterials, failures);
            }
        }

        /// <summary>The single policy asset, or null when there is none (or more than one, which is a failure).</summary>
        private static MaterialShaderPolicy FindPolicy(List<string> failures) {
            var policyPaths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets(policyFilter)) {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetValidator.IsValidatedPath(assetPath)) policyPaths.Add(assetPath);
            }

            if (policyPaths.Count == 0) return null;
            if (policyPaths.Count > 1) {
                failures.Add($"{policyPaths.Count} material shader policies ({string.Join(", ", policyPaths)}); keep exactly one.");
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<MaterialShaderPolicy>(policyPaths[0]);
        }

        private static void ValidateAsset(MaterialShaderPolicy policy, string assetPath, HashSet<Material> checkedMaterials, List<string> failures) {
            if (assetPath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) {
                Check(policy, AssetDatabase.LoadAssetAtPath<Material>(assetPath), assetPath, checkedMaterials, failures);
                return;
            }

            if (assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) {
                ValidatePrefab(policy, assetPath, checkedMaterials, failures);
                return;
            }

            if (IsModel(assetPath)) ValidateModel(policy, assetPath, checkedMaterials, failures);
        }

        private static void ValidateModel(MaterialShaderPolicy policy, string assetPath, HashSet<Material> checkedMaterials, List<string> failures) {
            foreach (UnityEngine.Object representation in AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath)) {
                if (representation is Material material) Check(policy, material, assetPath, checkedMaterials, failures);
            }
        }

        private static void ValidatePrefab(MaterialShaderPolicy policy, string assetPath, HashSet<Material> checkedMaterials, List<string> failures) {
            var prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefabRoot == null) return;

            if (policy.IsAllowed(assetPath)) return;

            foreach (Renderer renderer in prefabRoot.GetComponentsInChildren<Renderer>(true)) {
                foreach (Material material in renderer.sharedMaterials) {
                    Check(policy, material, $"{assetPath} ({renderer.name})", checkedMaterials, failures);
                }
            }
        }

        // Each material is judged once, under the first place it was met; its own asset path decides the exemption
        // (an exempt prefab is skipped before it gets here).
        private static void Check(MaterialShaderPolicy policy, Material material, string location, HashSet<Material> checkedMaterials, List<string> failures) {
            if (material == null || !checkedMaterials.Add(material)) return;
            if (!policy.IsForbidden(material.shader)) return;

            string materialPath = AssetDatabase.GetAssetPath(material);
            if (policy.IsAllowed(materialPath)) return;

            string source = materialPath == location ? location : $"{location} -> {materialPath}";
            failures.Add($"{source}: material '{material.name}' uses forbidden shader '{material.shader.name}'.");
        }

        private static bool IsModel(string assetPath) {
            foreach (string extension in modelExtensions) {
                if (assetPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }
}
