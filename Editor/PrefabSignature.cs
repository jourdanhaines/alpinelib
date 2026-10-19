using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AlpineLib.Editor {
    /// <summary>
    /// A text fingerprint of a GameObject hierarchy's serialized state that ignores file IDs: references inside
    /// the hierarchy read as child paths, references outside it as asset GUID + local ID. Lets an assembler that
    /// rebuilds a prefab from scratch skip the save when nothing changed, since every save mints fresh file IDs.
    /// </summary>
    public static class PrefabSignature {
        // Bookkeeping that differs between a fresh object and a loaded asset without being content.
        private static readonly HashSet<string> IgnoredProperties = new HashSet<string> {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject",
            "m_Father", "m_Children", "m_Component", "m_EditorHideFlags", "m_EditorClassIdentifier",
        };

        /// <summary>One line per serialized property of every component, in hierarchy order.</summary>
        public static List<string> Lines(GameObject root) {
            var lines = new List<string>();
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true)) {
                AppendNode(lines, root.transform, node);
            }

            return lines;
        }

        /// <summary>The first line where two signatures differ, or null when they match.</summary>
        public static string FirstDifference(List<string> expected, List<string> actual) {
            int count = Mathf.Min(expected.Count, actual.Count);
            for (int index = 0; index < count; index++) {
                if (expected[index] != actual[index]) return $"want '{expected[index]}', have '{actual[index]}'";
            }

            if (expected.Count == actual.Count) return null;

            return $"{expected.Count} lines wanted, {actual.Count} present";
        }

        private static void AppendNode(List<string> lines, Transform root, Transform node) {
            GameObject gameObject = node.gameObject;
            lines.Add($"node {PathOf(root, node)} layer={gameObject.layer} active={gameObject.activeSelf} tag={gameObject.tag}");

            Component[] components = gameObject.GetComponents<Component>();
            for (int index = 0; index < components.Length; index++) {
                AppendComponent(lines, root, components[index], index);
            }
        }

        private static void AppendComponent(List<string> lines, Transform root, Component component, int index) {
            if (component == null) {
                lines.Add($"  [{index}] missing script");
                return;
            }

            lines.Add($"  [{index}] {component.GetType().FullName}");
            if (IsDrivenRect(component)) return;

            SerializedProperty property = new SerializedObject(component).GetIterator();
            bool enterChildren = true;
            while (property.Next(enterChildren)) {
                // A rect's local position is derived from its anchors, pivot and the parent's size.
                bool isIgnored = IgnoredProperties.Contains(property.name) || (component is RectTransform && property.name == "m_LocalPosition");
                // A reference's own m_FileID/m_PathID children are exactly the IDs this signature abstracts away.
                enterChildren = !isIgnored && property.propertyType != SerializedPropertyType.ObjectReference;
                if (isIgnored) continue;

                string value = Describe(root, property);
                if (value != null) lines.Add($"    {property.propertyPath}={value}");
            }
        }

        // Rects whose stored values are incidental: a screen-space canvas sizes itself from its camera, and a layout
        // group drives its children (saved zeroed, rebuilt at runtime).
        private static bool IsDrivenRect(Component component) {
            if (!(component is RectTransform)) return false;

            Canvas canvas = component.GetComponent<Canvas>();
            if (canvas != null && canvas.renderMode != RenderMode.WorldSpace) return true;

            Transform parent = component.transform.parent;
            return parent != null && parent.GetComponent<LayoutGroup>() != null;
        }

        private static string Describe(Transform root, SerializedProperty property) {
            switch (property.propertyType) {
                case SerializedPropertyType.ObjectReference: return DescribeReference(root, property.objectReferenceValue);
                case SerializedPropertyType.Integer: return property.longValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean: return property.boolValue ? "1" : "0";
                case SerializedPropertyType.Float: return property.doubleValue.ToString("R", CultureInfo.InvariantCulture);
                case SerializedPropertyType.String: return property.stringValue;
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.ArraySize:
                case SerializedPropertyType.Character: return property.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Color: return property.colorValue.ToString("R");
                default: return null;
            }
        }

        private static string DescribeReference(Transform root, Object target) {
            if (target == null) return "null";

            Transform local = TransformOf(target);
            if (local != null && (local == root || local.IsChildOf(root))) return $"local:{PathOf(root, local)}:{target.GetType().Name}";

            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out string guid, out long localId)) return $"asset:{guid}:{localId}";

            return $"object:{target.GetType().Name}:{target.name}";
        }

        private static Transform TransformOf(Object target) {
            if (target is Component component) return component.transform;
            if (target is GameObject gameObject) return gameObject.transform;

            return null;
        }

        private static string PathOf(Transform root, Transform node) {
            var builder = new StringBuilder(node.name);
            for (Transform current = node; current != root && current.parent != null; current = current.parent) {
                builder.Insert(0, current.parent.name + "/");
            }

            return builder.ToString();
        }
    }
}
