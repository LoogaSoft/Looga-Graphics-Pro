using LoogaSoft.Lighting;
using UnityEditor;
using UnityEngine;

namespace LoogaSoft.Lighting.Editor
{
    [CustomEditor(typeof(LoogaLightAttenuation))]
    internal sealed class LoogaLightAttenuationEditor : LoogaEditorBase
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawLoogaSoftHeader();
            DrawSection("Distance Falloff", "LoogaLightAttenuation.DistanceFalloff", true, () =>
            {
                DrawProperty(serializedObject, "_mode", "Mode");
                DrawModeProperties();
            });

            EditorGUILayout.HelpBox(
                "Custom falloff affects Looga real-time lighting. Match the falloff in the active light baker when the light is baked or mixed.",
                MessageType.Info);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawModeProperties()
        {
            SerializedProperty modeProperty = serializedObject.FindProperty("_mode");
            if (modeProperty == null)
                return;

            LoogaLightAttenuationMode mode =
                (LoogaLightAttenuationMode)modeProperty.enumValueIndex;

            switch (mode)
            {
                case LoogaLightAttenuationMode.Physical:
                    DrawProperty(serializedObject, "_sourceRadius", "Source Radius");
                    break;
                case LoogaLightAttenuationMode.SoftPhysical:
                    DrawProperty(serializedObject, "_sourceRadius", "Source Radius");
                    DrawProperty(serializedObject, "_rangeFadeStart", "Range Fade Start");
                    break;
                case LoogaLightAttenuationMode.Power:
                    DrawProperty(serializedObject, "_falloffExponent", "Falloff Exponent");
                    break;
                case LoogaLightAttenuationMode.CustomCurve:
                    DrawProperty(serializedObject, "_customCurve", "Attenuation Curve");
                    EditorGUILayout.HelpBox(
                        "The horizontal axis is normalized distance. Zero is the light source. One is the light range.",
                        MessageType.None);
                    break;
            }
        }
    }
}
