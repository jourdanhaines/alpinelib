using System;
using System.Collections.Generic;
using AlpineLib.Appearance;
using AlpineLib.Netcode.Appearance;
using UnityEditor;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// Authoring checks for an <see cref="AppearanceCatalog"/> and its models and items: the data rules the
    /// wire table needs plus the rig checks only the editor can make (bone names against the body FBX).
    /// </summary>
    /// <remarks>
    /// Errors make an item unwearable or the catalog unusable. Warnings (a rest pose that differs from the
    /// body, a socket not named <c>Socket_*</c>) go to a separate list and never fail validation.
    /// </remarks>
    public static class AppearanceCatalogValidator {
        private const string MenuPath = "AlpineLib/Appearance/Validate Catalog";
        private const string SocketPrefix = "Socket_";
        private const float RestPoseTolerance = 0.01f;
        private const string LogPrefix = "[AlpineLib] AppearanceCatalogValidator";

        /// <summary>Validates a catalog; true when it adds no errors.</summary>
        public static bool Validate(AppearanceCatalog catalog, List<string> errors) {
            return Validate(catalog, errors, null);
        }

        /// <summary>Validates a catalog, collecting warnings when <paramref name="warnings"/> is given.</summary>
        public static bool Validate(AppearanceCatalog catalog, List<string> errors, List<string> warnings) {
            int errorCountBefore = errors.Count;
            warnings = warnings ?? new List<string>();
            if (catalog == null) {
                errors.Add("No catalog to validate.");
                return false;
            }

            ValidateModels(catalog, errors);
            ValidateItems(catalog, errors, warnings);
            ValidateTable(catalog, errors);
            return errors.Count == errorCountBefore;
        }

        /// <summary>Checks that a profile converts into an outfit the catalog accepts.</summary>
        public static bool ValidateProfile(AppearanceProfile profile, AppearanceCatalog catalog, List<string> errors) {
            if (profile == null) {
                errors.Add("No profile to validate.");
                return false;
            }

            if (profile.TryToOutfit(catalog, out _, out string error)) return true;

            errors.Add($"Profile '{profile.name}': {error}");
            return false;
        }

        [MenuItem(MenuPath)]
        private static void ValidateSelectedCatalog() {
            AppearanceCatalog catalog = Selection.activeObject as AppearanceCatalog;
            if (catalog == null) return;

            var errors = new List<string>();
            var warnings = new List<string>();
            Validate(catalog, errors, warnings);
            ValidateProfilesOf(catalog, errors);

            foreach (string warning in warnings) Debug.LogWarning($"{LogPrefix}: {warning}", catalog);
            foreach (string error in errors) Debug.LogError($"{LogPrefix}: {error}", catalog);
            Debug.Log($"{LogPrefix}: '{catalog.name}' {errors.Count} error(s), {warnings.Count} warning(s).", catalog);
        }

        [MenuItem(MenuPath, true)]
        private static bool CanValidateSelectedCatalog() {
            return Selection.activeObject is AppearanceCatalog;
        }

        // Every profile whose model this catalog numbers.
        private static void ValidateProfilesOf(AppearanceCatalog catalog, List<string> errors) {
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(AppearanceProfile))) {
                var profile = AssetDatabase.LoadAssetAtPath<AppearanceProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (profile == null || catalog.IdOf(profile.Model) == 0) continue;

                ValidateProfile(profile, catalog, errors);
            }
        }

        private static void ValidateTable(AppearanceCatalog catalog, List<string> errors) {
            try {
                catalog.ToTable((model) => 0);
            } catch (ArgumentException exception) {
                errors.Add($"Catalog '{catalog.name}' table: {exception.Message}");
            }
        }

        private static void ValidateModels(AppearanceCatalog catalog, List<string> errors) {
            var seenKeys = new HashSet<string>();
            var seenModels = new HashSet<CharacterModel>();
            foreach (CharacterModel model in catalog.Models) {
                if (model == null) continue;
                if (!seenModels.Add(model)) errors.Add($"Model '{model.name}' is listed more than once.");
                if (string.IsNullOrWhiteSpace(model.Key)) errors.Add($"Model '{model.name}' has no key.");
                if (!string.IsNullOrWhiteSpace(model.Key) && !seenKeys.Add(model.Key)) errors.Add($"Model key '{model.Key}' is used by more than one model.");

                ValidateModel(model, errors);
            }
        }

        private static void ValidateModel(CharacterModel model, List<string> errors) {
            if (model.BodyModel == null) errors.Add($"Model '{model.name}' has no body model.");
            if (model.SlotCount == 0) errors.Add($"Model '{model.name}' has no slots.");
            if (model.SlotCount > AppearanceOutfit.MaxSlots) errors.Add($"Model '{model.name}' has {model.SlotCount} slots; at most {AppearanceOutfit.MaxSlots} are allowed.");

            var seenKeys = new HashSet<string>();
            for (int index = 0; index < model.SlotCount; index++) {
                AppearanceSlotDefinition slot = model.SlotAt(index);
                if (slot == null || string.IsNullOrWhiteSpace(slot.Key)) {
                    errors.Add($"Model '{model.name}' slot {index} has no key.");
                    continue;
                }

                if (!seenKeys.Add(slot.Key)) errors.Add($"Model '{model.name}' slot key '{slot.Key}' is used more than once.");
            }
        }

        private static void ValidateItems(AppearanceCatalog catalog, List<string> errors, List<string> warnings) {
            var seenItems = new HashSet<AppearanceItem>();
            foreach (AppearanceItem item in catalog.Items) {
                if (item == null) continue;
                if (!seenItems.Add(item)) errors.Add($"Item '{item.name}' is listed more than once.");

                ValidateItem(catalog, item, errors, warnings);
            }
        }

        private static void ValidateItem(AppearanceCatalog catalog, AppearanceItem item, List<string> errors, List<string> warnings) {
            if (string.IsNullOrWhiteSpace(item.SlotKey)) errors.Add($"Item '{item.name}' has no slot key.");
            if (item.Variants.Count == 0) errors.Add($"Item '{item.name}' has no variants.");

            ValidateMaterialSets(item, errors);
            ValidateHiddenBodyNames(item, errors, warnings);
            var seenModels = new HashSet<CharacterModel>();
            for (int index = 0; index < item.Variants.Count; index++) {
                AppearanceItemVariant variant = item.Variants[index];
                string context = $"Item '{item.name}' variant {index}";
                if (!ValidateVariantHeader(catalog, item, variant, context, errors)) continue;
                if (!seenModels.Add(variant.Model)) errors.Add($"{context}: model '{variant.Model.name}' already has a variant.");

                ValidateVariantPrefab(variant, context, errors, warnings);
            }
        }

        private static void ValidateMaterialSets(AppearanceItem item, List<string> errors) {
            if (item.MaterialSets.Count > byte.MaxValue) {
                errors.Add($"Item '{item.name}' has {item.MaterialSets.Count} material sets; at most {byte.MaxValue} are allowed.");
            }

            for (int index = 0; index < item.MaterialSets.Count; index++) {
                AppearanceMaterialSet set = item.MaterialSets[index];
                if (set != null && HasAnyMaterial(set)) continue;

                errors.Add($"Item '{item.name}' material set {index} has no material.");
            }
        }

        // Each name must pick out exactly one renderer on every body the item is authored for.
        private static void ValidateHiddenBodyNames(AppearanceItem item, List<string> errors, List<string> warnings) {
            var seenNames = new HashSet<string>();
            for (int index = 0; index < item.HidesBodyRenderers.Count; index++) {
                string bodyName = item.HidesBodyRenderers[index];
                if (string.IsNullOrWhiteSpace(bodyName)) {
                    errors.Add($"Item '{item.name}' hidden body renderer {index} has no name.");
                    continue;
                }

                if (!seenNames.Add(bodyName)) warnings.Add($"Item '{item.name}' hides body renderer '{bodyName}' more than once.");
            }

            foreach (CharacterModel model in BodyModelsOf(item)) {
                ValidateHiddenBodyNamesOn(item, model, seenNames, errors);
            }
        }

        private static List<CharacterModel> BodyModelsOf(AppearanceItem item) {
            var models = new List<CharacterModel>();
            foreach (AppearanceItemVariant variant in item.Variants) {
                if (variant == null || variant.Model == null || variant.Model.BodyModel == null) continue;
                if (!models.Contains(variant.Model)) models.Add(variant.Model);
            }

            return models;
        }

        private static void ValidateHiddenBodyNamesOn(AppearanceItem item, CharacterModel model, HashSet<string> bodyNames, List<string> errors) {
            Dictionary<string, List<Transform>> bodyNodes = MapByName(model.BodyModel.transform);
            foreach (string bodyName in bodyNames) {
                string problem = HiddenBodyNameProblem(bodyNodes, bodyName);
                if (problem == null) continue;

                errors.Add($"Item '{item.name}' on model '{model.name}': hidden body renderer '{bodyName}' {problem}.");
            }
        }

        private static string HiddenBodyNameProblem(Dictionary<string, List<Transform>> bodyNodes, string bodyName) {
            int matchCount = bodyNodes.TryGetValue(bodyName, out List<Transform> matches) ? matches.Count : 0;
            if (matchCount == 0) return "matched 0 transforms on the body";
            if (matchCount > 1) return $"is ambiguous on the body ({matchCount} matches)";
            if (matches[0].GetComponent<Renderer>() == null) return "matched a transform with no Renderer";

            return null;
        }

        private static bool HasAnyMaterial(AppearanceMaterialSet set) {
            foreach (Material material in set.Materials) {
                if (material != null) return true;
            }

            return false;
        }

        // False when the variant is too broken to inspect its prefab against the model.
        private static bool ValidateVariantHeader(AppearanceCatalog catalog, AppearanceItem item, AppearanceItemVariant variant, string context, List<string> errors) {
            string problem = VariantHeaderProblem(catalog, item, variant);
            if (problem == null) return true;

            errors.Add($"{context}: {problem}");
            return false;
        }

        private static string VariantHeaderProblem(AppearanceCatalog catalog, AppearanceItem item, AppearanceItemVariant variant) {
            if (variant == null) return "is empty.";
            if (variant.Model == null) return "has no model.";
            if (catalog.IdOf(variant.Model) == 0) return $"model '{variant.Model.name}' is not in the catalog.";
            if (variant.Model.IndexOfSlot(item.SlotKey) < 0) return $"model '{variant.Model.name}' has no slot '{item.SlotKey}'.";
            if (variant.Prefab == null) return "has no prefab.";

            return null;
        }

        private static void ValidateVariantPrefab(AppearanceItemVariant variant, string context, List<string> errors, List<string> warnings) {
            GameObject prefab = variant.Prefab;
            AddIfPresent<Collider>(prefab, context, errors);
            AddIfPresent<Rigidbody>(prefab, context, errors);
            AddIfPresent<Joint>(prefab, context, errors);
            if (variant.Attach == AppearanceAttachMode.Socket) AddIfPresent<Animator>(prefab, context, errors);

            Transform body = variant.Model.BodyModel == null ? null : variant.Model.BodyModel.transform;
            if (variant.Attach == AppearanceAttachMode.Skinned) {
                ValidateSkinned(prefab, body, context, errors, warnings);
                return;
            }

            ValidateSocket(variant.AttachPoint, body, context, errors, warnings);
        }

        private static void AddIfPresent<TComponent>(GameObject prefab, string context, List<string> errors) where TComponent : Component {
            if (prefab.GetComponentsInChildren<TComponent>(true).Length == 0) return;

            errors.Add($"{context}: prefab '{prefab.name}' has a {typeof(TComponent).Name}; wearables must be visual only.");
        }

        private static void ValidateSkinned(GameObject prefab, Transform body, string context, List<string> errors, List<string> warnings) {
            SkinnedMeshRenderer[] renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (renderers.Length == 0) errors.Add($"{context}: skinned prefab '{prefab.name}' has no SkinnedMeshRenderer.");
            if (prefab.GetComponentsInChildren<MeshRenderer>(true).Length > 0) {
                errors.Add($"{context}: skinned prefab '{prefab.name}' has a MeshRenderer; use a socket variant for rigid meshes.");
            }

            if (body == null) return;

            Dictionary<string, List<Transform>> bodyBones = MapByName(body);
            var checkedBones = new HashSet<string>();
            foreach (SkinnedMeshRenderer renderer in renderers) {
                ValidateRendererBones(renderer, bodyBones, checkedBones, context, errors, warnings);
            }
        }

        private static void ValidateRendererBones(SkinnedMeshRenderer renderer, Dictionary<string, List<Transform>> bodyBones, HashSet<string> checkedBones,
            string context, List<string> errors, List<string> warnings) {
            string rendererContext = $"{context} renderer '{renderer.name}'";
            if (renderer.bones.Length == 0) errors.Add($"{rendererContext} has no bones.");
            if (renderer.rootBone == null) errors.Add($"{rendererContext} has no root bone.");

            foreach (Transform bone in renderer.bones) {
                if (bone == null) {
                    errors.Add($"{rendererContext} has a null bone.");
                    continue;
                }

                if (checkedBones.Add(bone.name)) ValidateBone(bone, bodyBones, rendererContext, errors, warnings);
            }

            if (renderer.rootBone != null && checkedBones.Add(renderer.rootBone.name)) {
                ValidateBone(renderer.rootBone, bodyBones, rendererContext, errors, warnings);
            }
        }

        // A bone must match exactly one body transform by name; a moved rest pose deforms wrongly but binds.
        private static void ValidateBone(Transform bone, Dictionary<string, List<Transform>> bodyBones, string context, List<string> errors, List<string> warnings) {
            if (!bodyBones.TryGetValue(bone.name, out List<Transform> matches)) {
                errors.Add($"{context}: bone '{bone.name}' is missing from the body.");
                return;
            }

            if (matches.Count > 1) {
                errors.Add($"{context}: bone '{bone.name}' is ambiguous on the body ({matches.Count} matches).");
                return;
            }

            float offset = Vector3.Distance(bone.localPosition, matches[0].localPosition);
            if (offset <= RestPoseTolerance) return;

            warnings.Add($"{context}: bone '{bone.name}' rest position differs from the body by {offset * 100f:0.0} cm.");
        }

        private static void ValidateSocket(string attachPoint, Transform body, string context, List<string> errors, List<string> warnings) {
            if (string.IsNullOrWhiteSpace(attachPoint)) {
                errors.Add($"{context}: socket variant has no attach point.");
                return;
            }

            if (!attachPoint.StartsWith(SocketPrefix, StringComparison.Ordinal)) {
                warnings.Add($"{context}: attach point '{attachPoint}' is not named {SocketPrefix}*.");
            }

            if (body == null) return;

            int matchCount = MapByName(body).TryGetValue(attachPoint, out List<Transform> matches) ? matches.Count : 0;
            if (matchCount == 0) errors.Add($"{context}: attach point '{attachPoint}' is missing from the body.");
            if (matchCount > 1) errors.Add($"{context}: attach point '{attachPoint}' is ambiguous on the body ({matchCount} matches).");
        }

        private static Dictionary<string, List<Transform>> MapByName(Transform root) {
            var map = new Dictionary<string, List<Transform>>();
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true)) {
                if (!map.TryGetValue(node.name, out List<Transform> matches)) {
                    matches = new List<Transform>();
                    map.Add(node.name, matches);
                }

                matches.Add(node);
            }

            return map;
        }
    }
}
