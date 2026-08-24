using UnityEditor;
using UnityEngine;

namespace LoogaSoft.Lighting.Editor
{
    public class LoogaGlassShaderGUI : LoogaShaderGUIBase
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
            
            MaterialProperty smoothnessSource = FindProperty("_SmoothnessTextureChannel", properties);
            MaterialProperty smoothness = FindProperty("_Smoothness", properties);
            
            MaterialProperty distortion = FindProperty("_Distortion", properties);
            
            MaterialProperty specHighlights = FindProperty("_SpecularHighlights", properties, false);
            MaterialProperty envReflections = FindProperty("_EnvironmentReflections", properties, false);

            DrawSurfaceOptionsSection(materialEditor, properties, "LoogaGlass_SurfaceOptions");

            Section("Surface Inputs", "LoogaGlass_SurfaceInputs", true, () =>
            {
                materialEditor.TexturePropertySingleLine(new GUIContent("Dirt Map (RGB) Opacity (A)"), baseMap, baseColor);
                EditorGUILayout.Space(2);
                materialEditor.TexturePropertySingleLine(new GUIContent("Normal Map"), normalMap, normalScale);
                EditorGUILayout.Space(2);
                
                if (IsSpecularWorkflow(workflowMode))
                {
                    materialEditor.TexturePropertySingleLine(new GUIContent("Specular Map"), specGlossMap, specColor);
                    EditorGUI.indentLevel += 2;
                    materialEditor.ShaderProperty(smoothness, new GUIContent("Master Smoothness"));
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
                        materialEditor.ShaderProperty(smoothness, new GUIContent("Master Smoothness"));
                        EditorGUI.indentLevel -= 2;
                    }
                    else
                    {
                        materialEditor.TexturePropertySingleLine(new GUIContent("Metallic Map"), metallicMap, metallic);
                        EditorGUI.indentLevel += 2;
                        materialEditor.ShaderProperty(smoothness, new GUIContent("Master Smoothness"));
                        materialEditor.ShaderProperty(smoothnessSource, new GUIContent("Source"));
                        EditorGUI.indentLevel -= 2;
                        
                        materialEditor.TexturePropertySingleLine(new GUIContent("Occlusion Map"), occlusionMap, occlusionStrength);
                    }
                }

                EditorGUILayout.Space();
                materialEditor.TextureScaleOffsetProperty(baseMap);
            });

            Section("Optical Properties", "LoogaGlass_OpticalProperties", true, () =>
            {
                materialEditor.ShaderProperty(distortion, "Refraction Index (IOR)");
            });

            Section("Advanced Options", "LoogaGlass_AdvancedOptions", false, () =>
            {
                if (specHighlights != null) materialEditor.ShaderProperty(specHighlights, "Specular Highlights");
                if (envReflections != null) materialEditor.ShaderProperty(envReflections, "Environment Reflections");
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
