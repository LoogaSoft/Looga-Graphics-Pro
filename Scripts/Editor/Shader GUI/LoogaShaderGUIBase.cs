using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LoogaSoft.Lighting.Editor
{
    public abstract class LoogaShaderGUIBase : ShaderGUI
    {
        protected static GUIStyle _header, _box;
        private static readonly GUIContent[] RenderFaceLabels =
        {
            new GUIContent("Front"),
            new GUIContent("Back"),
            new GUIContent("Both")
        };
        private static readonly float[] RenderFaceValues = { 2.0f, 1.0f, 0.0f };

        protected static void Styles()
        {
            if (_header != null) return;
            _header = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, padding = new RectOffset(0, 0, 0, 4) };
            _box = new GUIStyle("HelpBox") { padding = new RectOffset(8, 8, 6, 6) };
        }

        protected void DrawLoogaSoftHeader()
        {
            GUIStyle titleStyle = new GUIStyle()
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f) }
            };

            EditorGUILayout.Space(3);
            GUILayout.Label("-  LoogaSoft  -", titleStyle);
            EditorGUILayout.Space(3);
        }

        protected void Section(string title, string prefKey, bool defaultShow, System.Action content)
        {
            bool show = EditorPrefs.GetBool(prefKey, defaultShow);

            EditorGUILayout.BeginVertical(_box);
            Rect full = GUILayoutUtility.GetRect(GUIContent.none, _header);
            full.height += 4f; full.y -= 2f; full.width += 8f; full.x -= 4f;
            Rect text  = new Rect(full.x + 4, full.y + 1, full.width - 24, full.height);
            Rect arrow = new Rect(full.xMax - 10, full.y, 15, full.height);

            if (full.Contains(Event.current.mousePosition)) EditorGUI.DrawRect(full, new Color(1, 1, 1, 0.05f));
            GUI.Label(text, title, _header);

            bool newShow = EditorGUI.Foldout(arrow, show, GUIContent.none);
            if (Event.current.type == EventType.MouseDown && full.Contains(Event.current.mousePosition) && Event.current.button == 0)
            {
                newShow = !show;
                Event.current.Use();
            }

            if (newShow != show)
            {
                EditorPrefs.SetBool(prefKey, newShow);
                show = newShow;
            }

            if (show)
            {
                EditorGUILayout.Space(2);
                content();
                EditorGUILayout.Space(2);
            }
            EditorGUILayout.EndVertical();
        }

        protected void DrawEmissionToggle(MaterialEditor materialEditor, MaterialProperty emissionMap, MaterialProperty emissionColor, string keyword, string mapLabel)
        {
            bool enabled = ShouldEnableEmissionFromExistingMaterial(materialEditor, keyword);
            EditorGUI.BeginChangeCheck();
            enabled = EditorGUILayout.Toggle("Emission", enabled);
            if (EditorGUI.EndChangeCheck())
            {
                SetKeyword(materialEditor, keyword, enabled);
            }

            if (enabled)
            {
                EditorGUI.indentLevel += 1;
                materialEditor.TexturePropertySingleLine(new GUIContent(mapLabel), emissionMap, emissionColor);
                EditorGUI.indentLevel -= 1;
            }
        }

        protected void DrawSurfaceOptionsSection(MaterialEditor materialEditor, MaterialProperty[] properties, string prefKey)
        {
            MaterialProperty workflowMode = FindProperty("_WorkflowMode", properties, false);
            MaterialProperty surface = FindProperty("_Surface", properties, false);
            MaterialProperty cull = FindProperty("_Cull", properties, false);
            MaterialProperty alphaClip = FindProperty("_AlphaClip", properties, false);
            MaterialProperty cutoff = FindProperty("_Cutoff", properties, false);
            MaterialProperty receiveShadows = FindProperty("_ReceiveShadows", properties, false);
            MaterialProperty backfaceNormalMode = FindProperty("_BackfaceNormalMode", properties, false);

            Section("Surface Options", prefKey, true, () =>
            {
                if (workflowMode != null) materialEditor.ShaderProperty(workflowMode, "Workflow Mode");
                if (surface != null) materialEditor.ShaderProperty(surface, "Surface Type");
                if (cull != null) DrawRenderFaceProperty(cull);

                if (cull != null && backfaceNormalMode != null && !cull.hasMixedValue && Mathf.Approximately(cull.floatValue, 0.0f))
                {
                    EditorGUI.indentLevel += 1;
                    materialEditor.ShaderProperty(backfaceNormalMode, "Backface Normals");
                    EditorGUI.indentLevel -= 1;
                }

                if (alphaClip != null) materialEditor.ShaderProperty(alphaClip, "Alpha Clipping");
                if (alphaClip != null && cutoff != null && (alphaClip.hasMixedValue || alphaClip.floatValue > 0.5f))
                {
                    EditorGUI.indentLevel += 1;
                    materialEditor.ShaderProperty(cutoff, "Threshold");
                    EditorGUI.indentLevel -= 1;
                }

                if (receiveShadows != null) materialEditor.ShaderProperty(receiveShadows, "Receive Shadows");
            });

            ApplyMaterialSurfaceState(materialEditor);
        }

        protected void DrawLightingModelInputsSection(MaterialEditor materialEditor, MaterialProperty[] properties, string prefKey)
        {
            MaterialProperty orenNayarSigma = FindProperty("_OrenNayarSigma", properties, false);
            MaterialProperty minnaertK = FindProperty("_MinnaertK", properties, false);
            MaterialProperty overwatchWrap = FindProperty("_OverwatchWrap", properties, false);
            MaterialProperty arkaneBandCount = FindProperty("_ArkaneBandCount", properties, false);
            MaterialProperty arkaneBandFeather = FindProperty("_ArkaneBandFeather", properties, false);
            MaterialProperty minnaertIndirectSpecularModel = FindProperty("_MinnaertIndirectSpecularModel", properties, false);
            MaterialProperty orenNayarIndirectSpecularModel = FindProperty("_OrenNayarIndirectSpecularModel", properties, false);

            if (orenNayarSigma == null)
                return;

            Section("Lighting Model Inputs", prefKey, false, () =>
            {
                GUILayout.Label("Rough Diffuse", EditorStyles.boldLabel);
                materialEditor.ShaderProperty(orenNayarSigma, "Oren-Nayar Sigma");
                materialEditor.ShaderProperty(orenNayarIndirectSpecularModel, "Oren-Nayar Indirect");
                materialEditor.ShaderProperty(minnaertK, "Minnaert k");
                materialEditor.ShaderProperty(minnaertIndirectSpecularModel, "Minnaert Indirect");

                EditorGUILayout.Space(5);
                GUILayout.Label("Stylized", EditorStyles.boldLabel);
                materialEditor.ShaderProperty(overwatchWrap, "Overwatch Wrap");
                materialEditor.ShaderProperty(arkaneBandCount, "Arkane Band Count");
                materialEditor.ShaderProperty(arkaneBandFeather, "Arkane Band Feather");
            });
        }

        protected void DrawBacklightingSection(MaterialEditor materialEditor, MaterialProperty[] properties, string prefKey)
        {
            MaterialProperty enabled = FindProperty("_UseBacklighting", properties, false);
            if (enabled == null)
                return;

            MaterialProperty color = FindProperty("_SubsurfaceColor", properties, false);
            MaterialProperty width = FindProperty("_ScatterWidth", properties, false);
            MaterialProperty ambient = FindProperty("_AmbientScatterStrength", properties, false);
            MaterialProperty thickness = FindProperty("_ThicknessMap", properties, false);
            MaterialProperty strength = FindProperty("_TransmissionStrength", properties, false);
            MaterialProperty shadowSoftness = FindProperty("_TransmissionShadowSoftness", properties, false);
            MaterialProperty rimPower = FindProperty("_BacklightRimPower", properties, false);
            MaterialProperty distortion = FindProperty("_BacklightDistortion", properties, false);

            Section("Backlighting", prefKey, true, () =>
            {
                materialEditor.ShaderProperty(enabled, "Enable Backlighting");
                if (!enabled.hasMixedValue && enabled.floatValue < 0.5f)
                    return;

                EditorGUI.indentLevel++;
                if (color != null) materialEditor.ShaderProperty(color, "Scattering Color");
                if (thickness != null) materialEditor.TexturePropertySingleLine(new GUIContent("Thickness Map (Black=Thin)"), thickness);
                if (strength != null) materialEditor.ShaderProperty(strength, "Strength");
                if (width != null) materialEditor.ShaderProperty(width, "Scatter Width");
                if (ambient != null) materialEditor.ShaderProperty(ambient, "Backlight Wrap");
                if (rimPower != null) materialEditor.ShaderProperty(rimPower, "Rim Tightness");
                if (distortion != null) materialEditor.ShaderProperty(distortion, "Light Distortion");
                if (shadowSoftness != null) materialEditor.ShaderProperty(shadowSoftness, "Shadow Softness");
                EditorGUI.indentLevel--;
            });
        }

        private static void DrawRenderFaceProperty(MaterialProperty cull)
        {
            EditorGUI.showMixedValue = cull.hasMixedValue;
            int selected = 0;
            if (!cull.hasMixedValue)
            {
                selected = Mathf.Approximately(cull.floatValue, 1.0f) ? 1 : Mathf.Approximately(cull.floatValue, 0.0f) ? 2 : 0;
            }

            Rect rect = EditorGUILayout.GetControlRect();
            EditorGUI.BeginChangeCheck();
            selected = EditorGUI.Popup(rect, new GUIContent("Render Face"), selected, RenderFaceLabels);
            if (EditorGUI.EndChangeCheck())
            {
                cull.floatValue = RenderFaceValues[selected];
            }
            EditorGUI.showMixedValue = false;
        }

        private static bool ShouldEnableEmissionFromExistingMaterial(MaterialEditor materialEditor, string keyword)
        {
            foreach (Object target in materialEditor.targets)
            {
                if (target is not Material material)
                    continue;

                if (material.IsKeywordEnabled(keyword))
                    return true;

            }

            return false;
        }

        private static void SetKeyword(MaterialEditor materialEditor, string keyword, bool enabled)
        {
            foreach (Object target in materialEditor.targets)
            {
                if (target is not Material material)
                    continue;

                if (enabled)
                    material.EnableKeyword(keyword);
                else
                    material.DisableKeyword(keyword);
            }
        }

        protected void DrawSpecularWorkflowInputs(MaterialEditor materialEditor, MaterialProperty workflowMode, MaterialProperty specGlossMap, MaterialProperty specColor, MaterialProperty smoothness)
        {
            if (workflowMode != null && !workflowMode.hasMixedValue && workflowMode.floatValue < 0.5f)
            {
                materialEditor.TexturePropertySingleLine(new GUIContent("Specular Map"), specGlossMap, specColor);
                EditorGUI.indentLevel += 2;
                materialEditor.ShaderProperty(smoothness, new GUIContent("Master Smoothness"));
                EditorGUI.indentLevel -= 2;
            }
        }

        protected bool IsSpecularWorkflow(MaterialProperty workflowMode)
        {
            return workflowMode != null && !workflowMode.hasMixedValue && workflowMode.floatValue < 0.5f;
        }

        protected void DrawPackedMaskMapHint(
            MaterialEditor materialEditor,
            MaterialProperty useMaskMap,
            MaterialProperty maskMap,
            MaterialProperty metallicMap,
            MaterialProperty occlusionMap)
        {
            if (useMaskMap == null || maskMap == null || metallicMap == null ||
                useMaskMap.hasMixedValue || useMaskMap.floatValue > 0.5f ||
                maskMap.textureValue != null || metallicMap.textureValue == null ||
                (occlusionMap != null && occlusionMap.textureValue != null) ||
                !LooksLikePackedOcclusionMap(metallicMap.textureValue.name))
            {
                return;
            }

            EditorGUILayout.HelpBox(
                "This metallic texture appears to contain occlusion. Looga micro shadows need its green channel through the Mask Map workflow.",
                MessageType.Warning);

            if (!GUILayout.Button("Use Metallic Texture As Mask Map"))
                return;

            foreach (Object target in materialEditor.targets)
            {
                if (target is not Material material)
                    continue;

                Undo.RecordObject(material, "Use Looga Mask Map");
                material.SetTexture("_MaskMap", material.GetTexture("_MetallicGlossMap"));
                material.SetFloat("_UseMaskMap", 1.0f);
                material.EnableKeyword("_USE_MASK_MAP");
                EditorUtility.SetDirty(material);
            }

            maskMap.textureValue = metallicMap.textureValue;
            useMaskMap.floatValue = 1.0f;
        }

        private static bool LooksLikePackedOcclusionMap(string textureName)
        {
            if (string.IsNullOrEmpty(textureName))
                return false;

            string normalized = textureName.ToLowerInvariant();
            return normalized.Contains("occlusion") ||
                   normalized.Contains("maskmap") ||
                   normalized.Contains("mask_map") ||
                   normalized.EndsWith("_mos") ||
                   normalized.EndsWith("_orm") ||
                   normalized.EndsWith("_rma");
        }

        private static void ApplyMaterialSurfaceState(MaterialEditor materialEditor)
        {
            foreach (Object target in materialEditor.targets)
            {
                if (target is Material material)
                    ApplyMaterialSurfaceState(material);
            }
        }

        private static void ApplyMaterialSurfaceState(Material material)
        {
            bool alphaClip = material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > 0.5f;
            bool transparent = material.HasProperty("_Surface") && material.GetFloat("_Surface") > 0.5f;

            SetMaterialKeyword(material, "_ALPHATEST_ON", alphaClip);
            SetMaterialKeyword(material, "_SURFACE_TYPE_TRANSPARENT", transparent);

            if (transparent)
            {
                material.SetOverrideTag("RenderType", "Transparent");
                SetFloatIfPresent(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                SetFloatIfPresent(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                SetFloatIfPresent(material, "_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
                SetFloatIfPresent(material, "_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                SetFloatIfPresent(material, "_ZWrite", 0.0f);
                SetFloatIfPresent(material, "_AlphaToMask", 0.0f);
                material.renderQueue = (int)RenderQueue.Transparent + GetQueueOffset(material);
                material.SetShaderPassEnabled("DepthOnly", false);
            }
            else
            {
                material.SetOverrideTag("RenderType", alphaClip ? "TransparentCutout" : "Opaque");
                SetFloatIfPresent(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                SetFloatIfPresent(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                SetFloatIfPresent(material, "_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
                SetFloatIfPresent(material, "_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.Zero);
                SetFloatIfPresent(material, "_ZWrite", 1.0f);
                SetFloatIfPresent(material, "_AlphaToMask", alphaClip ? 1.0f : 0.0f);
                material.renderQueue = (alphaClip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry) + GetQueueOffset(material);
                material.SetShaderPassEnabled("DepthOnly", true);
            }

            bool receiveShadowsOff = material.HasProperty("_ReceiveShadows") && material.GetFloat("_ReceiveShadows") < 0.5f;
            SetMaterialKeyword(material, "_RECEIVE_SHADOWS_OFF", receiveShadowsOff);

            bool specularHighlightsOff = material.HasProperty("_SpecularHighlights") && material.GetFloat("_SpecularHighlights") < 0.5f;
            bool environmentReflectionsOff = material.HasProperty("_EnvironmentReflections") && material.GetFloat("_EnvironmentReflections") < 0.5f;
            SetMaterialKeyword(material, "_SPECULARHIGHLIGHTS_OFF", specularHighlightsOff);
            SetMaterialKeyword(material, "_ENVIRONMENTREFLECTIONS_OFF", environmentReflectionsOff);

            bool specularWorkflow = material.HasProperty("_WorkflowMode") && material.GetFloat("_WorkflowMode") < 0.5f;
            SetMaterialKeyword(material, "_SPECULAR_SETUP", specularWorkflow);

            // Mask and specular workflows share one mutually exclusive variant group.
            bool useMaskMap = !specularWorkflow &&
                              material.HasProperty("_UseMaskMap") &&
                              material.GetFloat("_UseMaskMap") > 0.5f;
            SetMaterialKeyword(material, "_USE_MASK_MAP", useMaskMap);
        }

        private static int GetQueueOffset(Material material)
        {
            return material.HasProperty("_QueueOffset") ? (int)material.GetFloat("_QueueOffset") : 0;
        }

        private static void SetFloatIfPresent(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
                material.SetFloat(propertyName, value);
        }

        private static void SetMaterialKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
        }

        protected void DrawMinMaxSlider(MaterialProperty prop, string label, float minLimit, float maxLimit)
        {
            Vector4 vec = prop.vectorValue;
            float minVal = vec.x;
            float maxVal = vec.y;

            Rect rect = EditorGUILayout.GetControlRect();
            Rect labelRect = new Rect(rect.x, rect.y, EditorGUIUtility.labelWidth, rect.height);

            float fieldWidth = 45f;
            float spacing = 4f;

            Rect minFieldRect = new Rect(labelRect.xMax, rect.y, fieldWidth, rect.height);
            float sliderWidth = rect.width - EditorGUIUtility.labelWidth - (fieldWidth * 2) - (spacing * 2);
            Rect sliderRect = new Rect(minFieldRect.xMax + spacing, rect.y, sliderWidth, rect.height);
            Rect maxFieldRect = new Rect(sliderRect.xMax + spacing, rect.y, fieldWidth, rect.height);

            EditorGUI.LabelField(labelRect, new GUIContent(label));

            EditorGUI.BeginChangeCheck();

            minVal = EditorGUI.FloatField(minFieldRect, (float)System.Math.Round(minVal, 3));
            EditorGUI.MinMaxSlider(sliderRect, ref minVal, ref maxVal, minLimit, maxLimit);
            maxVal = EditorGUI.FloatField(maxFieldRect, (float)System.Math.Round(maxVal, 3));

            if (EditorGUI.EndChangeCheck())
            {
                minVal = Mathf.Clamp(minVal, minLimit, maxVal);
                maxVal = Mathf.Clamp(maxVal, minVal, maxLimit);
                vec.x = minVal;
                vec.y = maxVal;
                prop.vectorValue = vec;
            }
        }
    }
}
