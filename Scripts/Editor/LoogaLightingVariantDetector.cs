using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LoogaSoft.Lighting.Editor
{
    internal sealed class LoogaLightingVariantRequirements
    {
        internal MainLightShadowVariants MainLightShadows { get; set; } = MainLightShadowVariants.Off;
        internal SoftShadowVariants SoftShadows { get; set; } = SoftShadowVariants.Off;
        internal ShaderFeatureVariants GBufferOctahedralNormals { get; set; } = ShaderFeatureVariants.Both;
        internal ShaderFeatureVariants ReflectionProbeBlending { get; set; } = ShaderFeatureVariants.Both;
        internal ShaderFeatureVariants ReflectionProbeBoxProjection { get; set; } = ShaderFeatureVariants.Both;
        internal ShaderFeatureVariants ReflectionProbeAtlas { get; set; } = ShaderFeatureVariants.Both;
        internal ShaderFeatureVariants ReflectionProbeRotation { get; set; } = ShaderFeatureVariants.DisabledOnly;
        internal ShaderFeatureVariants AdditionalLightShadows { get; set; } = ShaderFeatureVariants.Both;
        internal ShaderFeatureVariants LightmapShadowMixing { get; set; } = ShaderFeatureVariants.Both;
        internal ShaderFeatureVariants ShadowMask { get; set; } = ShaderFeatureVariants.Both;
        internal ShaderFeatureVariants DeferredMixedLighting { get; set; } = ShaderFeatureVariants.Both;
        internal ShaderFeatureVariants ScreenSpaceOcclusion { get; set; } = ShaderFeatureVariants.Both;
        internal ShaderFeatureVariants LightCookies { get; set; } = ShaderFeatureVariants.Both;
        internal ShaderFeatureVariants LightLayers { get; set; } = ShaderFeatureVariants.Both;

        internal IEnumerable<(string Name, ShaderFeatureVariants Mode)> EnumerateFeatures()
        {
            yield return ("_GBUFFER_NORMALS_OCT", GBufferOctahedralNormals);
            yield return ("_REFLECTION_PROBE_BLENDING", ReflectionProbeBlending);
            yield return ("_REFLECTION_PROBE_BOX_PROJECTION", ReflectionProbeBoxProjection);
            yield return ("_REFLECTION_PROBE_ATLAS", ReflectionProbeAtlas);
            yield return ("REFLECTION_PROBE_ROTATION", ReflectionProbeRotation);
            yield return ("_ADDITIONAL_LIGHT_SHADOWS", AdditionalLightShadows);
            yield return ("LIGHTMAP_SHADOW_MIXING", LightmapShadowMixing);
            yield return ("SHADOWS_SHADOWMASK", ShadowMask);
            yield return ("_DEFERRED_MIXED_LIGHTING", DeferredMixedLighting);
            yield return ("_SCREEN_SPACE_OCCLUSION", ScreenSpaceOcclusion);
            yield return ("_LIGHT_COOKIES", LightCookies);
            yield return ("_LIGHT_LAYERS", LightLayers);
        }
    }

    internal readonly struct LoogaLightingVariantDetectionResult
    {
        internal LoogaLightingVariantDetectionResult(
            LoogaLightingVariantRequirements requirements,
            int pipelineAssetCount,
            int rendererCount,
            string summary)
        {
            Requirements = requirements;
            PipelineAssetCount = pipelineAssetCount;
            RendererCount = rendererCount;
            Summary = summary;
        }

        internal LoogaLightingVariantRequirements Requirements { get; }
        internal int PipelineAssetCount { get; }
        internal int RendererCount { get; }
        internal string Summary { get; }
        internal bool IsValid => PipelineAssetCount > 0 && RendererCount > 0;
    }

    internal static class LoogaLightingVariantDetector
    {
        private const string FeatureTypeName = "LoogaLightingFeature";
        private const string ScreenSpaceShadowsTypeName = "ScreenSpaceShadows";
        private const string LoogaShadowsFeatureTypeName =
            "LoogaSoft.Shadows.LoogaShadowRendererFeature";
        private const string ScreenSpaceOcclusionTypeName = "ScreenSpaceAmbientOcclusion";

        internal static LoogaLightingVariantDetectionResult Detect()
        {
            List<UniversalRenderPipelineAsset> pipelineAssets = FindActivePipelineAssets();
            List<UniversalRendererData> renderers = FindLoogaRenderers(pipelineAssets);
            LoogaLightingVariantRequirements requirements = BuildRequirements(pipelineAssets, renderers);

            string summary = pipelineAssets.Count == 0
                ? "No active Universal Render Pipeline assets were found. Compatibility settings were retained."
                : renderers.Count == 0
                    ? $"Found {pipelineAssets.Count} active URP asset(s), but none reference an active Looga Lighting renderer feature."
                    : $"Detected {pipelineAssets.Count} active URP asset(s) and {renderers.Count} Looga Deferred+ renderer(s).";

            return new LoogaLightingVariantDetectionResult(
                requirements,
                pipelineAssets.Count,
                renderers.Count,
                summary);
        }

        private static LoogaLightingVariantRequirements BuildRequirements(
            IReadOnlyList<UniversalRenderPipelineAsset> pipelineAssets,
            IReadOnlyList<UniversalRendererData> renderers)
        {
            LoogaLightingVariantRequirements requirements = new LoogaLightingVariantRequirements();
            if (pipelineAssets.Count == 0)
                return requirements;

            List<bool> reflectionBlending = new List<bool>(pipelineAssets.Count);
            List<bool> reflectionBoxProjection = new List<bool>(pipelineAssets.Count);
            List<bool> reflectionAtlas = new List<bool>(pipelineAssets.Count);
            List<bool> lightLayers = new List<bool>(pipelineAssets.Count);
            bool supportsAdditionalLightShadows = false;
            bool supportsMixedLighting = false;
            bool supportsLightCookies = false;
            bool supportsSoftShadows = false;

            for (int i = 0; i < pipelineAssets.Count; i++)
            {
                SerializedObject asset = new SerializedObject(pipelineAssets[i]);
                bool supportsMainShadows = ReadBool(asset, "m_MainLightShadowsSupported");
                int cascadeCount = ReadInt(asset, "m_ShadowCascadeCount", 1);
                if (supportsMainShadows)
                {
                    requirements.MainLightShadows |= cascadeCount > 1
                        ? MainLightShadowVariants.Cascades
                        : MainLightShadowVariants.Standard;
                }

                supportsSoftShadows |= ReadBool(asset, "m_SoftShadowsSupported");
                supportsAdditionalLightShadows |= ReadBool(asset, "m_AdditionalLightShadowsSupported");
                supportsMixedLighting |= ReadBool(asset, "m_MixedLightingSupported");
                supportsLightCookies |= ReadBool(asset, "m_SupportsLightCookies");
                reflectionBlending.Add(ReadBool(asset, "m_ReflectionProbeBlending"));
                reflectionBoxProjection.Add(ReadBool(asset, "m_ReflectionProbeBoxProjection"));
                reflectionAtlas.Add(ReadBool(asset, "m_ReflectionProbeAtlas"));
                lightLayers.Add(ReadBool(asset, "m_SupportsLightLayers"));
            }

            requirements.ReflectionProbeBlending = ResolveFixedFeature(reflectionBlending);
            requirements.ReflectionProbeBoxProjection = ResolveFixedFeature(reflectionBoxProjection);
            requirements.ReflectionProbeAtlas = ResolveFixedFeature(reflectionAtlas);
            requirements.LightLayers = ResolveFixedFeature(lightLayers);
            // URP's generic soft-shadow path selects hard/low/medium/high filtering from
            // runtime shadow data. Additional-light shadows and cookies likewise return
            // neutral results when the current light has no shadow or cookie.
            requirements.SoftShadows = supportsSoftShadows
                ? SoftShadowVariants.Standard
                : SoftShadowVariants.Off;
            requirements.AdditionalLightShadows = ResolveRuntimeCapableFeature(
                supportsAdditionalLightShadows);
            requirements.LightCookies = ResolveRuntimeCapableFeature(supportsLightCookies);
            requirements.LightmapShadowMixing = ResolveRuntimeFeature(supportsMixedLighting);
            requirements.ShadowMask = ResolveRuntimeFeature(supportsMixedLighting);
            requirements.DeferredMixedLighting = ResolveRuntimeFeature(supportsMixedLighting);

            if (renderers.Count == 0)
                return requirements;

            List<bool> octahedralNormals = new List<bool>(renderers.Count);
            bool hasScreenSpaceShadows = false;
            bool hasScreenSpaceOcclusion = false;

            for (int i = 0; i < renderers.Count; i++)
            {
                SerializedObject renderer = new SerializedObject(renderers[i]);
                octahedralNormals.Add(ReadBool(renderer, "m_AccurateGbufferNormals"));
                hasScreenSpaceShadows |=
                    HasActiveRendererFeature(renderer, ScreenSpaceShadowsTypeName) ||
                    HasActiveRendererFeature(renderer, LoogaShadowsFeatureTypeName);
                hasScreenSpaceOcclusion |= HasActiveRendererFeature(renderer, ScreenSpaceOcclusionTypeName);
            }

            if (hasScreenSpaceShadows)
                requirements.MainLightShadows |= MainLightShadowVariants.ScreenSpace;

            requirements.GBufferOctahedralNormals = ResolveFixedFeature(octahedralNormals);
            requirements.ScreenSpaceOcclusion = ResolveRuntimeFeature(hasScreenSpaceOcclusion);
            return requirements;
        }

        internal static List<UniversalRenderPipelineAsset> FindActivePipelineAssets()
        {
            HashSet<UniversalRenderPipelineAsset> unique = new HashSet<UniversalRenderPipelineAsset>();
            AddPipelineAsset(unique, GraphicsSettings.defaultRenderPipeline);
            AddPipelineAsset(unique, GraphicsSettings.currentRenderPipeline);

            MethodInfo getQualityAsset = typeof(QualitySettings).GetMethod(
                "GetRenderPipelineAssetAt",
                BindingFlags.Public | BindingFlags.Static);

            if (getQualityAsset != null)
            {
                string[] qualityNames = QualitySettings.names;
                for (int i = 0; i < qualityNames.Length; i++)
                {
                    RenderPipelineAsset asset = getQualityAsset.Invoke(null, new object[] { i }) as RenderPipelineAsset;
                    AddPipelineAsset(unique, asset);
                }
            }

            return new List<UniversalRenderPipelineAsset>(unique);
        }

        internal static List<UniversalRendererData> FindLoogaRenderers(
            IReadOnlyList<UniversalRenderPipelineAsset> pipelineAssets)
        {
            HashSet<UniversalRendererData> unique = new HashSet<UniversalRendererData>();
            for (int i = 0; i < pipelineAssets.Count; i++)
            {
                SerializedObject asset = new SerializedObject(pipelineAssets[i]);
                SerializedProperty rendererList = asset.FindProperty("m_RendererDataList");
                if (rendererList == null || !rendererList.isArray)
                    continue;

                for (int rendererIndex = 0; rendererIndex < rendererList.arraySize; rendererIndex++)
                {
                    UniversalRendererData renderer = rendererList.GetArrayElementAtIndex(rendererIndex)
                        .objectReferenceValue as UniversalRendererData;

                    if (renderer == null || !ContainsActiveLoogaFeature(renderer))
                        continue;

                    unique.Add(renderer);
                }
            }

            return new List<UniversalRendererData>(unique);
        }

        private static bool ContainsActiveLoogaFeature(UniversalRendererData renderer)
        {
            SerializedObject serializedRenderer = new SerializedObject(renderer);
            SerializedProperty features = serializedRenderer.FindProperty("m_RendererFeatures");
            if (features == null || !features.isArray)
                return false;

            for (int i = 0; i < features.arraySize; i++)
            {
                ScriptableRendererFeature feature = features.GetArrayElementAtIndex(i)
                    .objectReferenceValue as ScriptableRendererFeature;

                if (feature != null && feature.isActive && feature.GetType().Name == FeatureTypeName)
                    return true;
            }

            return false;
        }

        private static bool HasActiveRendererFeature(SerializedObject renderer, string typeName)
        {
            SerializedProperty features = renderer.FindProperty("m_RendererFeatures");
            if (features == null || !features.isArray)
                return false;

            for (int i = 0; i < features.arraySize; i++)
            {
                ScriptableRendererFeature feature = features.GetArrayElementAtIndex(i)
                    .objectReferenceValue as ScriptableRendererFeature;

                if (feature != null &&
                    feature.isActive &&
                    (feature.GetType().Name == typeName || feature.GetType().FullName == typeName))
                    return true;
            }

            return false;
        }

        private static ShaderFeatureVariants ResolveFixedFeature(IReadOnlyList<bool> states)
        {
            if (states.Count == 0)
                return ShaderFeatureVariants.Both;

            bool anyEnabled = false;
            bool anyDisabled = false;
            for (int i = 0; i < states.Count; i++)
            {
                anyEnabled |= states[i];
                anyDisabled |= !states[i];
            }

            if (anyEnabled && anyDisabled)
                return ShaderFeatureVariants.Both;

            return anyEnabled ? ShaderFeatureVariants.EnabledOnly : ShaderFeatureVariants.DisabledOnly;
        }

        private static ShaderFeatureVariants ResolveRuntimeFeature(bool supported)
        {
            return supported ? ShaderFeatureVariants.Both : ShaderFeatureVariants.DisabledOnly;
        }

        private static ShaderFeatureVariants ResolveRuntimeCapableFeature(bool supported)
        {
            return supported ? ShaderFeatureVariants.EnabledOnly : ShaderFeatureVariants.DisabledOnly;
        }

        private static void AddPipelineAsset(HashSet<UniversalRenderPipelineAsset> assets, RenderPipelineAsset asset)
        {
            if (asset is UniversalRenderPipelineAsset universalAsset)
                assets.Add(universalAsset);
        }

        private static bool ReadBool(SerializedObject target, string name)
        {
            SerializedProperty property = target.FindProperty(name);
            return property != null && property.boolValue;
        }

        private static int ReadInt(SerializedObject target, string name, int fallback)
        {
            SerializedProperty property = target.FindProperty(name);
            return property != null ? property.intValue : fallback;
        }
    }
}
