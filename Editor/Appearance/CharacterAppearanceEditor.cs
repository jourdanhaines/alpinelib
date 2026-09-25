using System.Collections.Generic;
using AlpineLib.Appearance;
using AlpineLib.Netcode.Appearance;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// Picks a character's item per slot from its catalog. Edit-mode changes rebuild the preview; in play
    /// mode a change goes through <see cref="CharacterAppearance.Apply"/>, as a network outfit would.
    /// </summary>
    [CustomEditor(typeof(CharacterAppearance))]
    public class CharacterAppearanceEditor : UnityEditor.Editor {
        private const string NoneLabel = "None";
        private const string NotAllowedSuffix = " (not allowed)";
        private const string NotInCatalogSuffix = " (not in catalog)";
        private const string FirstPersonHiddenSuffix = " (FP hidden)";
        private const string AssetSelectedHint = "Select a scene or prefab-mode instance to preview.";

        private readonly List<AppearanceItem> _allowedItems = new List<AppearanceItem>();
        private SerializedProperty _catalog;
        private SerializedProperty _model;
        private SerializedProperty _slots;
        private AppearanceProfile _profile;

        private CharacterAppearance Character => (CharacterAppearance)target;

        private void OnEnable() {
            _catalog = serializedObject.FindProperty("catalog");
            _model = serializedObject.FindProperty("model");
            _slots = serializedObject.FindProperty("slots");
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable() {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();
            EditorGUILayout.PropertyField(_catalog);
            AppearanceCatalog catalog = _catalog.objectReferenceValue as AppearanceCatalog;
            DrawModel(catalog);
            CharacterModel model = _model.objectReferenceValue as CharacterModel;
            SyncSlotCount(model);
            DrawSlots(catalog, model);

            bool changed = serializedObject.ApplyModifiedProperties();
            if (changed) RebuildPreview();

            DrawButtons();
            DrawProfile(catalog, model);
            DrawErrors(catalog, model);
        }

        // The model is fixed for a character's life, so it only changes outside play mode.
        private void DrawModel(AppearanceCatalog catalog) {
            using (new EditorGUI.DisabledScope(Application.isPlaying)) {
                if (catalog == null) {
                    EditorGUILayout.PropertyField(_model);
                    return;
                }

                DrawModelPopup(catalog);
            }
        }

        private void DrawModelPopup(AppearanceCatalog catalog) {
            var models = new List<CharacterModel> { null };
            var labels = new List<string> { NoneLabel };
            foreach (CharacterModel candidate in catalog.Models) {
                if (candidate == null) continue;

                models.Add(candidate);
                labels.Add(string.IsNullOrEmpty(candidate.Key) ? candidate.name : candidate.Key);
            }

            CharacterModel current = _model.objectReferenceValue as CharacterModel;
            int currentIndex = models.IndexOf(current);
            if (currentIndex < 0) {
                models.Add(current);
                labels.Add(current.name + NotInCatalogSuffix);
                currentIndex = models.Count - 1;
            }

            int chosenIndex = EditorGUILayout.Popup("Model", currentIndex, labels.ToArray());
            if (chosenIndex != currentIndex) _model.objectReferenceValue = models[chosenIndex];
        }

        // Keeps one serialized row per model slot, as the component does on enable.
        private void SyncSlotCount(CharacterModel model) {
            if (model == null || Application.isPlaying) return;
            if (_slots.arraySize != model.SlotCount) _slots.arraySize = model.SlotCount;
        }

        private void DrawSlots(AppearanceCatalog catalog, CharacterModel model) {
            if (model == null) {
                EditorGUILayout.HelpBox("Pick a character model to fill its slots.", MessageType.Info);
                return;
            }

            if (catalog == null) {
                EditorGUILayout.HelpBox("No catalog assigned; slots are unfiltered.", MessageType.Warning);
                using (new EditorGUI.DisabledScope(Application.isPlaying)) EditorGUILayout.PropertyField(_slots, true);
                return;
            }

            int count = Mathf.Min(model.SlotCount, _slots.arraySize);
            for (int index = 0; index < count; index++) {
                DrawSlotRow(catalog, model, index);
            }
        }

        private void DrawSlotRow(AppearanceCatalog catalog, CharacterModel model, int slotIndex) {
            AppearanceSlotDefinition slot = model.SlotAt(slotIndex);
            if (slot == null) return;

            SerializedProperty element = _slots.GetArrayElementAtIndex(slotIndex);
            SerializedProperty itemProperty = element.FindPropertyRelative("item");
            SerializedProperty variantProperty = element.FindPropertyRelative("variant");
            string label = slot.HiddenInFirstPerson ? slot.Key + FirstPersonHiddenSuffix : slot.Key;

            AppearanceItem currentItem = itemProperty.objectReferenceValue as AppearanceItem;
            AppearanceItem chosenItem = DrawItemPopup(catalog, model, slotIndex, label, currentItem);
            int chosenVariant = chosenItem == currentItem ? DrawVariantPopup(chosenItem, variantProperty.intValue) : 0;
            if (chosenItem == currentItem && chosenVariant == variantProperty.intValue) return;

            if (Application.isPlaying) {
                ApplyInPlay(catalog, slotIndex, chosenItem, chosenVariant);
                return;
            }

            itemProperty.objectReferenceValue = chosenItem;
            variantProperty.intValue = chosenVariant;
        }

        // Lists "None" plus every item the model can wear here; a current pick outside that list stays
        // listed, marked, so it can be seen and replaced.
        private AppearanceItem DrawItemPopup(AppearanceCatalog catalog, CharacterModel model, int slotIndex, string label, AppearanceItem current) {
            catalog.CollectItemsFor(model, slotIndex, _allowedItems);
            var items = new List<AppearanceItem> { null };
            var labels = new List<string> { NoneLabel };
            foreach (AppearanceItem item in _allowedItems) {
                items.Add(item);
                labels.Add(item.name);
            }

            int currentIndex = items.IndexOf(current);
            if (currentIndex < 0) {
                items.Add(current);
                labels.Add(current.name + NotAllowedSuffix);
                currentIndex = items.Count - 1;
            }

            int chosenIndex = EditorGUILayout.Popup(label, currentIndex, labels.ToArray());
            return items[chosenIndex];
        }

        // Only items with several material sets have a choice to make.
        private static int DrawVariantPopup(AppearanceItem item, int current) {
            if (item == null || item.VariantCount <= 1) return current;

            var labels = new List<string>();
            for (int index = 0; index < item.VariantCount; index++) {
                labels.Add(VariantLabel(item, index));
            }

            int shownIndex = current;
            if (current < 0 || current >= item.VariantCount) {
                labels.Add($"{current}: (invalid)");
                shownIndex = labels.Count - 1;
            }

            using (new EditorGUI.IndentLevelScope()) {
                int chosenIndex = EditorGUILayout.Popup("Variant", shownIndex, labels.ToArray());
                return chosenIndex == shownIndex ? current : chosenIndex;
            }
        }

        private static string VariantLabel(AppearanceItem item, int variantIndex) {
            AppearanceMaterialSet set = item.MaterialSetAt(variantIndex);
            string name = set == null || string.IsNullOrEmpty(set.Label) ? $"Variant {variantIndex}" : set.Label;
            return $"{variantIndex}: {name}";
        }

        private void ApplyInPlay(AppearanceCatalog catalog, int slotIndex, AppearanceItem item, int variant) {
            AppearanceSlotPick pick = item == null
                ? AppearanceSlotPick.Empty
                : new AppearanceSlotPick(catalog.IdOf(item), (byte)Mathf.Clamp(variant, 0, byte.MaxValue));
            Character.Apply(Character.CurrentOutfit.With(slotIndex, pick));
        }

        private void DrawButtons() {
            bool persistent = EditorUtility.IsPersistent(Character);
            using (new EditorGUILayout.HorizontalScope()) {
                using (new EditorGUI.DisabledScope(persistent)) {
                    if (GUILayout.Button("Build")) BuildTarget();
                    if (GUILayout.Button("Clear")) Character.Clear();
                }
            }

            if (persistent) EditorGUILayout.LabelField(AssetSelectedHint, EditorStyles.miniLabel);
        }

        private void DrawProfile(AppearanceCatalog catalog, CharacterModel model) {
            EditorGUILayout.Space();
            _profile = (AppearanceProfile)EditorGUILayout.ObjectField("Profile", _profile, typeof(AppearanceProfile), false);
            if (_profile == null) return;

            bool sameModel = _profile.Model == model && model != null;
            using (new EditorGUILayout.HorizontalScope()) {
                using (new EditorGUI.DisabledScope(!sameModel || EditorUtility.IsPersistent(Character))) {
                    if (GUILayout.Button("Load From Profile")) LoadFromProfile(model);
                }

                using (new EditorGUI.DisabledScope(model == null)) {
                    if (GUILayout.Button("Save To Profile")) SaveToProfile(model);
                }
            }

            if (!sameModel) EditorGUILayout.HelpBox("The profile is for a different character model; saving overwrites its model.", MessageType.Info);
            if (catalog != null && sameModel && !_profile.TryToOutfit(catalog, out _, out string error)) {
                EditorGUILayout.HelpBox(error, MessageType.Warning);
            }
        }

        private void LoadFromProfile(CharacterModel model) {
            if (Application.isPlaying) {
                Character.ApplyProfile(_profile);
                return;
            }

            Undo.RecordObject(Character, "Load Appearance Profile");
            for (int index = 0; index < model.SlotCount; index++) {
                AppearanceSlotSelection selection = index < _profile.Slots.Count ? _profile.Slots[index] : null;
                if (selection == null) {
                    Character.SetSlot(index, null, 0);
                    continue;
                }

                Character.SetSlot(index, selection.Item, selection.Variant);
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(Character);
            EditorUtility.SetDirty(Character);
            serializedObject.Update();
            RebuildPreview();
        }

        private void SaveToProfile(CharacterModel model) {
            var selections = new List<AppearanceSlotSelection>();
            for (int index = 0; index < _slots.arraySize; index++) {
                SerializedProperty element = _slots.GetArrayElementAtIndex(index);
                AppearanceItem item = element.FindPropertyRelative("item").objectReferenceValue as AppearanceItem;
                selections.Add(new AppearanceSlotSelection(item, element.FindPropertyRelative("variant").intValue));
            }

            Undo.RecordObject(_profile, "Save Appearance Profile");
            _profile.CopyFrom(model, selections);
            EditorUtility.SetDirty(_profile);
        }

        private void DrawErrors(AppearanceCatalog catalog, CharacterModel model) {
            IReadOnlyList<string> errors = Character.LastErrors;
            if (errors.Count > 0) EditorGUILayout.HelpBox(string.Join("\n", errors), MessageType.Error);
            if (catalog == null || model == null) return;
            if (catalog.IdOf(model) == 0) {
                EditorGUILayout.HelpBox($"Model '{model.name}' is not in catalog '{catalog.name}'.", MessageType.Error);
                return;
            }

            if (catalog.TryValidate(Character.CurrentOutfit, Character.VariantResolver, out string error)) return;

            EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        private void OnUndoRedo() {
            RebuildPreview();
        }

        // Play mode rebuilds through Apply, so only edit mode refreshes the preview here.
        private void RebuildPreview() {
            if (Application.isPlaying || !CanPreview(Character)) return;

            Character.Rebuild();
        }

        // Unlike the preview, Build also runs in play mode, where Rebuild builds real pieces.
        private void BuildTarget() {
            if (!CanPreview(Character)) return;

            Character.Rebuild();
        }

        // Scene and prefab-mode instances build; prefab assets and other preview scenes (e.g.
        // LoadPrefabContents) must stay free of built pieces.
        private static bool CanPreview(CharacterAppearance appearance) {
            if (appearance == null || EditorUtility.IsPersistent(appearance)) return false;
            if (PrefabStageUtility.GetPrefabStage(appearance.gameObject) != null) return true;

            return !EditorSceneManager.IsPreviewScene(appearance.gameObject.scene);
        }
    }
}
