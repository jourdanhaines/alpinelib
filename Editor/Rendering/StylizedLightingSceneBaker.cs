using AlpineLib.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AlpineLib.Editor {
    /// <summary>
    /// Bakes the open scene's <see cref="StylizedLightingApplier"/> preset into the scene itself (sun pose, render
    /// settings, skybox, volume profile), so edit mode and the first frame of play look like the applied preset.
    /// </summary>
    public static class StylizedLightingSceneBaker {
        [MenuItem("AlpineLib/Rendering/Apply Lighting Preset To Open Scene")]
        public static void ApplyToOpenScene() {
            if (TryApplyToOpenScene(out string message)) Debug.Log($"StylizedLightingSceneBaker: {message}");
            else Debug.LogError($"StylizedLightingSceneBaker: {message}");
        }

        /// <summary>Applies the active scene's single applier; the scene is marked dirty, not saved.</summary>
        public static bool TryApplyToOpenScene(out string message) {
            Scene scene = SceneManager.GetActiveScene();
            StylizedLightingApplier[] appliers = Object.FindObjectsByType<StylizedLightingApplier>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (appliers.Length != 1) {
                message = $"'{scene.name}' has {appliers.Length} StylizedLightingAppliers; it needs exactly one";
                return false;
            }

            StylizedLightingApplier applier = appliers[0];
            StylizedLightingPreset preset = applier.Preset;
            if (preset == null) {
                message = $"the applier on '{applier.name}' has no preset";
                return false;
            }

            Bake(applier, preset);
            EditorSceneManager.MarkSceneDirty(scene);
            message = $"applied '{preset.name}' to '{scene.name}'";
            return true;
        }

        private static void Bake(StylizedLightingApplier applier, StylizedLightingPreset preset) {
            preset.Apply(applier.Sun, null);

            // The scene keeps the asset, not the runtime instance; the asset is brought in line with the preset.
            Material skybox = preset.SkyboxMaterial;
            if (skybox != null) {
                preset.WriteSkybox(skybox);
                EditorUtility.SetDirty(skybox);
                RenderSettings.skybox = skybox;
            }

            if (applier.Sun != null) {
                EditorUtility.SetDirty(applier.Sun);
                EditorUtility.SetDirty(applier.Sun.transform);
            }

            if (applier.GlobalVolume == null || preset.VolumeProfile == null) return;

            applier.GlobalVolume.sharedProfile = preset.VolumeProfile;
            EditorUtility.SetDirty(applier.GlobalVolume);
        }
    }
}
