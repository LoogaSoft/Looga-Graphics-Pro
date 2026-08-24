using System;
using UnityEditor;
using UnityEngine;

namespace LoogaSoft.Lighting.Editor
{
    [Flags]
    internal enum MainLightShadowVariants
    {
        None = 0,
        Off = 1 << 0,
        Standard = 1 << 1,
        Cascades = 1 << 2,
        ScreenSpace = 1 << 3,
        All = Off | Standard | Cascades | ScreenSpace
    }

    [Flags]
    internal enum SoftShadowVariants
    {
        None = 0,
        Off = 1 << 0,
        Standard = 1 << 1,
        Low = 1 << 2,
        Medium = 1 << 3,
        High = 1 << 4,
        All = Off | Standard | Low | Medium | High
    }

    internal enum ShaderFeatureVariants
    {
        DisabledOnly = 0,
        EnabledOnly = 1,
        Both = 2
    }

    [FilePath("ProjectSettings/LoogaLightingVariants.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class LoogaLightingVariantProfile : ScriptableSingleton<LoogaLightingVariantProfile>
    {
        internal const int LightingModelVariantCount = 7;
        internal const int FullVariantsPerModel = 81920;
        internal const int FullMasterVariantCount = 573440;

        [SerializeField] private bool _useEditorCompileProfile = true;
        [SerializeField] private bool _autoDetectEditorCompileProfile = true;
        [SerializeField] private bool _stripMasterDeferredShader = true;
        [SerializeField] private bool _detectBeforeBuild = true;
        [SerializeField] private bool _validateBeforeBuild = true;
        [SerializeField] private bool _logBuildReport = true;

        [SerializeField] private MainLightShadowVariants _mainLightShadows = MainLightShadowVariants.All;
        [SerializeField] private SoftShadowVariants _softShadows = SoftShadowVariants.All;
        [SerializeField] private ShaderFeatureVariants _gBufferOctahedralNormals = ShaderFeatureVariants.Both;
        [SerializeField] private ShaderFeatureVariants _reflectionProbeBlending = ShaderFeatureVariants.Both;
        [SerializeField] private ShaderFeatureVariants _reflectionProbeBoxProjection = ShaderFeatureVariants.Both;
        [SerializeField] private ShaderFeatureVariants _reflectionProbeAtlas = ShaderFeatureVariants.Both;
        [SerializeField] private ShaderFeatureVariants _reflectionProbeRotation = ShaderFeatureVariants.Both;
        [SerializeField] private ShaderFeatureVariants _additionalLightShadows = ShaderFeatureVariants.Both;
        [SerializeField] private ShaderFeatureVariants _lightmapShadowMixing = ShaderFeatureVariants.Both;
        [SerializeField] private ShaderFeatureVariants _shadowMask = ShaderFeatureVariants.Both;
        [SerializeField] private ShaderFeatureVariants _deferredMixedLighting = ShaderFeatureVariants.Both;
        [SerializeField] private ShaderFeatureVariants _screenSpaceOcclusion = ShaderFeatureVariants.Both;
        [SerializeField] private ShaderFeatureVariants _lightCookies = ShaderFeatureVariants.Both;
        [SerializeField] private ShaderFeatureVariants _lightLayers = ShaderFeatureVariants.Both;

        internal bool UseEditorCompileProfile => _useEditorCompileProfile;
        internal bool AutoDetectEditorCompileProfile => _autoDetectEditorCompileProfile;
        internal bool StripMasterDeferredShader => _stripMasterDeferredShader;
        internal bool DetectBeforeBuild => _detectBeforeBuild;
        internal bool ValidateBeforeBuild => _validateBeforeBuild;
        internal bool LogBuildReport => _logBuildReport;
        internal MainLightShadowVariants MainLightShadows => _mainLightShadows;
        internal SoftShadowVariants SoftShadows => _softShadows;
        internal int EstimatedVariantsPerModel => CalculateVariantCount(1);
        internal int EstimatedMasterVariants => CalculateVariantCount(LightingModelVariantCount);

        internal int EstimateMasterVariants(int lightingModelCount)
        {
            if (!_useEditorCompileProfile)
            {
                return (int)Math.Min(
                    (long)Math.Max(0, lightingModelCount) * FullVariantsPerModel,
                    int.MaxValue);
            }

            return CalculateVariantCount(Math.Max(0, lightingModelCount));
        }

        internal void Apply(LoogaLightingVariantRequirements requirements)
        {
            _mainLightShadows = requirements.MainLightShadows;
            _softShadows = requirements.SoftShadows;
            _gBufferOctahedralNormals = requirements.GBufferOctahedralNormals;
            _reflectionProbeBlending = requirements.ReflectionProbeBlending;
            _reflectionProbeBoxProjection = requirements.ReflectionProbeBoxProjection;
            _reflectionProbeAtlas = requirements.ReflectionProbeAtlas;
            _reflectionProbeRotation = requirements.ReflectionProbeRotation;
            _additionalLightShadows = requirements.AdditionalLightShadows;
            _lightmapShadowMixing = requirements.LightmapShadowMixing;
            _shadowMask = requirements.ShadowMask;
            _deferredMixedLighting = requirements.DeferredMixedLighting;
            _screenSpaceOcclusion = requirements.ScreenSpaceOcclusion;
            _lightCookies = requirements.LightCookies;
            _lightLayers = requirements.LightLayers;
        }

        internal void ResetToCompatibilityDefaults()
        {
            _mainLightShadows = MainLightShadowVariants.All;
            _softShadows = SoftShadowVariants.All;
            _gBufferOctahedralNormals = ShaderFeatureVariants.Both;
            _reflectionProbeBlending = ShaderFeatureVariants.Both;
            _reflectionProbeBoxProjection = ShaderFeatureVariants.Both;
            _reflectionProbeAtlas = ShaderFeatureVariants.Both;
            _reflectionProbeRotation = ShaderFeatureVariants.Both;
            _additionalLightShadows = ShaderFeatureVariants.Both;
            _lightmapShadowMixing = ShaderFeatureVariants.Both;
            _shadowMask = ShaderFeatureVariants.Both;
            _deferredMixedLighting = ShaderFeatureVariants.Both;
            _screenSpaceOcclusion = ShaderFeatureVariants.Both;
            _lightCookies = ShaderFeatureVariants.Both;
            _lightLayers = ShaderFeatureVariants.Both;
        }

        internal void SaveSettings()
        {
            Save(true);
        }

        internal bool Allows(MainLightShadowVariants variant)
        {
            return (_mainLightShadows & variant) != 0;
        }

        internal bool Allows(SoftShadowVariants variant)
        {
            return (_softShadows & variant) != 0;
        }

        internal bool Allows(string keyword, bool enabled)
        {
            ShaderFeatureVariants mode = keyword switch
            {
                "_GBUFFER_NORMALS_OCT" => _gBufferOctahedralNormals,
                "_REFLECTION_PROBE_BLENDING" => _reflectionProbeBlending,
                "_REFLECTION_PROBE_BOX_PROJECTION" => _reflectionProbeBoxProjection,
                "_REFLECTION_PROBE_ATLAS" => _reflectionProbeAtlas,
                "REFLECTION_PROBE_ROTATION" => _reflectionProbeRotation,
                "_ADDITIONAL_LIGHT_SHADOWS" => _additionalLightShadows,
                "LIGHTMAP_SHADOW_MIXING" => _lightmapShadowMixing,
                "SHADOWS_SHADOWMASK" => _shadowMask,
                "_DEFERRED_MIXED_LIGHTING" => _deferredMixedLighting,
                "_SCREEN_SPACE_OCCLUSION" => _screenSpaceOcclusion,
                "_LIGHT_COOKIES" => _lightCookies,
                "_LIGHT_LAYERS" => _lightLayers,
                _ => ShaderFeatureVariants.Both
            };

            return mode == ShaderFeatureVariants.Both ||
                   enabled && mode == ShaderFeatureVariants.EnabledOnly ||
                   !enabled && mode == ShaderFeatureVariants.DisabledOnly;
        }

        internal bool Contains(LoogaLightingVariantRequirements requirements, out string missingRequirement)
        {
            if ((_mainLightShadows & requirements.MainLightShadows) != requirements.MainLightShadows)
            {
                missingRequirement = "main-light shadow modes";
                return false;
            }

            if ((_softShadows & requirements.SoftShadows) != requirements.SoftShadows)
            {
                missingRequirement = "soft-shadow quality modes";
                return false;
            }

            foreach ((string name, ShaderFeatureVariants required) in requirements.EnumerateFeatures())
            {
                if (!ContainsFeature(name, required))
                {
                    missingRequirement = name;
                    return false;
                }
            }

            missingRequirement = string.Empty;
            return true;
        }

        private bool ContainsFeature(string keyword, ShaderFeatureVariants required)
        {
            bool needsDisabled = required is ShaderFeatureVariants.DisabledOnly or ShaderFeatureVariants.Both;
            bool needsEnabled = required is ShaderFeatureVariants.EnabledOnly or ShaderFeatureVariants.Both;
            return (!needsDisabled || Allows(keyword, false)) && (!needsEnabled || Allows(keyword, true));
        }

        private int CalculateVariantCount(int lightingModelCount)
        {
            long count = lightingModelCount *
                         CountBits((int)_mainLightShadows) *
                         CountBits((int)_softShadows);
            count *= FeatureCount(_gBufferOctahedralNormals);
            count *= FeatureCount(_reflectionProbeBlending);
            count *= FeatureCount(_reflectionProbeBoxProjection);
            count *= FeatureCount(_reflectionProbeAtlas);
            count *= FeatureCount(_reflectionProbeRotation);
            count *= FeatureCount(_additionalLightShadows);
            count *= FeatureCount(_lightmapShadowMixing);
            count *= FeatureCount(_shadowMask);
            count *= FeatureCount(_deferredMixedLighting);
            count *= FeatureCount(_screenSpaceOcclusion);
            count *= FeatureCount(_lightCookies);
            count *= FeatureCount(_lightLayers);
            return (int)Math.Min(count, int.MaxValue);
        }

        private static int FeatureCount(ShaderFeatureVariants mode)
        {
            return mode == ShaderFeatureVariants.Both ? 2 : 1;
        }

        private static int CountBits(int value)
        {
            int count = 0;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }

            return Math.Max(1, count);
        }
    }
}
