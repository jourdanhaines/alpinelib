using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// Fails every material on a shader the project's <see cref="MaterialShaderPolicy"/> forbids: material
    /// assets, materials embedded in model files, every material a build scene or prefab depends on (package
    /// materials included) and whatever prefab renderers reference (built-in materials included). A material is
    /// exempt only when its own asset path is allowed. Does nothing when the project has no policy asset.
    /// <see cref="AssetValidator.ValidateAll"/> runs it.
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

            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes) {
                if (!string.IsNullOrEmpty(scene.path)) ValidateDependencies(policy, scene.path, checkedMaterials, failures);
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
                ValidatePrefabRenderers(policy, assetPath, checkedMaterials, failures);
                ValidateDependencies(policy, assetPath, checkedMaterials, failures);
                return;
            }

            if (IsModel(assetPath)) ValidateModel(policy, assetPath, assetPath, checkedMaterials, failures);
        }

        private static void ValidateModel(MaterialShaderPolicy policy, string modelPath, string location, HashSet<Material> checkedMaterials, List<string> failures) {
            foreach (UnityEngine.Object representation in AssetDatabase.LoadAllAssetRepresentationsAtPath(modelPath)) {
                if (representation is Material material) Check(policy, material, location, checkedMaterials, failures);
            }
        }

        // Built-in materials (e.g. Default-Material) are no asset dependency, so prefab renderers are read directly.
        private static void ValidatePrefabRenderers(MaterialShaderPolicy policy, string assetPath, HashSet<Material> checkedMaterials, List<string> failures) {
            var prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefabRoot == null) return;

            foreach (Renderer renderer in prefabRoot.GetComponentsInChildren<Renderer>(true)) {
                foreach (Material material in renderer.sharedMaterials) {
                    Check(policy, material, $"{assetPath} ({renderer.name})", checkedMaterials, failures);
                }
            }
        }

        // Every material a scene or prefab pulls in, wherever it lives (packages too), through nested prefabs and
        // assets. Models are not walked through: their dependencies are the import-time default material, which a
        // prefab's renderers replace (and those renderers are checked directly); their embedded materials are judged.
        private static void ValidateDependencies(MaterialShaderPolicy policy, string rootPath, HashSet<Material> checkedMaterials, List<string> failures) {
            var visited = new HashSet<string> { rootPath };
            var pending = new Stack<string>();
            pending.Push(rootPath);
            while (pending.Count > 0) {
                ValidateDirectDependencies(policy, rootPath, pending.Pop(), visited, pending, checkedMaterials, failures);
            }
        }

        private static void ValidateDirectDependencies(MaterialShaderPolicy policy, string rootPath, string assetPath, HashSet<string> visited,
                Stack<string> pending, HashSet<Material> checkedMaterials, List<string> failures) {
            foreach (string dependency in AssetDatabase.GetDependencies(assetPath, false)) {
                if (visited.Add(dependency)) ValidateDependency(policy, rootPath, dependency, pending, checkedMaterials, failures);
            }
        }

        private static void ValidateDependency(MaterialShaderPolicy policy, string rootPath, string dependency, Stack<string> pending,
                HashSet<Material> checkedMaterials, List<string> failures) {
            if (dependency.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) {
                Check(policy, AssetDatabase.LoadAssetAtPath<Material>(dependency), rootPath, checkedMaterials, failures);
                return;
            }

            if (IsModel(dependency)) {
                ValidateModel(policy, dependency, rootPath, checkedMaterials, failures);
                return;
            }

            pending.Push(dependency);
        }

        // Each material is judged once, under the first place it was met; only its own asset path decides the exemption.
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
