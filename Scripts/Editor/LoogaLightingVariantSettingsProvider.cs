using UnityEditor;
using UnityEngine;

namespace LoogaSoft.Lighting.Editor
{
    internal static class LoogaLightingVariantSettingsProvider
    {
        private const string SettingsPath = "Project/LoogaSoft/Lighting/Shader Variants";

        [SettingsProvider]
        private static SettingsProvider CreateProvider()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "Shader Variants",
                guiHandler = DrawSettings,
                keywords = new[]
                {
                    "Looga", "Lighting", "Shader", "Variants", "Stripping", "Deferred", "URP"
                }
            };
        }

        [MenuItem("LoogaSoft/Graphics Pro/Lighting/Shader Variants", priority = 21)]
        private static void OpenSettings()
        {
            SettingsService.OpenProjectSettings(SettingsPath);
        }

        private static void DrawSettings(string searchContext)
        {
            LoogaLightingVariantProfile profile = LoogaLightingVariantProfile.instance;
            SerializedObject serializedProfile = new SerializedObject(profile);
            serializedProfile.Update();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Master Deferred Shader", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This profile limits only the shader states that the active URP assets and Looga Deferred+ " +
                "renderers can request. It does not remove lighting models or alter their HLSL implementations.",
                MessageType.Info);

            DrawEstimate(profile);
            EditorGUILayout.Space(4f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Detect From Project"))
                    DetectAndApply(profile);

                if (GUILayout.Button("Validate"))
                    Validate(profile, true);

                if (GUILayout.Button("Reset Compatibility"))
                {
                    profile.ResetToCompatibilityDefaults();
                    profile.SaveSettings();
                    LoogaMasterDeferredCompileProfile.ScheduleRefresh();
                    serializedProfile.Update();
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Editor Compilation", EditorStyles.boldLabel);
            DrawProperty(serializedProfile, "_useEditorCompileProfile", "Use Project Compile Profile");
            DrawProperty(serializedProfile, "_autoDetectEditorCompileProfile", "Auto-Detect On Script Reload");
            EditorGUILayout.HelpBox(
                "Looga Lighting generates reduced Master Deferred shaders only for models referenced by active " +
                "Graphics and Quality URP renderer assets. Generated shaders are updated on demand and unchanged " +
                "shaders are not reimported.",
                MessageType.Info);

            if (GUILayout.Button("Regenerate Editor Shaders"))
                LoogaMasterDeferredCompileProfile.RefreshFromProject(true, true);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Build", EditorStyles.boldLabel);
            DrawProperty(serializedProfile, "_stripMasterDeferredShader", "Strip Master Deferred Shader");
            DrawProperty(serializedProfile, "_detectBeforeBuild", "Detect Before Build");
            DrawProperty(serializedProfile, "_validateBeforeBuild", "Validate Before Build");
            DrawProperty(serializedProfile, "_logBuildReport", "Log Build Report");

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Shadow Variants", EditorStyles.boldLabel);
            DrawProperty(serializedProfile, "_mainLightShadows", "Main Light Shadows");
            DrawProperty(serializedProfile, "_softShadows", "Soft Shadows");

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Renderer Variants", EditorStyles.boldLabel);
            DrawProperty(serializedProfile, "_gBufferOctahedralNormals", "Octahedral GBuffer Normals");
            DrawProperty(serializedProfile, "_screenSpaceOcclusion", "Screen-Space Occlusion");
            DrawProperty(serializedProfile, "_reflectionProbeBlending", "Reflection Probe Blending");
            DrawProperty(serializedProfile, "_reflectionProbeBoxProjection", "Reflection Probe Box Projection");
            DrawProperty(serializedProfile, "_reflectionProbeAtlas", "Reflection Probe Atlas");
            DrawProperty(serializedProfile, "_reflectionProbeRotation", "Reflection Probe Rotation");

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Lighting Variants", EditorStyles.boldLabel);
            DrawProperty(serializedProfile, "_additionalLightShadows", "Additional Light Shadows");
            DrawProperty(serializedProfile, "_lightmapShadowMixing", "Lightmap Shadow Mixing");
            DrawProperty(serializedProfile, "_shadowMask", "Shadow Mask");
            DrawProperty(serializedProfile, "_deferredMixedLighting", "Deferred Mixed Lighting");
            DrawProperty(serializedProfile, "_lightCookies", "Light Cookies");
            DrawProperty(serializedProfile, "_lightLayers", "Light Layers");

            if (serializedProfile.ApplyModifiedProperties())
            {
                profile.SaveSettings();
                LoogaMasterDeferredCompileProfile.ScheduleRefresh();
            }
        }

        private static void DetectAndApply(LoogaLightingVariantProfile profile)
        {
            LoogaLightingVariantDetectionResult result = LoogaLightingVariantDetector.Detect();
            if (!result.IsValid)
            {
                EditorUtility.DisplayDialog("Looga Lighting Variants", result.Summary, "OK");
                return;
            }

            profile.Apply(result.Requirements);
            profile.SaveSettings();
            LoogaMasterDeferredCompileProfile.RefreshFromProject(true, true);
            int modelCount = LoogaMasterDeferredCompileProfile.ReferencedModelCount;
            Debug.Log($"[Looga Lighting] {result.Summary} Estimated master variants: " +
                      $"{profile.EstimateMasterVariants(modelCount):N0}/" +
                      $"{LoogaLightingVariantProfile.FullMasterVariantCount:N0} across {modelCount} referenced model(s).");
        }

        private static bool Validate(LoogaLightingVariantProfile profile, bool showDialog)
        {
            LoogaLightingVariantDetectionResult result = LoogaLightingVariantDetector.Detect();
            string missingRequirement = string.Empty;
            bool valid = result.IsValid && profile.Contains(result.Requirements, out missingRequirement);
            string message = !result.IsValid
                ? result.Summary
                : valid
                    ? "The profile contains every shader state required by the active URP assets and Looga renderers."
                    : $"The profile excludes required {missingRequirement}. Detect the project again before building.";

            if (showDialog)
                EditorUtility.DisplayDialog("Looga Lighting Variant Validation", message, "OK");

            return valid;
        }

        private static void DrawEstimate(LoogaLightingVariantProfile profile)
        {
            int modelCount = LoogaMasterDeferredCompileProfile.ReferencedModelCount;
            int retained = profile.EstimateMasterVariants(modelCount);
            float reduction = 1f - retained / (float)LoogaLightingVariantProfile.FullMasterVariantCount;
            EditorGUILayout.LabelField(
                "Estimated theoretical variants",
                $"{retained:N0} / {LoogaLightingVariantProfile.FullMasterVariantCount:N0} " +
                $"({reduction:P1} reduction, {modelCount} model(s))");
        }

        private static void DrawProperty(SerializedObject target, string propertyName, string label)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property != null)
                EditorGUILayout.PropertyField(property, new GUIContent(label));
        }
    }
}
