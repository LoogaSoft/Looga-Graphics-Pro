using UnityEditor;
using UnityEngine;

namespace LoogaSoft.Lighting.Editor
{
    [CustomEditor(typeof(LoogaLightingModelProfile))]
    internal sealed class LoogaLightingModelProfileEditor : LoogaEditorBase
    {
        private LoogaLightingFeature.LightingModel _preset =
            LoogaLightingFeature.LightingModel.DisneyBurley;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawLoogaSoftHeader();

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("description"));

            DrawPresetControls();

            SerializedProperty settings = serializedObject.FindProperty("settings");
            DrawSection("Model Structure", "LoogaLightingModelProfile.Structure", true, () =>
            {
                DrawRelative(settings, "diffuseModel", "Diffuse Model");
                DrawRelative(settings, "directSpecularModel", "Direct Specular");
                DrawRelative(settings, "indirectSpecularModel", "Indirect Specular");
                DrawRelative(settings, "specularOcclusionModel", "Specular Occlusion");
            });

            DrawSection("Response", "LoogaLightingModelProfile.Response", true, () =>
            {
                DrawRelative(settings, "diffuseStrength", "Diffuse Strength");
                DrawRelative(settings, "directSpecularStrength", "Direct Specular Strength");
                DrawRelative(settings, "indirectSpecularStrength", "Indirect Specular Strength");
            });

            DrawSection("Diffuse Shape", "LoogaLightingModelProfile.Diffuse", true, () =>
            {
                LoogaDiffuseModel diffuseModel = (LoogaDiffuseModel)
                    settings.FindPropertyRelative("diffuseModel").enumValueIndex;
                switch (diffuseModel)
                {
                    case LoogaDiffuseModel.Minnaert:
                        DrawRelative(settings, "minnaertK", "Minnaert K");
                        break;
                    case LoogaDiffuseModel.OrenNayar:
                        DrawRelative(settings, "orenNayarSigma", "Roughness Sigma");
                        break;
                    case LoogaDiffuseModel.Wrapped:
                        DrawRelative(settings, "diffuseWrap", "Wrap");
                        break;
                    case LoogaDiffuseModel.Banded:
                        DrawRelative(settings, "bandCount", "Band Count");
                        DrawRelative(settings, "bandFeather", "Band Feather");
                        DrawRelative(settings, "bandBlend", "Natural Falloff Blend");
                        break;
                    default:
                        EditorGUILayout.HelpBox(
                            "This diffuse family has no additional shape parameters.",
                            MessageType.None);
                        break;
                }
            });

            DrawSection("Direct Specular", "LoogaLightingModelProfile.DirectSpecular", true, () =>
            {
                DrawRelative(settings, "directRoughnessScale", "Roughness Scale");
                DrawRelative(settings, "directRoughnessBias", "Roughness Bias");
                DrawRelative(settings, "secondarySpecularWeight", "Secondary Lobe");
                if (settings.FindPropertyRelative("secondarySpecularWeight").floatValue > 0.0f)
                {
                    EditorGUI.indentLevel++;
                    DrawRelative(settings, "secondaryRoughnessSpread", "Roughness Spread");
                    EditorGUI.indentLevel--;
                }

                DrawRelative(settings, "highlightShapeStrength", "Highlight Shaping");
                if (settings.FindPropertyRelative("highlightShapeStrength").floatValue > 0.0f)
                {
                    EditorGUI.indentLevel++;
                    DrawRelative(settings, "highlightShapeFloor", "Shape Floor");
                    DrawRelative(settings, "highlightShapeStart", "Shape Start");
                    DrawRelative(settings, "highlightShapeEnd", "Shape End");
                    EditorGUI.indentLevel--;
                }
            });

            DrawSection("Indirect Specular", "LoogaLightingModelProfile.IndirectSpecular", true, () =>
            {
                DrawRelative(settings, "indirectRoughnessScale", "Roughness Scale");
                DrawRelative(settings, "indirectRoughnessBias", "Roughness Bias");
                DrawRelative(settings, "indirectFresnelPower", "Fresnel Power");
                DrawRelative(settings, "grazingOcclusionStrength", "Grazing Occlusion");
                DrawRelative(settings, "edgeOcclusionStrength", "Edge Occlusion");
                if (settings.FindPropertyRelative("edgeOcclusionStrength").floatValue > 0.0f)
                {
                    EditorGUI.indentLevel++;
                    DrawRelative(settings, "edgeOcclusionStart", "Edge Start");
                    DrawRelative(settings, "edgeOcclusionEnd", "Edge End");
                    EditorGUI.indentLevel--;
                }
            });

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawPresetControls()
        {
            DrawSection("Initialize From Preset", "LoogaLightingModelProfile.Preset", false, () =>
            {
                _preset = (LoogaLightingFeature.LightingModel)EditorGUILayout.EnumPopup(
                    "Preset", _preset);

                if (_preset == LoogaLightingFeature.LightingModel.Custom)
                    _preset = LoogaLightingFeature.LightingModel.DisneyBurley;

                if (!GUILayout.Button("Apply Preset"))
                    return;

                LoogaLightingModelProfile profile =
                    (LoogaLightingModelProfile)target;
                Undo.RecordObject(profile, "Apply Lighting Model Preset");
                profile.ApplyPreset(_preset);
                EditorUtility.SetDirty(profile);
                serializedObject.Update();
            });
        }

        private static void DrawRelative(
            SerializedProperty parent, string propertyName, string label)
        {
            SerializedProperty property = parent.FindPropertyRelative(propertyName);
            if (property != null)
                EditorGUILayout.PropertyField(property, new GUIContent(label), true);
        }
    }
}
