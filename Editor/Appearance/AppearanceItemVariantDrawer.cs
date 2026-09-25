using System;
using System.Collections.Generic;
using AlpineLib.Appearance;
using UnityEditor;
using UnityEngine;

namespace AlpineLib.Editor {
    /// <summary>
    /// Draws an item variant; a socket variant picks its attach point from the <c>Socket_*</c> transforms
    /// of its model's body, and falls back to a text field when there is nothing to list.
    /// </summary>
    [CustomPropertyDrawer(typeof(AppearanceItemVariant))]
    public class AppearanceItemVariantDrawer : PropertyDrawer {
        private const string SocketPrefix = "Socket_";
        private const string NoneLabel = "(none)";
        private const string MissingSuffix = " (missing)";
        private const int FieldCount = 4;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            int lines = property.isExpanded ? FieldCount + 1 : 1;
            return lines * EditorGUIUtility.singleLineHeight + (lines - 1) * EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            EditorGUI.BeginProperty(position, label, property);
            Rect line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
            if (property.isExpanded) DrawFields(line, property);

            EditorGUI.EndProperty();
        }

        private static void DrawFields(Rect line, SerializedProperty property) {
            SerializedProperty model = property.FindPropertyRelative("model");
            SerializedProperty attach = property.FindPropertyRelative("attach");
            SerializedProperty attachPoint = property.FindPropertyRelative("attachPoint");
            using (new EditorGUI.IndentLevelScope()) {
                line = NextLine(line);
                EditorGUI.PropertyField(line, model);
                line = NextLine(line);
                EditorGUI.PropertyField(line, property.FindPropertyRelative("prefab"));
                line = NextLine(line);
                EditorGUI.PropertyField(line, attach);
                line = NextLine(line);
                DrawAttachPoint(line, attachPoint, attach, model);
            }
        }

        private static Rect NextLine(Rect line) {
            line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            return line;
        }

        private static void DrawAttachPoint(Rect line, SerializedProperty attachPoint, SerializedProperty attach, SerializedProperty model) {
            List<string> sockets = attach.enumValueIndex == (int)AppearanceAttachMode.Socket
                ? CollectSockets(model.objectReferenceValue as CharacterModel)
                : new List<string>();
            if (sockets.Count == 0) {
                EditorGUI.PropertyField(line, attachPoint);
                return;
            }

            string current = attachPoint.stringValue;
            int currentIndex = sockets.IndexOf(current);
            var labels = new List<string>(sockets);
            if (currentIndex < 0) {
                labels.Insert(0, string.IsNullOrEmpty(current) ? NoneLabel : current + MissingSuffix);
                sockets.Insert(0, current);
                currentIndex = 0;
            }

            int chosenIndex = EditorGUI.Popup(line, attachPoint.displayName, currentIndex, labels.ToArray());
            if (chosenIndex != currentIndex) attachPoint.stringValue = sockets[chosenIndex];
        }

        // Sorted, distinct Socket_* names under the model's body FBX; empty when there is no body.
        private static List<string> CollectSockets(CharacterModel model) {
            var sockets = new List<string>();
            if (model == null || model.BodyModel == null) return sockets;

            foreach (Transform node in model.BodyModel.GetComponentsInChildren<Transform>(true)) {
                if (!node.name.StartsWith(SocketPrefix, StringComparison.Ordinal) || sockets.Contains(node.name)) continue;

                sockets.Add(node.name);
            }

            sockets.Sort(StringComparer.Ordinal);
            return sockets;
        }
    }
}
