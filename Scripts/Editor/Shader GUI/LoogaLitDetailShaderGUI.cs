using UnityEditor;
using UnityEngine;

namespace LoogaSoft.Lighting.Editor
{
    public class LoogaLitDetailShaderGUI : LoogaShaderGUIBase
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
            MaterialProperty baseSmoothness = FindProperty("_BaseSmoothnessScale", properties);

            MaterialProperty useDetailMaps = FindProperty("_UseDetailMaps", properties);
            MaterialProperty detailMode = FindProperty("_DetailMode", properties);
            MaterialProperty detailBlendType = FindProperty("_DetailBlendType", properties);
            MaterialProperty detailBlendMap = FindProperty("_DetailBlendMap", properties);
            MaterialProperty detailBlendStrength = FindProperty("_DetailBlendStrength", properties);
            MaterialProperty detailBaseMap = FindProperty("_DetailBaseMap", properties);
            MaterialProperty detailBaseColor = FindProperty("_DetailBaseColor", properties);
            MaterialProperty detailNormalMap = FindProperty("_DetailNormalMap", properties);
            MaterialProperty detailNormalScale = FindProperty("_DetailNormalScale", properties);
            MaterialProperty useDetailMaskMap = FindProperty("_UseDetailMaskMap", properties);
            MaterialProperty detailMaskMap = FindProperty("_DetailMaskMap", properties);
            MaterialProperty detailMetallicMap = FindProperty("_DetailMetallicMap", properties);
            MaterialProperty detailMetallic = FindProperty("_DetailMetallic", properties);
            MaterialProperty detailOcclusionMap = FindProperty("_DetailOcclusionMap", properties);
            MaterialProperty detailOcclusionStrength = FindProperty("_DetailOcclusionStrength", properties);
            MaterialProperty detailEmissionMap = FindProperty("_DetailEmissionMap", properties);
            MaterialProperty detailEmissionColor = FindProperty("_DetailEmissionColor", properties);
            MaterialProperty detailSmoothnessSource = FindProperty("_DetailSmoothnessTextureChannel", properties);
            MaterialProperty detailBaseSmoothness = FindProperty("_DetailBaseSmoothnessScale", properties);

            // NEW SSSS Properties
            MaterialProperty useSSSS = FindProperty("_UseSSSS", properties);
            MaterialProperty ssssColor = FindProperty("_SubsurfaceColor", properties);
            MaterialProperty ssssAmbientScatterStrength = FindProperty("_AmbientScatterStrength", properties);
            MaterialProperty ssssWidth = FindProperty("_ScatterWidth", properties);
            MaterialProperty thicknessMap = FindProperty("_ThicknessMap", properties);
            MaterialProperty transmissionStrength = FindProperty("_TransmissionStrength", properties);
            MaterialProperty transmissionShadowSoftness = FindProperty("_TransmissionShadowSoftness", properties);

            MaterialProperty specHighlights = FindProperty("_SpecularHighlights", properties, false);
            MaterialProperty envReflections = FindProperty("_EnvironmentReflections", properties, false);

            DrawSurfaceOptionsSection(materialEditor, properties, "LoogaLitDetail_SurfaceOptions");

            Section("Surface Inputs", "LoogaLitDetail_SurfaceInputs", true, () =>
            {
                materialEditor.TexturePropertySingleLine(new GUIContent("Base Map"), baseMap, baseColor);
                materialEditor.TexturePropertySingleLine(new GUIContent("Normal Map"), normalMap, normalScale);
                EditorGUILayout.Space(2);
                if (IsSpecularWorkflow(workflowMode))
                {
                    materialEditor.TexturePropertySingleLine(new GUIContent("Specular Map"), specGlossMap, specColor);
                    EditorGUI.indentLevel += 2;
                    materialEditor.ShaderProperty(baseSmoothness, new GUIContent("Master Smoothness"));
                    materialEditor.ShaderProperty(smoothnessSource, new GUIContent("Source"));
                    EditorGUI.indentLevel -= 2;
                    materialEditor.TexturePropertySingleLine(new GUIContent("Occlusion Map"), occlusionMap, occlusionStrength);
                }
                else
                {
                    materialEditor.ShaderProperty(useMaskMap, "Use Mask Map");

                    if (useMaskMap.floatValue > 0.5f)
                    {
                        materialEditor.TexturePropertySingleLine(new GUIContent("Mask Map (M, AO, S)"), maskMap);
                        EditorGUI.indentLevel += 2;
                        materialEditor.ShaderProperty(baseSmoothness, new GUIContent("Master Smoothness"));
                        EditorGUI.indentLevel -= 2;
                    }
                    else
                    {
                        materialEditor.TexturePropertySingleLine(new GUIContent("Metallic Map"), metallicMap, metallic);
                        EditorGUI.indentLevel += 2;
                        materialEditor.ShaderProperty(baseSmoothness, new GUIContent("Master Smoothness"));
                        materialEditor.ShaderProperty(smoothnessSource, new GUIContent("Source"));
                        EditorGUI.indentLevel -= 2;
                        materialEditor.TexturePropertySingleLine(new GUIContent("Occlusion Map"), occlusionMap, occlusionStrength);
                    }
                }

                EditorGUILayout.Space(2);
                DrawEmissionToggle(materialEditor, emissionMap, emissionColor, "_EMISSION", "Emission Map");
                EditorGUILayout.Space();
                materialEditor.TextureScaleOffsetProperty(baseMap);
            });

