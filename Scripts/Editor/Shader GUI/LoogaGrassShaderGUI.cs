using UnityEditor;
using UnityEngine;

namespace LoogaSoft.Lighting.Editor
{
    public class LoogaGrassShaderGUI : LoogaShaderGUIBase
    {
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            Styles();
            DrawLoogaSoftHeader();

            MaterialProperty baseMap = FindProperty("_BaseMap", properties);
            MaterialProperty cutoff = FindProperty("_Cutoff", properties);
            MaterialProperty normalMap = FindProperty("_BumpMap", properties);
            MaterialProperty normalScale = FindProperty("_BumpScale", properties);
            MaterialProperty smoothness = FindProperty("_Smoothness", properties);

            // NEW SSSS Properties
            MaterialProperty useSSSS = FindProperty("_UseSSSS", properties);
            MaterialProperty ssssColor = FindProperty("_SubsurfaceColor", properties);
            MaterialProperty ssssAmbientScatterStrength =  FindProperty("_AmbientScatterStrength", properties);
            MaterialProperty ssssWidth = FindProperty("_ScatterWidth", properties);
            MaterialProperty thicknessMap = FindProperty("_ThicknessMap", properties);
            MaterialProperty transmissionStrength = FindProperty("_TransmissionStrength", properties);
            MaterialProperty transmissionShadowSoftness = FindProperty("_TransmissionShadowSoftness", properties);

            MaterialProperty windInfluence = FindProperty("_WindInfluence", properties);
            MaterialProperty windTint = FindProperty("_WindTint", properties);
            MaterialProperty windTintStrength = FindProperty("_WindTintStrength", properties);

            MaterialProperty interactionBend = FindProperty("_InteractionBend", properties);

            MaterialProperty globalScale = FindProperty("_GlobalGridScale", properties);
            MaterialProperty globalHue = FindProperty("_GlobalHueVar", properties);
            MaterialProperty globalSat = FindProperty("_GlobalSatVar", properties);
            MaterialProperty globalLum = FindProperty("_GlobalLumVar", properties);

            MaterialProperty localScale = FindProperty("_LocalNoiseScale", properties);
            MaterialProperty localType = FindProperty("_LocalNoiseType", properties);
            MaterialProperty localHue = FindProperty("_LocalHueVar", properties);
            MaterialProperty localSat = FindProperty("_LocalSatVar", properties);
            MaterialProperty localLum = FindProperty("_LocalLumVar", properties);

            MaterialProperty specHighlights = FindProperty("_SpecularHighlights", properties, false);
            MaterialProperty envReflections = FindProperty("_EnvironmentReflections", properties, false);

            DrawSurfaceOptionsSection(materialEditor, properties, "LoogaGrass_SurfaceOptions");

            Section("Surface Inputs", "LoogaGrass_SurfaceInputs", true, () =>
            {
                materialEditor.TexturePropertySingleLine(new GUIContent("Base Map (RGB) Alpha (A)"), baseMap);
                materialEditor.ShaderProperty(cutoff, "Alpha Cutoff");
                EditorGUILayout.Space(2);
                materialEditor.TexturePropertySingleLine(new GUIContent("Normal Map"), normalMap, normalScale);
                EditorGUILayout.Space(2);
                materialEditor.ShaderProperty(smoothness, "Smoothness");
                EditorGUILayout.Space();
                materialEditor.TextureScaleOffsetProperty(baseMap);
            });

            DrawLightingModelInputsSection(materialEditor, properties, "LoogaGrass_LightingModelInputs");

            Section("Subsurface Scattering", "LoogaGrass_SubsurfaceScattering", true, () =>
            {
                materialEditor.ShaderProperty(useSSSS, "Enable Subsurface Scattering");
                materialEditor.ShaderProperty(ssssColor, "Subsurface Color");
                materialEditor.ShaderProperty(ssssAmbientScatterStrength, "Ambient Scatter Strength");
                materialEditor.ShaderProperty(ssssWidth, "Scatter Width");
            });

            DrawBacklightingSection(materialEditor, properties, "LoogaGrass_Backlighting");

            Section("Procedural Wind & Bending", "LoogaGrass_ProceduralWind", true, () =>
            {
                materialEditor.ShaderProperty(windInfluence, "Wind Sway Influence");
                materialEditor.ShaderProperty(interactionBend, "Trample Bend Strength");
                EditorGUILayout.Space(4);
                GUILayout.Label("Wind Gust Tinting", EditorStyles.boldLabel);
                materialEditor.ShaderProperty(windTint, "Gust Tint Color");
                materialEditor.ShaderProperty(windTintStrength, "Gust Tint Strength");
            });

            Section("Global Color Variation", "LoogaGrass_GlobalVar", true, () =>
            {
                materialEditor.ShaderProperty(globalScale, "Grid Scale");
                EditorGUILayout.Space(2);
                DrawMinMaxSlider(globalHue, "Hue Variation", -0.5f, 0.5f);
                DrawMinMaxSlider(globalSat, "Sat Variation", -1f, 1f);
                DrawMinMaxSlider(globalLum, "Lum Variation", -1f, 1f);
            });

            Section("Local Color Variation", "LoogaGrass_LocalVar", true, () =>
            {
                materialEditor.ShaderProperty(localType, "Noise Type");
                materialEditor.ShaderProperty(localScale, "Noise Scale");
                EditorGUILayout.Space(2);
                DrawMinMaxSlider(localHue, "Hue Variation", -0.5f, 0.5f);
                DrawMinMaxSlider(localSat, "Sat Variation", -1f, 1f);
                DrawMinMaxSlider(localLum, "Lum Variation", -1f, 1f);
            });

            Section("Advanced Options", "LoogaGrass_AdvancedOptions", false, () =>
            {
                if (specHighlights != null) materialEditor.ShaderProperty(specHighlights, "Specular Highlights");
                if (envReflections != null) materialEditor.ShaderProperty(envReflections, "Environment Reflections");
                materialEditor.EnableInstancingField();
                materialEditor.RenderQueueField();
            });
        }
    }
}
