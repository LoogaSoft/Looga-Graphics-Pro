using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LoogaSoft.Lighting.Editor
{
    public abstract class LoogaEditorBase : UnityEditor.Editor
    {
        protected static GUIStyle HeaderStyle;
        protected static GUIStyle BoxStyle;

        protected static void EnsureStyles()
        {
            if (HeaderStyle != null)
                return;

            HeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                padding = new RectOffset(0, 0, 0, 4)
            };

            BoxStyle = new GUIStyle("HelpBox")
            {
                padding = new RectOffset(8, 8, 6, 6)
            };
        }

        public static void DrawLoogaSoftHeader()
        {
            GUIStyle titleStyle = new GUIStyle()
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f) }
            };

            EditorGUILayout.Space(3);
            GUILayout.Label("-  LoogaSoft  -", titleStyle);
            EditorGUILayout.Space(3);
        }

        protected static void DrawSection(string title, string prefKey, bool defaultShow, Action content)
        {
            EnsureStyles();

            bool show = EditorPrefs.GetBool(prefKey, defaultShow);

            EditorGUILayout.BeginVertical(BoxStyle);
            Rect full = GUILayoutUtility.GetRect(GUIContent.none, HeaderStyle);
            full.height += 4f;
            full.y -= 2f;
            full.width += 8f;
            full.x -= 4f;

            Rect text = new Rect(full.x + 4, full.y + 1, full.width - 24, full.height);
            Rect arrow = new Rect(full.xMax - 10, full.y, 15, full.height);

            if (full.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(full, new Color(1, 1, 1, 0.05f));

            GUI.Label(text, title, HeaderStyle);

            bool newShow = EditorGUI.Foldout(arrow, show, GUIContent.none);
            if (Event.current.type == EventType.MouseDown && full.Contains(Event.current.mousePosition) && Event.current.button == 0)
            {
                newShow = !show;
                Event.current.Use();
            }

            if (newShow != show)
            {
                EditorPrefs.SetBool(prefKey, newShow);
                show = newShow;
            }

            if (show)
            {
                EditorGUILayout.Space(2);
                content();
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndVertical();
        }

        protected static void DrawToggleSection(
            string title,
            string prefKey,
            bool defaultShow,
            SerializedProperty enabled,
            Action content)
        {
            EnsureStyles();

            bool show = EditorPrefs.GetBool(prefKey, defaultShow);
            EditorGUILayout.BeginVertical(BoxStyle);
            Rect full = GUILayoutUtility.GetRect(GUIContent.none, HeaderStyle);
            full.height += 4f;
            full.y -= 2f;
            full.width += 8f;
            full.x -= 4f;

            Rect toggle = new Rect(full.x + 3f, full.y + 1f, 18f, full.height);
            Rect text = new Rect(full.x + 24f, full.y + 1f, full.width - 44f, full.height);
            Rect arrow = new Rect(full.xMax - 10f, full.y, 15f, full.height);

            if (full.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(full, new Color(1f, 1f, 1f, 0.05f));

            EditorGUI.BeginChangeCheck();
            bool enabledValue = EditorGUI.Toggle(toggle, enabled.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                enabled.boolValue = enabledValue;
                if (enabledValue)
                {
                    show = true;
                    EditorPrefs.SetBool(prefKey, true);
                }
            }

            using (new EditorGUI.DisabledScope(!enabled.boolValue))
            {
                GUI.Label(text, title, HeaderStyle);
                bool newShow = enabled.boolValue
                    ? EditorGUI.Foldout(arrow, show, GUIContent.none)
                    : false;

                if (Event.current.type == EventType.MouseDown &&
                    Event.current.button == 0 &&
                    full.Contains(Event.current.mousePosition) &&
                    !toggle.Contains(Event.current.mousePosition) &&
                    enabled.boolValue)
                {
                    newShow = !show;
                    Event.current.Use();
                }

                if (newShow != show && enabled.boolValue)
                {
                    show = newShow;
                    EditorPrefs.SetBool(prefKey, show);
                }
            }

            if (enabled.boolValue && show)
            {
                EditorGUILayout.Space(2);
                content();
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndVertical();
        }

        protected static void DrawProperty(SerializedObject serializedObject, string propertyName, string label)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property != null)
                EditorGUILayout.PropertyField(property, new GUIContent(label));
        }

        protected bool TryGetRendererMode(out int renderingMode)
        {
            renderingMode = -1;

            string path = AssetDatabase.GetAssetPath(target);
            if (string.IsNullOrEmpty(path))
                return false;

            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (UnityEngine.Object asset in assets)
            {
                if (asset is not UniversalRendererData rendererData ||
                    !RendererContainsFeature(rendererData))
                {
                    continue;
                }

                SerializedObject rendererObject = new SerializedObject(rendererData);
                SerializedProperty modeProperty = rendererObject.FindProperty("m_RenderingMode");
                if (modeProperty == null)
                    return false;

                renderingMode = modeProperty.intValue;
                return true;
            }

            return false;
        }

        protected static string GetRenderingModeName(int renderingMode)
        {
            return renderingMode switch
            {
                0 => "Forward",
                1 => "Deferred",
                2 => "Forward+",
                3 => "Deferred+",
                _ => "Unknown"
            };
        }

        private bool RendererContainsFeature(UniversalRendererData rendererData)
        {
            SerializedObject rendererObject = new SerializedObject(rendererData);
            SerializedProperty features = rendererObject.FindProperty("m_RendererFeatures");
            if (features == null || !features.isArray)
                return false;

            for (int i = 0; i < features.arraySize; i++)
            {
                SerializedProperty feature = features.GetArrayElementAtIndex(i);
                if (feature.objectReferenceValue == target)
                    return true;
            }

            return false;
        }
    }
}
