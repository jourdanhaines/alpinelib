using System.Collections.Generic;
using AlpineLib.Body;
using UnityEditor;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// Play mode inspector for <see cref="BodySystem"/>: a foldout per body part with a card for each
    /// injury showing severity, the bleed rate breakdown and a progress bar per active condition.
    /// </summary>
    [CustomEditor(typeof(BodySystem))]
    public class BodySystemEditor : UnityEditor.Editor {
        private bool _isBodyPartsExpanded;
        private readonly HashSet<BodyPartDefinition> _expandedParts = new();

        public override void OnInspectorGUI() {
            DrawDefaultInspector();

            if (!Application.isPlaying) return;

            var bodySystem = (BodySystem)target;

            if (bodySystem.Parts == null || bodySystem.Parts.Count == 0) return;

            EditorGUILayout.Space(8);

            _isBodyPartsExpanded = EditorGUILayout.Foldout(_isBodyPartsExpanded, "Body Parts", true, EditorStyles.foldoutHeader);

            if (!_isBodyPartsExpanded) return;

            EditorGUI.indentLevel++;

            foreach (var kvp in bodySystem.Parts) {
                var part = kvp.Value;
                int injuryCount = part.Injuries.Count;

                string label = PartName(part);
                if (injuryCount > 0)
                    label += $" — {injuryCount} {(injuryCount == 1 ? "injury" : "injuries")}";

                bool isExpanded = _expandedParts.Contains(part.Definition);
                bool shouldExpand = EditorGUILayout.Foldout(isExpanded, label, true);

                if (shouldExpand != isExpanded) {
                    if (shouldExpand)
                        _expandedParts.Add(part.Definition);
                    else
                        _expandedParts.Remove(part.Definition);
                }

                if (!shouldExpand || injuryCount == 0) continue;

                EditorGUI.indentLevel++;

                foreach (var injury in part.Injuries) {
                    DrawInjury(injury, part);
                    EditorGUILayout.Space(2);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.indentLevel--;

            Repaint();
        }

        private static void DrawInjury(Injury injury, AlpineLib.Body.BodyPart part) {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(injury.Definition.name, EditorStyles.boldLabel);

            EditorGUILayout.LabelField("Severity", $"{injury.Severity:F2}");
            EditorGUILayout.LabelField("Part Multiplier", $"{part.SeverityMultiplier:F1}x");

            if (injury.BleedRate > 0f) {
                float total = injury.BleedRate * injury.Severity * part.SeverityMultiplier;
                EditorGUILayout.LabelField("Bleed Rate", $"{injury.BleedRate:F3}");
                EditorGUILayout.LabelField("Total", $"{injury.BleedRate:F3} * {injury.Severity:F2} * {part.SeverityMultiplier:F1} = {total:F3}");
            } else if (injury.IsBandaged) {
                EditorGUILayout.LabelField("Bleeding", "Bandaged");
            }

            foreach (var condition in injury.Conditions) {
                DrawProgressBar(condition.Condition.name, condition.Progress, 1f, condition.Condition.editorColor);
            }

            EditorGUILayout.EndVertical();
        }

        private static string PartName(AlpineLib.Body.BodyPart part) {
            if (!string.IsNullOrEmpty(part.Definition.displayName)) return part.Definition.displayName;

            return part.Definition.name;
        }

        private static void DrawProgressBar(string label, float value, float max, Color color) {
            var rect = GUILayoutUtility.GetRect(18, 18, GUILayout.ExpandWidth(true));

            var oldColor = GUI.color;
            GUI.color = new Color(0.2f, 0.2f, 0.2f);
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture);

            var fillRect = new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value / max), rect.height);
            GUI.color = color;
            GUI.DrawTexture(fillRect, EditorGUIUtility.whiteTexture);

            GUI.color = Color.white;
            var style = new GUIStyle(EditorStyles.miniLabel) {
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Label(rect, $"{label}: {value:P0}", style);

            GUI.color = oldColor;
        }
    }
}
