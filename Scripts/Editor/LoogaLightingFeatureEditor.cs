using LoogaSoft.Lighting;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using LightingModelSelection = System.Action<int>;

namespace LoogaSoft.Lighting.Editor
{
    [CustomEditor(typeof(LoogaLightingFeature))]
    internal sealed class LoogaLightingFeatureEditor : LoogaEditorBase
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawLoogaSoftHeader();
            DrawDeferredPlusWarning();

            DrawSection("Lighting Model", "LoogaLightingFeature.LightingModel", true, () =>
            {
                DrawLightingModelPopup();
                DrawLightingModelProfile();
                DrawLightingModelNotice();
            });

            DrawSection("Baked Lighting", "LoogaLightingFeature.BakedLighting", false, () =>
            {
                EditorGUILayout.HelpBox(
                    "Unity Lightmapper, Bakery lightmaps, APV, and light probes contribute neutral diffuse GI. " +
                    "Looga applies the selected model to real-time direct lights and reflection probes. " +
                    "Mixed-light shadow masks and subtractive lighting are preserved, but a finished lightmap " +
                    "does not retain enough per-light direction data to re-evaluate every model's diffuse BRDF.",
                    MessageType.Info);
            });

            DrawSection("Material Features", "LoogaLightingFeature.MaterialFeatures", true, () =>
            {
                DrawProperty(serializedObject, "enableAdvancedMaterialData", "Enable Advanced Material Data");
                DrawProperty(serializedObject, "enableSubsurfaceScattering", "Enable Subsurface Scattering");
                DrawProperty(serializedObject, "enableBacklighting", "Enable Backlighting");

                SerializedProperty backlighting = serializedObject.FindProperty("enableBacklighting");
                if (backlighting != null && backlighting.boolValue)
                {
                    EditorGUI.indentLevel++;
                    DrawProperty(serializedObject, "backlightingIntensity", "Intensity");
                    EditorGUI.indentLevel--;
                }
            });

            DrawToggleSection(
                "Tonemapper",
                "LoogaLightingFeature.Tonemapper",
                false,
                serializedObject.FindProperty("enableTonemapper"),
                () => EditorGUILayout.HelpBox(
                    "Applies the Looga tonemapping pass after post-processing.",
                    MessageType.Info));

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawDeferredPlusWarning()
        {
            if (!TryGetRendererMode(out int renderingMode))
                return;

            if (renderingMode == 3)
                return;

            EditorGUILayout.HelpBox($"Looga Lighting requires URP Deferred+ to function properly. Current renderer mode is {GetRenderingModeName(renderingMode)}.", MessageType.Warning);
            EditorGUILayout.Space(2);
        }

        private void DrawLightingModelNotice()
        {
            SerializedProperty model = serializedObject.FindProperty("activeLightingModel");
            if (model == null)
                return;

            string message = model.intValue switch
            {
                0 => "Burley diffuse and Disney's base GGX response using the metallic/specular data available in URP's GBuffer. This is the base BRDF, not the complete Disney Principled material model.",
                1 => "Source 2 inspired: Lambert/GGX PBR with bent-normal specular occlusion. Public Source 2 behavior is not fully documented.",
                3 => "Minnaert diffuse with an independent material k coefficient and selectable indirect specular family.",
                4 => "Overwatch inspired: softly wrapped diffuse with broad, controlled PBR highlights. Blizzard has not published an exact production BRDF.",
                5 => "Full Oren-Nayar rough diffuse coefficients with independent material sigma and selectable indirect specular family.",
                6 => "Arkane inspired: feathered band lighting and shaped highlights. This targets the studio's illustrative presentation rather than a published BRDF.",
                100 => "Custom model profile: assemble diffuse, direct specular, indirect specular, roughness response, and stylization controls independently.",
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(message))
                EditorGUILayout.HelpBox(message, MessageType.Info);
        }

