using UnityEditor;
using UnityEngine;

namespace LoogaSoft.Lighting.Editor
{
    public class LoogaLitShaderGUI : LoogaShaderGUIBase
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

            DrawSurfaceOptionsSection(materialEditor, properties, "LoogaLit_SurfaceOptions");

            Section("Surface Inputs", "LoogaLit_SurfaceInputs", true, () =>
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
                    DrawPackedMaskMapHint(
                        materialEditor,
                        useMaskMap,
                        maskMap,
                        metallicMap,
                        occlusionMap);

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

            DrawLightingModelInputsSection(materialEditor, properties, "LoogaLit_LightingModelInputs");

            Section("Subsurface Scattering", "LoogaLit_SSSS", true, () =>
            {
                materialEditor.ShaderProperty(useSSSS, "Enable Subsurface Scattering");
                materialEditor.ShaderProperty(ssssColor, "Subsurface Color");
                materialEditor.ShaderProperty(ssssAmbientScatterStrength, "Ambient Scatter Strength");
                materialEditor.ShaderProperty(ssssWidth, "Scatter Width");
            });

            DrawBacklightingSection(materialEditor, properties, "LoogaLit_Backlighting");

            Section("Advanced Options", "LoogaLit_Advanced", false, () =>
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