            DrawLightingModelInputsSection(materialEditor, properties, "LoogaLitDetail_LightingModelInputs");

            Section("Detail Options", "LoogaLitDetail_Detail", true, () =>
            {
                materialEditor.ShaderProperty(useDetailMaps, "Enable Detail Maps");
                if (useDetailMaps.floatValue <= 0.5f)
                    return;

                materialEditor.ShaderProperty(detailMode, "Detail Mode");
                materialEditor.ShaderProperty(detailBlendType, "Detail Blend Type");
                if (detailMode.floatValue > 0.5f)
                    materialEditor.TexturePropertySingleLine(new GUIContent("Blend Mask (R)"), detailBlendMap, detailBlendStrength);
                else
                    materialEditor.ShaderProperty(detailBlendStrength, "Vertex Color Strength");
                EditorGUILayout.Space(4);
                GUILayout.Label("Detail Textures", EditorStyles.boldLabel);
                materialEditor.TexturePropertySingleLine(new GUIContent("Detail Base Map"), detailBaseMap, detailBaseColor);
                materialEditor.TexturePropertySingleLine(new GUIContent("Detail Normal Map"), detailNormalMap, detailNormalScale);
                EditorGUILayout.Space(2);
                materialEditor.ShaderProperty(useDetailMaskMap, "Use Detail Mask Map");

                if (useDetailMaskMap.floatValue > 0.5f)
                {
                    materialEditor.TexturePropertySingleLine(new GUIContent("Detail Mask Map (M, AO, S)"), detailMaskMap);
                    EditorGUI.indentLevel += 2;
                    materialEditor.ShaderProperty(detailBaseSmoothness, new GUIContent("Master Smoothness"));
                    EditorGUI.indentLevel -= 2;
                }
                else
                {
                    materialEditor.TexturePropertySingleLine(new GUIContent("Detail Metallic Map"), detailMetallicMap, detailMetallic);
                    EditorGUI.indentLevel += 2;
                    materialEditor.ShaderProperty(detailBaseSmoothness, new GUIContent("Master Smoothness"));
                    materialEditor.ShaderProperty(detailSmoothnessSource, new GUIContent("Source"));
                    EditorGUI.indentLevel -= 2;
                    materialEditor.TexturePropertySingleLine(new GUIContent("Detail Occlusion Map"), detailOcclusionMap, detailOcclusionStrength);
                }

                EditorGUILayout.Space(2);
                DrawEmissionToggle(materialEditor, detailEmissionMap, detailEmissionColor, "_DETAIL_EMISSION", "Detail Emission Map");
                EditorGUILayout.Space();
                materialEditor.TextureScaleOffsetProperty(detailBaseMap);
            });

            Section("Subsurface Scattering", "LoogaLitDetail_SSSS", true, () =>
            {
                materialEditor.ShaderProperty(useSSSS, "Enable Subsurface Scattering");
                materialEditor.ShaderProperty(ssssColor, "Subsurface Color");
                materialEditor.ShaderProperty(ssssAmbientScatterStrength, "Ambient Scatter Strength");
                materialEditor.ShaderProperty(ssssWidth, "Scatter Width");
            });

            DrawBacklightingSection(materialEditor, properties, "LoogaLitDetail_Backlighting");

            Section("Advanced Options", "LoogaLitDetail_Advanced", false, () =>
            {
                if (specHighlights != null) materialEditor.ShaderProperty(specHighlights, "Specular Highlights");
                if (envReflections != null) materialEditor.ShaderProperty(envReflections, "Environment Reflections");
                materialEditor.EnableInstancingField();
                materialEditor.RenderQueueField();
            });
        }
    }
}
