using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace LoogaSoft.Lighting.Editor
{
    internal sealed class LoogaLightingShaderVariantStripper : IPreprocessShaders
    {
        private const string MasterShaderName = "Hidden/LoogaSoft/Lighting/MasterDeferred";
        private const string GeneratedMasterShaderPrefix =
            "Hidden/LoogaSoft/Lighting/MasterDeferredProject/";
        private const string MasterPassName = "Looga Master Deferred Lighting";

        private static readonly string[] BinaryKeywords =
        {
            "_GBUFFER_NORMALS_OCT",
            "_REFLECTION_PROBE_BLENDING",
            "_REFLECTION_PROBE_BOX_PROJECTION",
            "_REFLECTION_PROBE_ATLAS",
            "REFLECTION_PROBE_ROTATION",
            "_ADDITIONAL_LIGHT_SHADOWS",
            "LIGHTMAP_SHADOW_MIXING",
            "SHADOWS_SHADOWMASK",
            "_DEFERRED_MIXED_LIGHTING",
            "_SCREEN_SPACE_OCCLUSION",
            "_LIGHT_COOKIES",
            "_LIGHT_LAYERS"
        };

        internal static long VisitedVariants { get; private set; }
        internal static long RetainedVariants { get; private set; }

        public int callbackOrder => 0;

        public void OnProcessShader(
            Shader shader,
            ShaderSnippetData snippet,
            IList<ShaderCompilerData> data)
        {
            if (shader == null ||
                !IsMasterDeferredShader(shader.name) ||
                snippet.passName != MasterPassName)
                return;

            LoogaLightingVariantProfile profile = LoogaLightingVariantProfile.instance;
            if (!profile.StripMasterDeferredShader)
                return;

            VisitedVariants += data.Count;
            for (int i = data.Count - 1; i >= 0; i--)
            {
                if (!IsAllowed(shader, data[i], profile))
                    data.RemoveAt(i);
            }

            RetainedVariants += data.Count;
        }

        private static bool IsMasterDeferredShader(string shaderName)
        {
            return shaderName == MasterShaderName ||
                   shaderName.StartsWith(GeneratedMasterShaderPrefix, System.StringComparison.Ordinal);
        }

        internal static void ResetReport()
        {
            VisitedVariants = 0;
            RetainedVariants = 0;
        }

        private static bool IsAllowed(
            Shader shader,
            ShaderCompilerData data,
            LoogaLightingVariantProfile profile)
        {
            if (!TryGetMainLightShadowVariant(shader, data, out MainLightShadowVariants mainVariant) ||
                !profile.Allows(mainVariant))
            {
                return false;
            }

            if (!TryGetSoftShadowVariant(shader, data, out SoftShadowVariants softVariant) ||
                !profile.Allows(softVariant))
            {
                return false;
            }

            for (int i = 0; i < BinaryKeywords.Length; i++)
            {
                string keyword = BinaryKeywords[i];
                if (!profile.Allows(keyword, IsKeywordEnabled(shader, data, keyword)))
                    return false;
            }

            return true;
        }

        private static bool TryGetMainLightShadowVariant(
            Shader shader,
            ShaderCompilerData data,
            out MainLightShadowVariants variant)
        {
            bool standard = IsKeywordEnabled(shader, data, "_MAIN_LIGHT_SHADOWS");
            bool cascades = IsKeywordEnabled(shader, data, "_MAIN_LIGHT_SHADOWS_CASCADE");
            bool screenSpace = IsKeywordEnabled(shader, data, "_MAIN_LIGHT_SHADOWS_SCREEN");
            int enabledCount = (standard ? 1 : 0) + (cascades ? 1 : 0) + (screenSpace ? 1 : 0);

            variant = standard
                ? MainLightShadowVariants.Standard
                : cascades
                    ? MainLightShadowVariants.Cascades
                    : screenSpace
                        ? MainLightShadowVariants.ScreenSpace
                        : MainLightShadowVariants.Off;

            return enabledCount <= 1;
        }

        private static bool TryGetSoftShadowVariant(
            Shader shader,
            ShaderCompilerData data,
            out SoftShadowVariants variant)
        {
            bool standard = IsKeywordEnabled(shader, data, "_SHADOWS_SOFT");
            bool low = IsKeywordEnabled(shader, data, "_SHADOWS_SOFT_LOW");
            bool medium = IsKeywordEnabled(shader, data, "_SHADOWS_SOFT_MEDIUM");
            bool high = IsKeywordEnabled(shader, data, "_SHADOWS_SOFT_HIGH");
            int enabledCount = (standard ? 1 : 0) + (low ? 1 : 0) + (medium ? 1 : 0) + (high ? 1 : 0);

            variant = standard
                ? SoftShadowVariants.Standard
                : low
                    ? SoftShadowVariants.Low
                    : medium
                        ? SoftShadowVariants.Medium
                        : high
                            ? SoftShadowVariants.High
                            : SoftShadowVariants.Off;

            return enabledCount <= 1;
        }

        private static bool IsKeywordEnabled(Shader shader, ShaderCompilerData data, string keywordName)
        {
            ShaderKeyword keyword = new ShaderKeyword(shader, keywordName);
            return data.shaderKeywordSet.IsEnabled(keyword);
        }
    }

    internal sealed class LoogaLightingVariantBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            LoogaLightingShaderVariantStripper.ResetReport();
            LoogaLightingVariantProfile profile = LoogaLightingVariantProfile.instance;
            LoogaLightingVariantDetectionResult detection = LoogaLightingVariantDetector.Detect();

            if (profile.DetectBeforeBuild)
            {
                if (!detection.IsValid)
                    throw new BuildFailedException($"Looga Lighting variant detection failed. {detection.Summary}");

                profile.Apply(detection.Requirements);
                profile.SaveSettings();
            }

            if (profile.ValidateBeforeBuild)
            {
                if (!detection.IsValid)
                    throw new BuildFailedException($"Looga Lighting variant validation failed. {detection.Summary}");

                if (!profile.Contains(detection.Requirements, out string missingRequirement))
                {
                    throw new BuildFailedException(
                        $"Looga Lighting variant validation failed because the profile excludes required " +
                        $"{missingRequirement}. Open Project Settings > LoogaSoft > Lighting > Shader Variants " +
                        "and detect the project again.");
                }
            }

            // Model specialization is mandatory. The compile-profile toggle only controls
            // whether the remaining URP feature matrix is reduced before import.
            if (!LoogaMasterDeferredCompileProfile.Generate(profile, false))
                throw new BuildFailedException("Looga Lighting could not generate its project Master Deferred shader.");
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            LoogaLightingVariantProfile profile = LoogaLightingVariantProfile.instance;
            if (!profile.LogBuildReport || !profile.StripMasterDeferredShader)
                return;

            long visited = LoogaLightingShaderVariantStripper.VisitedVariants;
            long retained = LoogaLightingShaderVariantStripper.RetainedVariants;
            float actualReduction = visited > 0 ? 1f - retained / (float)visited : 0f;
            int estimatedVariants = profile.EstimateMasterVariants(
                LoogaMasterDeferredCompileProfile.ReferencedModelCount);
            float theoreticalReduction = 1f - estimatedVariants /
                (float)LoogaLightingVariantProfile.FullMasterVariantCount;

            Debug.Log(
                $"[Looga Lighting] Master deferred variant report: compiler visited {visited:N0}, " +
                $"retained {retained:N0} ({actualReduction:P1} removed after Unity filtering). " +
                $"Profile estimate: {estimatedVariants:N0}/" +
                $"{LoogaLightingVariantProfile.FullMasterVariantCount:N0} ({theoreticalReduction:P1} reduction).");
        }
    }
}
