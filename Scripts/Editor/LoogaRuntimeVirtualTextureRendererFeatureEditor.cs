using LoogaSoft.Rendering.VirtualTexturing;
using UnityEditor;
using UnityEngine;

namespace LoogaSoft.Lighting.Editor
{
    [CustomEditor(typeof(LoogaRuntimeVirtualTextureRendererFeature))]
    internal sealed class LoogaRuntimeVirtualTextureRendererFeatureEditor : LoogaEditorBase
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawLoogaSoftHeader();

            DrawSection("Coverage", "LoogaRVT.Coverage", true, () =>
            {
                DrawProperty(serializedObject, "clipmapCount", "Clipmap Count");
                DrawProperty(serializedObject, "firstClipmapExtent", "First Extent");
                DrawProperty(serializedObject, "writerLayerMask", "Writer Layers");
                DrawProperty(serializedObject, "cameraScope", "Cameras");
            });

            DrawSection("Cache", "LoogaRVT.Cache", true, () =>
            {
                DrawProperty(serializedObject, "atlasResolution", "Atlas Resolution");
                DrawProperty(serializedObject, "pagesPerClipmapAxis", "Pages Per Axis");
                DrawProperty(serializedObject, "clipmapBlendWidth", "Level Blend Width");
                DrawProperty(serializedObject, "updateMode", "Update Mode");
                EditorGUILayout.HelpBox(
                    GetMemoryEstimate(),
                    MessageType.None);
                EditorGUILayout.HelpBox(
                    "On Page Movement is best for static surfaces. Dynamic writers can call LoogaRuntimeVirtualTextureRendererFeature.RequestRefresh().",
                    MessageType.Info);
            });

            DrawSection("Height Encoding", "LoogaRVT.Height", false, () =>
            {
                DrawProperty(serializedObject, "minimumWorldHeight", "Minimum World Height");
                DrawProperty(serializedObject, "maximumWorldHeight", "Maximum World Height");
            });

            DrawSection("Capture", "LoogaRVT.Capture", false, () =>
            {
                DrawProperty(serializedObject, "capturePadding", "Vertical Padding");
                DrawProperty(serializedObject, "includeAlphaTestedGeometry", "Include Alpha Tested");
                EditorGUILayout.HelpBox(
                    "The current camera culling results limit writers. The cache always covers visible geometry and does not run a second CPU cull.",
                    MessageType.Info);
            });

            serializedObject.ApplyModifiedProperties();
        }

        private string GetMemoryEstimate()
        {
            SerializedProperty resolutionProperty = serializedObject.FindProperty("atlasResolution");
            int resolution = resolutionProperty != null ? resolutionProperty.intValue : 2048;
            long colorBytes = (long)resolution * resolution * 4 * 3;
            long depthBytes = (long)resolution * resolution * 4;
            float memoryMiB = (colorBytes + depthBytes) / (1024f * 1024f);
            return $"Estimated cache memory: {memoryMiB:0.#} MiB. The cache uses three color atlases and one depth atlas.";
        }
    }
}