        private void DrawLightingModelProfile()
        {
            SerializedProperty model = serializedObject.FindProperty("activeLightingModel");
            if (model == null)
                return;

            if (model.intValue == (int)LoogaLightingFeature.LightingModel.Custom)
            {
                SerializedProperty profile =
                    serializedObject.FindProperty("customLightingModelProfile");
                EditorGUILayout.PropertyField(profile, new GUIContent("Model Profile"));

                if (profile.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        "Assign a Lighting Model Profile. Until then, Custom uses the Disney/Burley defaults.",
                        MessageType.Warning);
                }
                else if (GUILayout.Button("Select Profile"))
                {
                    Selection.activeObject = profile.objectReferenceValue;
                    EditorGUIUtility.PingObject(profile.objectReferenceValue);
                }

                return;
            }

            if (GUILayout.Button("Create Custom From Preset..."))
                CreateCustomProfileFromPreset((LoogaLightingFeature.LightingModel)model.intValue);
        }

        private void CreateCustomProfileFromPreset(
            LoogaLightingFeature.LightingModel preset)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Lighting Model Profile",
                $"{GetLightingModelDisplayName((int)preset).Replace("/", " ")} Custom",
                "asset",
                "Choose where to save the editable lighting model profile.");
            if (string.IsNullOrEmpty(path))
                return;
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            LoogaLightingModelProfile profile =
                CreateInstance<LoogaLightingModelProfile>();
            profile.ApplyPreset(preset);
            profile.description =
                $"Editable custom model initialized from the {GetLightingModelDisplayName((int)preset)} preset.";
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();

            serializedObject.Update();
            serializedObject.FindProperty("customLightingModelProfile")
                .objectReferenceValue = profile;
            serializedObject.FindProperty("activeLightingModel").intValue =
                (int)LoogaLightingFeature.LightingModel.Custom;
            serializedObject.ApplyModifiedProperties();
            LoogaMasterDeferredCompileProfile.ScheduleRefresh();
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
        }

        private void DrawLightingModelPopup()
        {
            SerializedProperty model = serializedObject.FindProperty("activeLightingModel");
            if (model == null)
                return;

            Rect row = EditorGUILayout.GetControlRect();
            Rect popupRect = EditorGUI.PrefixLabel(row, new GUIContent("Active Lighting Model"));
            string displayName = GetLightingModelDisplayName(model.intValue);

            if (!EditorGUI.DropdownButton(popupRect, new GUIContent(displayName), FocusType.Keyboard))
                return;

            LightingModelDropdown dropdown = new LightingModelDropdown(
                new AdvancedDropdownState(),
                selectedValue =>
                {
                    serializedObject.Update();
                    SerializedProperty currentModel = serializedObject.FindProperty("activeLightingModel");
                    if (currentModel == null)
                        return;

                    currentModel.intValue = selectedValue;
                    serializedObject.ApplyModifiedProperties();
                    LoogaMasterDeferredCompileProfile.ScheduleRefresh();
                    Repaint();
                });
            dropdown.Show(popupRect);
        }

        private static string GetLightingModelDisplayName(int value)
        {
            return value switch
            {
                0 => "Disney/Burley",
                1 => "Source 2",
                3 => "Minnaert",
                4 => "Overwatch",
                5 => "Oren-Nayar",
                6 => "Arkane",
                100 => "Custom Profile",
                _ => "Disney/Burley"
            };
        }

        private sealed class LightingModelDropdown : AdvancedDropdown
        {
            private readonly LightingModelSelection _onSelected;

            public LightingModelDropdown(AdvancedDropdownState state, LightingModelSelection onSelected)
                : base(state)
            {
                _onSelected = onSelected;
                minimumSize = new Vector2(220.0f, 180.0f);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                AdvancedDropdownItem root = new AdvancedDropdownItem("Lighting Model");
                root.AddChild(new LightingModelItem("Disney/Burley", 0));
                root.AddChild(new LightingModelItem("Source 2", 1));
                root.AddChild(new LightingModelItem("Minnaert", 3));
                root.AddChild(new LightingModelItem("Overwatch", 4));
                root.AddChild(new LightingModelItem("Oren-Nayar", 5));
                root.AddChild(new LightingModelItem("Arkane", 6));
                root.AddSeparator();
                root.AddChild(new LightingModelItem("Custom Profile", 100));
                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (item is LightingModelItem modelItem)
                    _onSelected(modelItem.Value);
            }
        }

        private sealed class LightingModelItem : AdvancedDropdownItem
        {
            public int Value { get; }

            public LightingModelItem(string name, int value)
                : base(name)
            {
                Value = value;
            }
        }

    }
}
