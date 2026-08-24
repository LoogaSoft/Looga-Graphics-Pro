using UnityEditor;
using UnityEngine;

namespace LoogaSoft.Lighting.Editor
{
    [CustomEditor(typeof(LoogaGtaoRendererFeature))]
    internal sealed class LoogaGtaoRendererFeatureEditor : LoogaEditorBase
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawLoogaSoftHeader();
            DrawDeferredPlusWarning();

            DrawToggleSection(
                "GTAO",
                "LoogaGtaoRendererFeature.Gtao",
                true,
                serializedObject.FindProperty("enable"),
                () =>
                {
                    DrawProperty(serializedObject, "generateBentNormals", "Generate Bent Normals");
                    DrawProperty(serializedObject, "debugMode", "Debug Mode");
                    DrawProperty(serializedObject, "radius", "Radius");
                    DrawProperty(serializedObject, "intensity", "Intensity");

                    SerializedProperty bentNormals =
                        serializedObject.FindProperty("generateBentNormals");
                    if (bentNormals != null && !bentNormals.boolValue)
                    {
                        EditorGUILayout.HelpBox(
                            "AO-only mode writes the surface normal into the shared occlusion texture. " +
                            "Bent-normal debug views are not meaningful in this mode.",
                            MessageType.Info);
                    }
                });

            DrawSection("Quality", "LoogaGtaoRendererFeature.Quality", true, () =>
            {
                DrawProperty(serializedObject, "sliceCount", "Slice Count");
                DrawProperty(serializedObject, "stepCount", "Step Count");
                DrawProperty(serializedObject, "blurRadius", "Blur Radius");
            });

            DrawSection("Lighting Influence", "LoogaGtaoRendererFeature.LightingInfluence", true, () =>
            {
                DrawProperty(serializedObject, "directLightStrength", "Direct Light Strength");
                DrawProperty(serializedObject, "indirectLightStrength", "Indirect Light Strength");
            });

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawDeferredPlusWarning()
        {
            if (!TryGetRendererMode(out int renderingMode) || renderingMode == 3)
                return;

            EditorGUILayout.HelpBox(
                $"Looga GTAO requires URP Deferred+. Current renderer mode is " +
                $"{GetRenderingModeName(renderingMode)}.",
                MessageType.Warning);
            EditorGUILayout.Space(2.0f);
        }
    }
}
