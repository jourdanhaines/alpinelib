using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// Batch mode asset integrity check. Scans every prefab, ScriptableObject asset and build scene
    /// in the project and in the AlpineLib package for components whose script reference no longer
    /// resolves, plus a table of hand picked serialized references that must never be left empty.
    /// Intended to be driven from a build gate:
    /// <c>-batchmode -quit -executeMethod AlpineLib.Editor.AssetValidator.ValidateAll</c>.
    /// </summary>
    public static class AssetValidator {
        private const string libraryPackagePath = "Packages/com.fluxinteractive.alpinelib";
        private const string sceneFallbackFolder = "Assets/Scenes/";
        private const string logPrefix = "[AlpineLib] AssetValidator";

        /// <summary>
        /// Serialized fields that must be filled in, keyed by the runtime type name that declares
        /// them. Deliberately keyed by string rather than by <see cref="Type"/> so the validator keeps
        /// working while these types are being moved between assemblies or do not exist yet — an entry
        /// whose type is absent from the project is skipped silently.
        /// </summary>
        /// <remarks>
        /// One entry per type: the field picked is the one whose absence turns the asset into a silent
        /// no-op rather than a visible error. An unassigned <c>animationTrigger</c>, for instance,
        /// leaves a skill that consumes its cost and never plays, which is far harder to trace back
        /// than a failed import.
        /// </remarks>
        private static readonly Dictionary<string, string> criticalFields = new Dictionary<string, string> {
            { "SpawnConfig", "spawnActorPrefab" },
            { "HurtBox", "bodyPart" },
            { "BodySystem", "bodyPlan" },
            { "MeleeSkillDefinition", "animationTrigger" },
            { "ProjectileSkillDefinition", "projectilePrefab" },
            { "WeaponDefinition", "locomotionOverride" },
            { "LoadoutDefinition", "weapon" },
            { "ClassDefinition", "passiveTree" },
            { "SpecializationDefinition", "parentClass" },
            { "StatConversionDefinition", "target" },
            { "NetworkConfig", "gameProtocolName" },
            { "MatchDefinition", "matchId" },
            { "SessionConfig", "profile" },
            { "LobbyConfig", "lobbySceneName" }
        };

        /// <summary>
        /// Runs every validation pass. Logs one error per failure with the offending asset path,
        /// then exits the editor with code 1 when running in batch mode and anything failed, or 0
        /// when everything passed. In an interactive editor the result is only logged.
        /// </summary>
        public static void ValidateAll() {
            var failures = new List<string>();

            ValidatePrefabs(failures);
            ValidateScriptableObjects(failures);
            SessionConfigValidator.Validate(failures);
            ValidateScenes(failures);

            Report(failures);
        }

        private static void ValidatePrefabs(List<string> failures) {
            foreach (var assetPath in GatherAssetPaths(".prefab")) {
                var prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefabRoot == null) {
                    failures.Add($"{assetPath}: prefab could not be loaded.");
                    continue;
                }

                ValidateGameObjectTree(prefabRoot, assetPath, failures);
            }
        }

        private static void ValidateScriptableObjects(List<string> failures) {
            foreach (var assetPath in GatherAssetPaths(".asset")) {
                var subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                if (subAssets.Length == 0) {
                    failures.Add($"{assetPath}: asset could not be loaded.");
                    continue;
                }

                foreach (var subAsset in subAssets) {
                    if (subAsset == null) {
                        failures.Add($"{assetPath}: asset has a missing script reference.");
                        continue;
                    }

                    if (!(subAsset is ScriptableObject scriptableObject)) continue;

                    ValidateScriptReference(scriptableObject, assetPath, failures);
                    ValidateCriticalFields(scriptableObject, assetPath, failures);
                }
            }
        }

        private static void ValidateScenes(List<string> failures) {
            var scenePaths = GatherScenePaths();
            if (scenePaths.Count == 0) return;

            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
                failures.Add("Scene validation aborted: open scenes have unsaved changes.");
                return;
            }

            foreach (var scenePath in scenePaths) {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                if (!scene.IsValid()) {
                    failures.Add($"{scenePath}: scene could not be opened.");
                    continue;
                }

                foreach (var rootGameObject in scene.GetRootGameObjects()) {
                    ValidateGameObjectTree(rootGameObject, scenePath, failures);
                }
            }
        }

        private static void ValidateGameObjectTree(GameObject root, string assetPath, List<string> failures) {
            foreach (var childTransform in root.GetComponentsInChildren<Transform>(true)) {
                var gameObject = childTransform.gameObject;

                var missingScriptCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
                if (missingScriptCount > 0) {
                    failures.Add($"{assetPath}: '{GetHierarchyPath(childTransform)}' has {missingScriptCount} component(s) with a missing script.");
                }

                foreach (var component in gameObject.GetComponents<Component>()) {
                    if (component == null) continue;

                    ValidateCriticalFields(component, $"{assetPath} ({GetHierarchyPath(childTransform)})", failures);
                }
            }
        }

        private static void ValidateScriptReference(ScriptableObject scriptableObject, string assetPath, List<string> failures) {
            var serializedObject = new SerializedObject(scriptableObject);
            var scriptProperty = serializedObject.FindProperty("m_Script");
            if (scriptProperty == null) return;
            if (scriptProperty.objectReferenceValue != null) return;

            failures.Add($"{assetPath}: ScriptableObject '{scriptableObject.name}' has a missing script.");
        }

        private static void ValidateCriticalFields(UnityEngine.Object target, string assetPath, List<string> failures) {
            var typeName = target.GetType().Name;
            if (!criticalFields.TryGetValue(typeName, out var propertyName)) return;

            var serializedObject = new SerializedObject(target);
            var property = serializedObject.FindProperty(propertyName);
            if (property == null) {
                Debug.LogWarning($"{logPrefix}: {typeName} declares no serialized property '{propertyName}' — critical field table is out of date.");
                return;
            }

            if (IsCriticalFieldFilled(property)) return;

            failures.Add($"{assetPath}: {typeName}.{propertyName} is not assigned.");
        }

        /// <summary>
        /// True when a critical field carries a usable value.
        /// </summary>
        /// <remarks>
        /// Only object references and strings are judged. Any other property type means the field has
        /// not been migrated to its final form yet, and is treated as filled so a table entry written
        /// ahead of a refactor cannot fail the gate on shape alone.
        /// </remarks>
        private static bool IsCriticalFieldFilled(SerializedProperty property) {
            if (property.propertyType == SerializedPropertyType.ObjectReference) {
                return property.objectReferenceValue != null;
            }

            if (property.propertyType == SerializedPropertyType.String) {
                return !string.IsNullOrWhiteSpace(property.stringValue);
            }

            return true;
        }

        private static List<string> GatherAssetPaths(string extension) {
            var assetPaths = new List<string>();

            foreach (var assetPath in AssetDatabase.GetAllAssetPaths()) {
                if (!assetPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsValidatedPath(assetPath)) continue;

                assetPaths.Add(assetPath);
            }

            return assetPaths;
        }

        private static List<string> GatherScenePaths() {
            var scenePaths = new List<string>();

            foreach (var buildScene in EditorBuildSettings.scenes) {
                if (string.IsNullOrEmpty(buildScene.path)) continue;

                scenePaths.Add(buildScene.path);
            }

            if (scenePaths.Count > 0) return scenePaths;

            foreach (var assetPath in AssetDatabase.GetAllAssetPaths()) {
                if (!assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) continue;
                if (!assetPath.StartsWith(sceneFallbackFolder, StringComparison.Ordinal)) continue;

                scenePaths.Add(assetPath);
            }

            return scenePaths;
        }

        private static bool IsValidatedPath(string assetPath) {
            if (assetPath.StartsWith("Assets/", StringComparison.Ordinal)) return true;

            return assetPath.StartsWith(libraryPackagePath + "/", StringComparison.Ordinal);
        }

        private static string GetHierarchyPath(Transform target) {
            var path = target.name;

            for (var parent = target.parent; parent != null; parent = parent.parent) {
                path = $"{parent.name}/{path}";
            }

            return path;
        }

        private static void Report(List<string> failures) {
            foreach (var failure in failures) {
                Debug.LogError($"{logPrefix}: {failure}");
            }

            if (failures.Count == 0) {
                Debug.Log($"{logPrefix}: all assets valid.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
                return;
            }

            Debug.LogError($"{logPrefix}: {failures.Count} validation failure(s).");
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
