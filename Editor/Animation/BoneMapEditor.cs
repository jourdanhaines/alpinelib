using System.Collections.Generic;
using AlpineLib.Animation.Bones;
using UnityEditor;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>Shows what each <see cref="BoneMap"/> row resolves to, with a validate button.</summary>
    [CustomEditor(typeof(BoneMap))]
    public class BoneMapEditor : UnityEditor.Editor {
        private const string MissingLabel = "missing";

        private readonly List<string> _problems = new List<string>();
        private GUIStyle _missingStyle;

        private BoneMap Map => (BoneMap)target;

        public override void OnInspectorGUI() {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Resolved", EditorStyles.boldLabel);
            foreach (BoneMapEntry entry in Map.Entries) {
                DrawResolvedRow(entry);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Validate")) RunValidation();
            if (_problems.Count > 0) EditorGUILayout.HelpBox(string.Join("\n", _problems), MessageType.Error);
        }

        private void DrawResolvedRow(BoneMapEntry entry) {
            if (Map.TryGet(entry.bone, out Transform found)) {
                using (new EditorGUI.DisabledScope(true)) {
                    EditorGUILayout.ObjectField(entry.bone.ToString(), found, typeof(Transform), true);
                }

                return;
            }

            EditorGUILayout.LabelField(entry.bone.ToString(), MissingLabel, MissingStyle());
        }

        private void RunValidation() {
            _problems.Clear();
            if (Map.Validate(_problems)) Debug.Log($"BoneMap on '{Map.name}': all {Map.Entries.Count} rows resolve.", Map);
        }

        private GUIStyle MissingStyle() {
            if (_missingStyle != null) return _missingStyle;

            _missingStyle = new GUIStyle(EditorStyles.label);
            _missingStyle.normal.textColor = new Color(0.9f, 0.25f, 0.25f);
            return _missingStyle;
        }
    }
}
