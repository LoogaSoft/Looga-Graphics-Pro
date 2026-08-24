using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LoogaSoft.Lighting.Editor
{
    [InitializeOnLoad]
    internal static class LoogaMasterDeferredCompileProfile
    {
        internal const string GeneratedShaderDirectory =
            "Assets/Resources/Shaders/Generated/LoogaSoft";

        private const string SourceShaderName = "Hidden/LoogaSoft/Lighting/MasterDeferred";
        private const string TemplateRelativePath = "Shaders/Looga Master Deferred.shader.template";
        private const string GenerationCachePath =
            "Library/LoogaLighting/MasterDeferredGeneration.hash";
        private const int GeneratorVersion = 2;
        private const string GeneratedShaderNamePrefix =
            "Hidden/LoogaSoft/Lighting/MasterDeferredProject/";
        private const string PreviousGeneratedShaderDirectory =
            "Assets/LoogaSoft/Lighting/Resources";
        private const string LegacyGeneratedShaderDirectory =
            "Assets/LoogaSoft/Lighting/Generated/Resources/LoogaSoft/Lighting";
        private const string LegacyGeneratedShaderPath =
            "Assets/LoogaSoft/Lighting/Generated/Looga Master Deferred Project.shader";
        private const string ModelVariantPragma =
            "#pragma multi_compile_local_fragment _ _LOOGA_MODEL_SOURCE2 _LOOGA_MODEL_MINNAERT _LOOGA_MODEL_OVERWATCH _LOOGA_MODEL_OREN_NAYAR _LOOGA_MODEL_ARKANE _LOOGA_MODEL_CUSTOM";
        private const string FixedModelBlockStart = "// LOOGA_FIXED_MODEL_BEGIN";
        private const string FixedModelBlockEnd = "// LOOGA_FIXED_MODEL_END";
        private const string FirstVariantPragma =
            "#pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN";
        private const string LastVariantPragma =
            "#pragma multi_compile_fragment _ _LIGHT_LAYERS";

        private static bool s_refreshing;

        private readonly struct LightingModelShader
        {
            internal LightingModelShader(LoogaLightingFeature.LightingModel model, string token, int value)
            {
                Model = model;
                Token = token;
                Value = value;
            }

            internal LoogaLightingFeature.LightingModel Model { get; }
            internal string Token { get; }
            internal int Value { get; }
            internal string AssetPath =>
                $"{GeneratedShaderDirectory}/Looga Master Deferred {Token}.shader";
            internal string PreviousAssetPath =>
                $"{PreviousGeneratedShaderDirectory}/Looga Master Deferred {Token}.shader";
            internal string LegacyAssetPath =>
                $"{LegacyGeneratedShaderDirectory}/Looga Master Deferred {Token}.shader";
            internal string ShaderName => $"{GeneratedShaderNamePrefix}{Token}";
        }

        private sealed class LightingUsage
        {
            internal readonly HashSet<LoogaLightingFeature.LightingModel> Models = new();
            internal readonly HashSet<LoogaLightingFeature> Features = new();
        }

        private static readonly LightingModelShader[] LightingModelShaders =
        {
            new(LoogaLightingFeature.LightingModel.DisneyBurley, "DisneyBurley", 0),
            new(LoogaLightingFeature.LightingModel.Source2, "Source2", 1),
            new(LoogaLightingFeature.LightingModel.Minnaert, "Minnaert", 3),
            new(LoogaLightingFeature.LightingModel.Overwatch, "Overwatch", 4),
            new(LoogaLightingFeature.LightingModel.OrenNayar, "OrenNayar", 5),
            new(LoogaLightingFeature.LightingModel.Arkane, "Arkane", 6),
            new(LoogaLightingFeature.LightingModel.Custom, "Custom", 100)
        };

        static LoogaMasterDeferredCompileProfile()
        {
            EditorApplication.delayCall += RefreshAfterReload;
            EditorApplication.projectChanged += ScheduleRefresh;
        }

        internal static int ReferencedModelCount => CollectLightingUsage().Models.Count;

        internal static void ScheduleRefresh()
        {
            EditorApplication.delayCall -= RefreshAfterReload;
            EditorApplication.delayCall += RefreshAfterReload;
        }

        internal static bool RefreshFromProject(bool logResult, bool force = false)
        {
            LoogaLightingVariantProfile profile = LoogaLightingVariantProfile.instance;
            if (profile.UseEditorCompileProfile)
            {
                LoogaLightingVariantDetectionResult detection = LoogaLightingVariantDetector.Detect();
                if (!detection.IsValid)
                {
                    AssignFallbackToActiveFeatures(CollectLightingUsage());
                    if (logResult)
                    {
                        Debug.LogWarning(
                            $"[Looga Lighting] Editor compile profile was not generated. {detection.Summary}");
                    }

                    return false;
                }

                if (profile.AutoDetectEditorCompileProfile)
                {
                    string previousProfile = EditorJsonUtility.ToJson(profile);
                    profile.Apply(detection.Requirements);
                    if (previousProfile != EditorJsonUtility.ToJson(profile))
                        profile.SaveSettings();
                }
            }

            return Generate(profile, logResult, force);
        }

        public static void RefreshFromCommandLine()
        {
            if (!RefreshFromProject(true, true))
                EditorApplication.Exit(1);
        }

        internal static bool Generate(
            LoogaLightingVariantProfile profile,
            bool logResult,
            bool force = false)
        {
            if (!TryReadTemplate(out string source))
                return false;

            LightingUsage usage = CollectLightingUsage();
            string fingerprint = CalculateFingerprint(source, profile, usage.Models);
            if (!force && IsGenerationCurrent(fingerprint, usage))
                return true;

            MigrateGeneratedShaders();
            EnsureAssetFolder(GeneratedShaderDirectory);

            List<LightingModelShader> requiredModels = GetRequiredModels(usage.Models);
            List<string> changedShaderPaths = new(requiredModels.Count);
            for (int i = 0; i < requiredModels.Count; i++)
            {
                LightingModelShader model = requiredModels[i];
                string generated = CreateGeneratedSource(source, profile, model);
                if (string.IsNullOrEmpty(generated))
                    return false;

                string absoluteGeneratedPath = Path.GetFullPath(model.AssetPath);
                bool sourceChanged = !File.Exists(absoluteGeneratedPath) ||
                                     NormalizeLineEndings(File.ReadAllText(absoluteGeneratedPath)) != generated;
                if (!sourceChanged)
                    continue;

                File.WriteAllText(absoluteGeneratedPath, generated, new UTF8Encoding(false));
                changedShaderPaths.Add(model.AssetPath);
            }

            bool removedStaleShaders = DeleteUnreferencedGeneratedShaders(usage.Models);
            for (int i = 0; i < changedShaderPaths.Count; i++)
            {
                AssetDatabase.ImportAsset(
                    changedShaderPaths[i],
                    ImportAssetOptions.ForceSynchronousImport);
            }

            Shader fallback = Shader.Find(SourceShaderName);
            Dictionary<LoogaLightingFeature.LightingModel, Shader> generatedShaders =
                new(requiredModels.Count);
            for (int i = 0; i < requiredModels.Count; i++)
            {
                LightingModelShader model = requiredModels[i];
                Shader generatedShader = AssetDatabase.LoadAssetAtPath<Shader>(model.AssetPath);
                if (generatedShader == null || ShaderUtil.ShaderHasError(generatedShader))
                {
                    Debug.LogError(
                        $"[Looga Lighting] Unity could not compile the generated {model.Token} Master " +
                        $"Deferred shader at {model.AssetPath}. The compact Disney/Burley fallback will be used.");
                    AssignFallbackToActiveFeatures(usage);
                    return false;
                }

                generatedShaders.Add(model.Model, generatedShader);
            }

            bool referencesChanged = AssignShadersToFeatures(usage, generatedShaders, fallback);
            if (referencesChanged)
                AssetDatabase.SaveAssets();

            if (AssetDatabase.LoadAssetAtPath<Shader>(LegacyGeneratedShaderPath) != null)
                AssetDatabase.DeleteAsset(LegacyGeneratedShaderPath);
            DeletePreviousGeneratedFoldersIfEmpty();
            WriteGenerationFingerprint(fingerprint);

            if (logResult)
            {
                string action = changedShaderPaths.Count > 0 || removedStaleShaders
                    ? "Generated"
                    : "Validated";
                int estimatedVariants = profile.EstimateMasterVariants(requiredModels.Count);
                Debug.Log(
                    $"[Looga Lighting] {action} {requiredModels.Count} referenced model-specialized " +
                    $"Master Deferred shader(s), approximately {estimatedVariants:N0} theoretical variants, " +
                    $"under {GeneratedShaderDirectory}.");
            }

            return true;
        }

        private static bool TryReadTemplate(out string source)
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(LoogaMasterDeferredCompileProfile).Assembly);
            string templatePath = package == null
                ? string.Empty
                : Path.Combine(package.resolvedPath, TemplateRelativePath);

            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
            {
                Debug.LogError(
                    $"[Looga Lighting] Could not locate the non-imported Master Deferred template at " +
                    $"{TemplateRelativePath}.");
                source = string.Empty;
                return false;
            }

            source = NormalizeLineEndings(File.ReadAllText(templatePath));
            return true;
        }

        private static LightingUsage CollectLightingUsage()
        {
            LightingUsage usage = new();
            List<UniversalRenderPipelineAsset> pipelineAssets =
                LoogaLightingVariantDetector.FindActivePipelineAssets();
            List<UniversalRendererData> renderers =
                LoogaLightingVariantDetector.FindLoogaRenderers(pipelineAssets);

            for (int rendererIndex = 0; rendererIndex < renderers.Count; rendererIndex++)
            {
                SerializedObject renderer = new(renderers[rendererIndex]);
                SerializedProperty features = renderer.FindProperty("m_RendererFeatures");
                if (features == null || !features.isArray)
                    continue;

                for (int featureIndex = 0; featureIndex < features.arraySize; featureIndex++)
                {
                    LoogaLightingFeature feature = features.GetArrayElementAtIndex(featureIndex)
                        .objectReferenceValue as LoogaLightingFeature;
                    if (feature == null || !feature.isActive)
                        continue;

                    usage.Features.Add(feature);
                    usage.Models.Add(NormalizeModel(feature.activeLightingModel));
                }
            }

            return usage;
        }

        private static List<LightingModelShader> GetRequiredModels(
            HashSet<LoogaLightingFeature.LightingModel> models)
        {
            List<LightingModelShader> required = new(models.Count);
            for (int i = 0; i < LightingModelShaders.Length; i++)
            {
                if (models.Contains(LightingModelShaders[i].Model))
                    required.Add(LightingModelShaders[i]);
            }

            return required;
        }

        private static string CalculateFingerprint(
            string source,
            LoogaLightingVariantProfile profile,
            HashSet<LoogaLightingFeature.LightingModel> models)
        {
            List<int> modelValues = new(models.Count);
            foreach (LoogaLightingFeature.LightingModel model in models)
                modelValues.Add((int)model);
            modelValues.Sort();

            StringBuilder input = new(source.Length + 1024);
            input.Append(GeneratorVersion)
                .Append('\n').Append(source)
                .Append('\n').Append(EditorJsonUtility.ToJson(profile))
                .Append('\n').Append(Application.unityVersion)
                .Append('\n').Append(typeof(UniversalRenderPipelineAsset).Assembly.GetName().Version);
            for (int i = 0; i < modelValues.Count; i++)
                input.Append('\n').Append(modelValues[i]);

            return Hash128.Compute(input.ToString()).ToString();
        }

        private static bool IsGenerationCurrent(string fingerprint, LightingUsage usage)
        {
            if (!File.Exists(GenerationCachePath) ||
                File.ReadAllText(GenerationCachePath).Trim() != fingerprint)
            {
                return false;
            }

            for (int i = 0; i < LightingModelShaders.Length; i++)
            {
                LightingModelShader model = LightingModelShaders[i];
                bool shouldExist = usage.Models.Contains(model.Model);
                bool exists = File.Exists(Path.GetFullPath(model.AssetPath));
                if (shouldExist != exists)
                    return false;
            }

            foreach (LoogaLightingFeature feature in usage.Features)
            {
                if (feature == null || feature.masterDeferredShader == null)
                    return false;

                string expectedName = GeneratedShaderNamePrefix + GetModelToken(feature.activeLightingModel);
                if (feature.masterDeferredShader.name != expectedName)
                    return false;
            }

            return true;
        }

        private static void WriteGenerationFingerprint(string fingerprint)
        {
            string directory = Path.GetDirectoryName(GenerationCachePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(GenerationCachePath, fingerprint, new UTF8Encoding(false));
        }

        private static bool DeleteUnreferencedGeneratedShaders(
            HashSet<LoogaLightingFeature.LightingModel> requiredModels)
        {
            bool changed = false;
            for (int i = 0; i < LightingModelShaders.Length; i++)
            {
                LightingModelShader model = LightingModelShaders[i];
                if (requiredModels.Contains(model.Model) ||
                    AssetDatabase.LoadMainAssetAtPath(model.AssetPath) == null)
                {
                    continue;
                }

                changed |= AssetDatabase.DeleteAsset(model.AssetPath);
            }

            return changed;
        }

        private static bool AssignShadersToFeatures(
            LightingUsage usage,
            IReadOnlyDictionary<LoogaLightingFeature.LightingModel, Shader> shaders,
            Shader fallback)
        {
            bool changed = false;
            foreach (LoogaLightingFeature feature in usage.Features)
            {
                if (feature == null)
                    continue;

                Shader shader = fallback;
                LoogaLightingFeature.LightingModel model = NormalizeModel(feature.activeLightingModel);
                if (shaders.TryGetValue(model, out Shader specializedShader))
                    shader = specializedShader;

                if (feature.masterDeferredShader == shader)
                    continue;

                feature.masterDeferredShader = shader;
                EditorUtility.SetDirty(feature);
                changed = true;
            }

            return changed;
        }

        private static void AssignFallbackToActiveFeatures(LightingUsage usage)
        {
            Shader fallback = Shader.Find(SourceShaderName);
            if (fallback == null)
                return;

            bool referencesChanged = AssignShadersToFeatures(
                usage,
                new Dictionary<LoogaLightingFeature.LightingModel, Shader>(),
                fallback);
            if (referencesChanged)
                AssetDatabase.SaveAssets();
        }

        private static string GetModelToken(LoogaLightingFeature.LightingModel model)
        {
            model = NormalizeModel(model);
            for (int i = 0; i < LightingModelShaders.Length; i++)
            {
                if (LightingModelShaders[i].Model == model)
                    return LightingModelShaders[i].Token;
            }

            return LightingModelShaders[0].Token;
        }

        private static LoogaLightingFeature.LightingModel NormalizeModel(
            LoogaLightingFeature.LightingModel model)
        {
            for (int i = 0; i < LightingModelShaders.Length; i++)
            {
                if (LightingModelShaders[i].Model == model)
                    return model;
            }

            return LoogaLightingFeature.LightingModel.DisneyBurley;
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            string[] segments = assetPath.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = $"{current}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }

        private static void MigrateGeneratedShaders()
        {
            for (int i = 0; i < LightingModelShaders.Length; i++)
            {
                LightingModelShader model = LightingModelShaders[i];
                MigrateGeneratedShader(model.PreviousAssetPath, model.AssetPath);
                MigrateGeneratedShader(model.LegacyAssetPath, model.AssetPath);
            }
        }

        private static void MigrateGeneratedShader(string sourcePath, string destinationPath)
        {
            if (AssetDatabase.LoadAssetAtPath<Shader>(sourcePath) == null)
                return;

            if (AssetDatabase.LoadAssetAtPath<Shader>(destinationPath) != null)
            {
                AssetDatabase.DeleteAsset(sourcePath);
                return;
            }

            EnsureAssetFolder(GeneratedShaderDirectory);
            string error = AssetDatabase.MoveAsset(sourcePath, destinationPath);
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning(
                    $"[Looga Lighting] Could not move the generated shader to {destinationPath}. {error}");
            }
        }

        private static void DeletePreviousGeneratedFoldersIfEmpty()
        {
            DeleteAssetFolderIfEmpty(LegacyGeneratedShaderDirectory);
            DeleteAssetFolderIfEmpty("Assets/LoogaSoft/Lighting/Generated/Resources/LoogaSoft");
            DeleteAssetFolderIfEmpty("Assets/LoogaSoft/Lighting/Generated/Resources");
            DeleteAssetFolderIfEmpty("Assets/LoogaSoft/Lighting/Generated");
            DeleteAssetFolderIfEmpty(PreviousGeneratedShaderDirectory);
            DeleteAssetFolderIfEmpty("Assets/LoogaSoft/Lighting");
            DeleteAssetFolderIfEmpty("Assets/LoogaSoft");
        }

        private static void DeleteAssetFolderIfEmpty(string assetPath)
        {
            if (!AssetDatabase.IsValidFolder(assetPath))
                return;

            string absolutePath = Path.GetFullPath(assetPath);
            if (!Directory.Exists(absolutePath))
                return;

            foreach (string entry in Directory.EnumerateFileSystemEntries(absolutePath))
            {
                if (!entry.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    return;
            }

            AssetDatabase.DeleteAsset(assetPath);
        }

        private static void RefreshAfterReload()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (s_refreshing || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                ScheduleRefresh();
                return;
            }

            s_refreshing = true;
            try
            {
                RefreshFromProject(false);
            }
            finally
            {
                s_refreshing = false;
            }
        }

        private static string CreateGeneratedSource(
            string source,
            LoogaLightingVariantProfile profile,
            LightingModelShader model)
        {
            string sourceDeclaration = $"Shader \"{SourceShaderName}\"";
            int declarationIndex = source.IndexOf(sourceDeclaration, StringComparison.Ordinal);
            int firstPragmaIndex = source.IndexOf(FirstVariantPragma, StringComparison.Ordinal);
            int lastPragmaIndex = source.IndexOf(LastVariantPragma, StringComparison.Ordinal);
            int modelPragmaIndex = source.IndexOf(ModelVariantPragma, StringComparison.Ordinal);
            int fixedModelStart = source.IndexOf(FixedModelBlockStart, StringComparison.Ordinal);
            int fixedModelEnd = source.IndexOf(FixedModelBlockEnd, StringComparison.Ordinal);
            if (declarationIndex < 0 ||
                firstPragmaIndex < 0 ||
                lastPragmaIndex < firstPragmaIndex ||
                modelPragmaIndex < 0 ||
                fixedModelStart < 0 ||
                fixedModelEnd < fixedModelStart)
            {
                Debug.LogError(
                    "[Looga Lighting] The Master Deferred template no longer matches the generator markers.");
                return string.Empty;
            }

            string generated = source;
            if (profile.UseEditorCompileProfile)
            {
                int blockStart = source.LastIndexOf('\n', firstPragmaIndex);
                blockStart = blockStart < 0 ? firstPragmaIndex : blockStart + 1;
                int blockEnd = source.IndexOf('\n', lastPragmaIndex);
                blockEnd = blockEnd < 0 ? source.Length : blockEnd + 1;
                generated = source.Substring(0, blockStart) +
                            BuildVariantBlock(profile) +
                            source.Substring(blockEnd);
            }

            generated = RemoveContainingLine(generated, ModelVariantPragma);
            generated = ReplaceMarkedBlock(
                generated,
                FixedModelBlockStart,
                FixedModelBlockEnd,
                $"            {FixedModelBlockStart}\n" +
                $"            #define LOOGA_FIXED_LIGHTING_MODEL {model.Value}\n" +
                $"            {FixedModelBlockEnd}");

            return generated.Replace(
                sourceDeclaration,
                $"Shader \"{model.ShaderName}\"\n" +
                $"// Generated {model.Token} model by Looga Lighting. Changes will be overwritten.");
        }

        private static string RemoveContainingLine(string source, string value)
        {
            int valueIndex = source.IndexOf(value, StringComparison.Ordinal);
            if (valueIndex < 0)
                return source;

            int lineStart = source.LastIndexOf('\n', valueIndex);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            int lineEnd = source.IndexOf('\n', valueIndex);
            lineEnd = lineEnd < 0 ? source.Length : lineEnd + 1;
            return source.Remove(lineStart, lineEnd - lineStart);
        }

        private static string ReplaceMarkedBlock(
            string source,
            string startMarker,
            string endMarker,
            string replacement)
        {
            int start = source.IndexOf(startMarker, StringComparison.Ordinal);
            int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
            if (start < 0 || end < 0)
                return source;

            int lineStart = source.LastIndexOf('\n', start);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            int lineEnd = source.IndexOf('\n', end);
            lineEnd = lineEnd < 0 ? source.Length : lineEnd;
            return source.Substring(0, lineStart) + replacement + source.Substring(lineEnd);
        }

        private static string BuildVariantBlock(LoogaLightingVariantProfile profile)
        {
            StringBuilder builder = new(1024);

            AppendGroup(builder, new[]
            {
                (profile.Allows(MainLightShadowVariants.Off), string.Empty),
                (profile.Allows(MainLightShadowVariants.Standard), "_MAIN_LIGHT_SHADOWS"),
                (profile.Allows(MainLightShadowVariants.Cascades), "_MAIN_LIGHT_SHADOWS_CASCADE"),
                (profile.Allows(MainLightShadowVariants.ScreenSpace), "_MAIN_LIGHT_SHADOWS_SCREEN")
            });

            AppendGroup(builder, new[]
            {
                (profile.Allows(SoftShadowVariants.Off), string.Empty),
                (profile.Allows(SoftShadowVariants.Standard), "_SHADOWS_SOFT"),
                (profile.Allows(SoftShadowVariants.Low), "_SHADOWS_SOFT_LOW"),
                (profile.Allows(SoftShadowVariants.Medium), "_SHADOWS_SOFT_MEDIUM"),
                (profile.Allows(SoftShadowVariants.High), "_SHADOWS_SOFT_HIGH")
            });

            AppendBinary(builder, profile, "_GBUFFER_NORMALS_OCT");
            AppendBinary(builder, profile, "_REFLECTION_PROBE_BLENDING");
            AppendBinary(builder, profile, "_REFLECTION_PROBE_BOX_PROJECTION");
            AppendBinary(builder, profile, "_REFLECTION_PROBE_ATLAS");
            AppendBinary(builder, profile, "REFLECTION_PROBE_ROTATION");
            AppendDefine(builder, "_CLUSTER_LIGHT_LOOP");
            AppendBinary(builder, profile, "_ADDITIONAL_LIGHT_SHADOWS");
            AppendBinary(builder, profile, "LIGHTMAP_SHADOW_MIXING");
            AppendBinary(builder, profile, "SHADOWS_SHADOWMASK");
            AppendBinary(builder, profile, "_DEFERRED_MIXED_LIGHTING");
            AppendBinary(builder, profile, "_SCREEN_SPACE_OCCLUSION");
            AppendBinary(builder, profile, "_LIGHT_COOKIES");
            AppendBinary(builder, profile, "_LIGHT_LAYERS");
            builder.Append('\n');
            return builder.ToString();
        }

        private static void AppendGroup(
            StringBuilder builder,
            IReadOnlyList<(bool Enabled, string Keyword)> variants)
        {
            List<string> enabled = new(variants.Count);
            for (int i = 0; i < variants.Count; i++)
            {
                if (variants[i].Enabled)
                    enabled.Add(variants[i].Keyword);
            }

            if (enabled.Count == 0 || enabled.Count == 1 && string.IsNullOrEmpty(enabled[0]))
                return;

            if (enabled.Count == 1)
            {
                AppendDefine(builder, enabled[0]);
                return;
            }

            builder.Append("            #pragma multi_compile_fragment");
            for (int i = 0; i < enabled.Count; i++)
                builder.Append(' ').Append(string.IsNullOrEmpty(enabled[i]) ? "_" : enabled[i]);
            builder.Append('\n');
        }

        private static void AppendBinary(
            StringBuilder builder,
            LoogaLightingVariantProfile profile,
            string keyword)
        {
            bool disabled = profile.Allows(keyword, false);
            bool enabled = profile.Allows(keyword, true);
            if (disabled && enabled)
                builder.Append("            #pragma multi_compile_fragment _ ").Append(keyword).Append('\n');
            else if (enabled)
                AppendDefine(builder, keyword);
        }

        private static void AppendDefine(StringBuilder builder, string keyword)
        {
            builder.Append("            #define ").Append(keyword).Append(" 1\n");
        }

        private static string NormalizeLineEndings(string value)
        {
            return value.Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }
}
