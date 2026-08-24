using UnityEditor;
using UnityEngine;

namespace LoogaSoft.Lighting.Editor
{
    public class LoogaCrystalShaderGUI : LoogaShaderGUIBase
    {
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            Styles();
            DrawLoogaSoftHeader();

            MaterialProperty baseColor = FindProperty("_BaseColor", properties);
            MaterialProperty normalMap = FindProperty("_BumpMap", properties);
            MaterialProperty normalScale = FindProperty("_BumpScale", properties);
            MaterialProperty smoothness = FindProperty("_Smoothness", properties);
            
            MaterialProperty innerMap = FindProperty("_InnerMap", properties);
            MaterialProperty innerColor = FindProperty("_InnerColor", properties);
            MaterialProperty parallaxDepth = FindProperty("_ParallaxDepth", properties);
            
            MaterialProperty thicknessInfluence = FindProperty("_ThicknessInfluence", properties);
            MaterialProperty edgeSharpness = FindProperty("_EdgeSharpness", properties);
            MaterialProperty coreDensity = FindProperty("_CoreDensity", properties);
            
            MaterialProperty distortion = FindProperty("_Distortion", properties);
            
            MaterialProperty specHighlights = FindProperty("_SpecularHighlights", properties, false);
            MaterialProperty envReflections = FindProperty("_EnvironmentReflections", properties, false);

            DrawSurfaceOptionsSection(materialEditor, properties, "LoogaCrystal_SurfaceOptions");

            Section("Surface Inputs", "LoogaCrystal_SurfaceInputs", true, () =>
            {
                materialEditor.ShaderProperty(baseColor, "Outer Shell Tint & Opacity");
                EditorGUILayout.Space(2);
                materialEditor.TexturePropertySingleLine(new GUIContent("Normal Map (Facets & Scratches)"), normalMap, normalScale);
                EditorGUILayout.Space(2);
                materialEditor.ShaderProperty(smoothness, "Surface Smoothness");
            });

            Section("Volumetric Core", "LoogaCrystal_Core", true, () =>
            {
                materialEditor.TexturePropertySingleLine(new GUIContent("Inner Cloud/Fractal Texture"), innerMap, innerColor);
                EditorGUILayout.Space(2);
                materialEditor.ShaderProperty(parallaxDepth, "Parallax Depth");
                
                EditorGUILayout.Space(4);
                GUILayout.Label("Core Edge Masking", EditorStyles.boldLabel);
                
                materialEditor.ShaderProperty(thicknessInfluence, "Geometric Edge Influence");
                
                EditorGUI.indentLevel += 1;
                
                // Disable Sharpness slider if we are 100% using Fresnel
                EditorGUI.BeginDisabledGroup(thicknessInfluence.floatValue == 0.0f);
                materialEditor.ShaderProperty(edgeSharpness, "Edge Sharpness");
                EditorGUI.EndDisabledGroup();
                EditorGUI.indentLevel -= 1;
                
                // Disable Fresnel slider if we are 100% using Geometric Edges
                EditorGUI.BeginDisabledGroup(thicknessInfluence.floatValue == 1.0f);
                materialEditor.ShaderProperty(coreDensity, "Camera Fresnel Density");
                EditorGUI.EndDisabledGroup();
            });

            Section("Optical Refraction", "LoogaCrystal_Optics", true, () =>
            {
                materialEditor.ShaderProperty(distortion, "Refraction Index (IOR)");
            });

            Section("Advanced Options", "LoogaCrystal_Advanced", false, () =>
            {
                if (specHighlights != null) materialEditor.ShaderProperty(specHighlights, "Specular Highlights");
                if (envReflections != null) materialEditor.ShaderProperty(envReflections, "Environment Reflections");
                
                EditorGUILayout.Space();
                materialEditor.EnableInstancingField();
                materialEditor.RenderQueueField();
            });
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            MaterialEditor.ApplyMaterialPropertyDrawers(material);
        }
    }
}
