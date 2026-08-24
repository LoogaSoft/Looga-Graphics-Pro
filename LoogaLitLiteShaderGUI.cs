using UnityEditor;
using UnityEngine;

namespace LoogaSoft.Lighting.Editor
{
    public sealed class LoogaLitLiteShaderGUI : LoogaShaderGUIBase
    {
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            Styles();
            DrawLoogaSoftHeader();

            MaterialProperty baseMap = FindProperty("_BaseMap", properties);
            MaterialProperty baseColor = FindProperty("_BaseColor", properties);
            MaterialProperty normalMap = FindProperty("_BumpMap", properties);
            MaterialProperty normalScale = FindProperty("_BumpScale", properties);
            MaterialProperty workflowMode = FindProperty("_WorkflowMode", properties, false);
            MaterialProperty useMaskMap = FindProperty("_UseMaskMap", properties);
            MaterialProperty maskMap = FindProperty("_MaskMap", properties);
            MaterialProperty metallicMap = FindProperty("_MetallicGlossMap", properties);
            MaterialProperty specGlossMap = FindProperty("_SpecGlossMap", properties);
            MaterialProperty specColor = FindProperty("_SpecColor", properties);
            MaterialProperty metallic = FindProperty("_Metallic", properties);
            MaterialProperty occlusionMap = FindProperty("_OcclusionMap", properties);
            MaterialProperty occlusionStrength = FindProperty("_OcclusionStrength", properties);
            MaterialProperty emissionMap = FindProperty("_EmissionMap", properties);
            MaterialProperty emissionColor = FindProperty("_EmissionColor", properties);
            MaterialProperty smoothnessSource = FindProperty("_SmoothnessTextureChannel", properties);
            MaterialProperty smoothness = FindProperty("_BaseSmoothnessScale", properties);
            MaterialProperty specHighlights = FindProperty("_SpecularHighlights", properties, false);
            MaterialProperty envReflections = FindProperty("_EnvironmentReflections", properties, false);

            DrawSurfaceOptionsSection(materialEditor, properties, "LoogaLitLite_SurfaceOptions");

            Section("Surface Inputs", "LoogaLitLite_SurfaceInputs", true, () =>
            {
                materialEditor.TexturePropertySingleLine(new GUIContent("Base Map"), baseMap, baseColor);
                materialEditor.TexturePropertySingleLine(new GUIContent("Normal Map"), normalMap, normalScale);
                EditorGUILayout.Space(2);

                if (IsSpecularWorkflow(workflowMode))
                {
                    materialEditor.TexturePropertySingleLine(new GUIContent("Specular Map"), specGlossMap, specColor);
                    materialEditor.ShaderProperty(smoothness, "Smoothness");
                    materialEditor.TexturePropertySingleLine(new GUIContent("Occlusion Map"), occlusionMap, occlusionStrength);
                }
                else
                {
                    materialEditor.ShaderProperty(useMaskMap, "Use Mask Map");
                    DrawPackedMaskMapHint(
                        materialEditor,
                        useMaskMap,
                        maskMap,
                        metallicMap,
                        occlusionMap);
                    if (useMaskMap.floatValue > 0.5f)
                    {
                        materialEditor.TexturePropertySingleLine(new GUIContent("Mask Map (M, AO, S)"), maskMap);
                        materialEditor.ShaderProperty(smoothness, "Smoothness");
                    }
                    else
                    {
                        materialEditor.TexturePropertySingleLine(new GUIContent("Metallic Map"), metallicMap, metallic);
                        materialEditor.ShaderProperty(smoothness, "Smoothness");
                        materialEditor.ShaderProperty(smoothnessSource, "Smoothness Source");
                        materialEditor.TexturePropertySingleLine(new GUIContent("Occlusion Map"), occlusionMap, occlusionStrength);
                    }
                }

                EditorGUILayout.Space(2);
                DrawEmissionToggle(materialEditor, emissionMap, emissionColor, "_EMISSION", "Emission Map");
                EditorGUILayout.Space();
                materialEditor.TextureScaleOffsetProperty(baseMap);
            });

            Section("Advanced Options", "LoogaLitLite_Advanced", false, () =>
            {
                if (specHighlights != null) materialEditor.ShaderProperty(specHighlights, "Specular Highlights");
                if (envReflections != null) materialEditor.ShaderProperty(envReflections, "Environment Reflections");
                EditorGUILayout.Space();
                materialEditor.EnableInstancingField();
                materialEditor.RenderQueueField();
            });
        }
    }
}
