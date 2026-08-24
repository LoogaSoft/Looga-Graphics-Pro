using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace LoogaSoft.Lighting.Editor
{
    // Uses Unity's external-tool compiler path so timings match cold D3D backend work.
    internal static class LoogaMasterDeferredCompileDiagnostics
    {
        private const string PassName = "Looga Master Deferred Lighting";
        private const string ReportPath = "Library/LoogaLighting/MasterDeferredCompileProfile.csv";

        private readonly struct VariantCase
        {
            internal VariantCase(string name, string shaderPath)
            {
                Name = name;
                ShaderPath = shaderPath;
            }

            internal string Name { get; }
            internal string ShaderPath { get; }
        }

        [MenuItem("LoogaSoft/Graphics Pro/Lighting/Profile Master Deferred Compile #F12", priority = 21)]
        internal static void ProfileMasterDeferredCompile()
        {
            if (!LoogaMasterDeferredCompileProfile.RefreshFromProject(true, true))
                return;

            List<VariantCase> variants = BuildLightingModelVariants();
            if (variants.Count == 0)
            {
                UnityEngine.Debug.LogWarning(
                    "[Looga Lighting] No active URP renderer references a Looga Lighting model to profile.");
                return;
            }
            StringBuilder report = new StringBuilder(variants.Count * 128);
            report.AppendLine(
                "variant,keywords,preprocess_ms,compile_ms,preprocessed_chars,bytecode_bytes,success,messages");

            Stopwatch totalWatch = Stopwatch.StartNew();
            double totalPreprocessMs = 0.0;
            double totalCompileMs = 0.0;
            long totalPreprocessedChars = 0;
            long totalBytecodeBytes = 0;
            int failedVariants = 0;

            try
            {
                for (int i = 0; i < variants.Count; i++)
                {
                    VariantCase variant = variants[i];
                    EditorUtility.DisplayProgressBar(
                        "Looga Lighting Shader Diagnostics",
                        $"Compiling {variant.Name} ({i + 1}/{variants.Count})",
                        i / (float)variants.Count);

                    Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(variant.ShaderPath);
                    ShaderData.Pass pass = shader != null ? FindPass(shader) : null;
                    if (pass == null)
                        throw new InvalidOperationException(
                            $"Could not load pass '{PassName}' from {variant.ShaderPath}.");

                    string[] keywords = Array.Empty<string>();
                    Stopwatch watch = Stopwatch.StartNew();
                    ShaderData.PreprocessedVariant preprocessed = pass.PreprocessVariant(
                        ShaderType.Fragment,
                        keywords,
                        ShaderCompilerPlatform.D3D,
                        BuildTarget.StandaloneWindows64,
                        true);
                    watch.Stop();
                    double preprocessMs = watch.Elapsed.TotalMilliseconds;

                    watch.Restart();
                    ShaderData.VariantCompileInfo compiled = pass.CompileVariant(
                        ShaderType.Fragment,
                        keywords,
                        ShaderCompilerPlatform.D3D,
                        BuildTarget.StandaloneWindows64,
                        true);
                    watch.Stop();
                    double compileMs = watch.Elapsed.TotalMilliseconds;

                    int preprocessedChars = preprocessed.PreprocessedCode?.Length ?? 0;
                    int bytecodeBytes = compiled.ShaderData?.Length ?? 0;
                    int messageCount = (preprocessed.Messages?.Length ?? 0) +
                                       (compiled.Messages?.Length ?? 0);
                    bool success = preprocessed.Success && compiled.Success;

                    totalPreprocessMs += preprocessMs;
                    totalCompileMs += compileMs;
                    totalPreprocessedChars += preprocessedChars;
                    totalBytecodeBytes += bytecodeBytes;
                    if (!success)
                        failedVariants++;

                    report.Append(EscapeCsv(variant.Name)).Append(',')
                        .Append(EscapeCsv(string.Empty)).Append(',')
                        .Append(preprocessMs.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                        .Append(compileMs.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                        .Append(preprocessedChars).Append(',')
                        .Append(bytecodeBytes).Append(',')
                        .Append(success ? "true" : "false").Append(',')
                        .Append(messageCount)
                        .AppendLine();
                }
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                return;
            }
            finally
            {
                totalWatch.Stop();
                EditorUtility.ClearProgressBar();
            }

            string absoluteReportPath = Path.GetFullPath(ReportPath);
            string directory = Path.GetDirectoryName(absoluteReportPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(absoluteReportPath, report.ToString(), new UTF8Encoding(false));

            UnityEngine.Debug.Log(
                $"[Looga Lighting] Master Deferred compile profile complete: {variants.Count} fragment " +
                $"variants in {totalWatch.Elapsed.TotalSeconds:F2}s. Preprocessing {totalPreprocessMs:F1}ms; " +
                $"backend compilation {totalCompileMs:F1}ms; expanded source " +
                $"{totalPreprocessedChars / (1024.0 * 1024.0):F2} MiB; bytecode " +
                $"{totalBytecodeBytes / (1024.0 * 1024.0):F2} MiB; failures {failedVariants}. " +
                $"Report: {ReportPath}");
        }

        private static ShaderData.Pass FindPass(Shader shader)
        {
            ShaderData shaderData = ShaderUtil.GetShaderData(shader);
            ShaderData.Subshader subshader = shaderData.ActiveSubshader;
            if (subshader == null)
                return null;

            for (int i = 0; i < subshader.PassCount; i++)
            {
                ShaderData.Pass pass = subshader.GetPass(i);
                if (pass != null && pass.Name == PassName)
                    return pass;
            }

            return null;
        }

        private static List<VariantCase> BuildLightingModelVariants()
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:Shader",
                new[] { LoogaMasterDeferredCompileProfile.GeneratedShaderDirectory });
            List<VariantCase> variants = new List<VariantCase>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                string fileName = Path.GetFileNameWithoutExtension(path);
                const string prefix = "Looga Master Deferred ";
                if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                variants.Add(new VariantCase(fileName.Substring(prefix.Length), path));
            }

            variants.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));

            return variants;
        }

        private static string EscapeCsv(string value)
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
    }
}
