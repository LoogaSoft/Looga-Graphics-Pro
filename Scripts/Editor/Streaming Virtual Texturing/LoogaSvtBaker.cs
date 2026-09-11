using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LoogaSoft.Rendering.StreamingVirtualTexturing;
using UnityEditor;
using UnityEngine;

namespace LoogaSoft.Rendering.Editor.StreamingVirtualTexturing
{
    /// <summary>Builds independently compressed texture tiles without changing source import settings.</summary>
    public static class LoogaSvtBaker
    {
        /// <summary>Bakes a square power-of-two albedo and optional normal and mask maps.</summary>
        public static LoogaStreamingVirtualTextureAsset Bake(LoogaSvtBakeSettings settings, string assetPath)
        {
            if (!settings || !settings.Albedo || settings.Albedo.width != settings.Albedo.height ||
                !Mathf.IsPowerOfTwo(settings.Albedo.width) || settings.Albedo.width < 128 || settings.Albedo.width > 8192)
                throw new ArgumentException("Choose a square power-of-two albedo from 128 to 8192 pixels.");
            if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal) || !assetPath.EndsWith(".asset", StringComparison.Ordinal))
                throw new ArgumentException("Choose an asset path under Assets.");
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (existing && existing != settings.Output)
                throw new InvalidOperationException("The output path belongs to another asset.");
            Shader shader = Shader.Find("Hidden/LoogaSoft/SVT/Bake");
            if (!shader || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("The SVT bake shader is unavailable.");
            const int tile = 128;
            int resolution = settings.Albedo.width;
            int edge = tile + 2 * LoogaStreamingVirtualTextureAsset.Border;
            string directory = Path.Combine(Application.streamingAssetsPath, "LoogaSVT");
            Directory.CreateDirectory(directory);
            string fileName = Guid.NewGuid().ToString("N") + ".lsvt";
            string destination = Path.Combine(directory, fileName);
            string temporary = destination + ".tmp";
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                Color32[][] channels =
                {
                    Read(settings.Albedo, resolution, material, 0, Color.white),
                    Read(settings.Normal, resolution, material, 1, new Color(0.5f, 0.5f, 1, 1)),
                    Read(settings.Mask, resolution, material, 2, new Color(0, 1, 0, 0.5f))
                };
                var records = new List<LoogaStreamingVirtualTextureAsset.Page>();
                byte[] fallback = null;
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using var writer = new BinaryWriter(file, System.Text.Encoding.UTF8, true);
                    writer.Write(LoogaSvtPageIO.Magic);
                    writer.Write(LoogaStreamingVirtualTextureAsset.FormatVersion);
                    for (int size = resolution, mip = 0; size >= 1; size >>= 1, mip++)
                    {
                        int side = Mathf.Max(1, size / tile);
                        for (int y = 0; y < side; y++)
                        {
                            if (EditorUtility.DisplayCancelableProgressBar("Bake Looga SVT", "Mip " + mip + ", row " + y,
                                (float)mip / ((int)Mathf.Log(resolution, 2) + 1)))
                                throw new OperationCanceledException("SVT bake canceled.");
                            for (int x = 0; x < side; x++)
                            {
                                byte[] bytes = new byte[edge * edge * 4 * 3];
                                for (int channel = 0; channel < 3; channel++)
                                {
                                    for (int py = 0; py < edge; py++)
                                    {
                                        for (int px = 0; px < edge; px++)
                                        {
                                            int sx = Address(x * tile + px - 4, size, settings.Repeat);
                                            int sy = Address(y * tile + py - 4, size, settings.Repeat);
                                            Color32 color = channels[channel][sy * size + sx];
                                            int offset = (channel * edge * edge + py * edge + px) * 4;
                                            bytes[offset] = color.r;
                                            bytes[offset + 1] = color.g;
                                            bytes[offset + 2] = color.b;
                                            bytes[offset + 3] = color.a;
                                        }
                                    }
                                }
                                records.Add(LoogaSvtPageIO.Write(file, bytes));
                                if (size == 1)
                                {
                                    fallback = bytes;
                                }
                            }
                        }
                        if (size > 1)
                        {
                            for (int channel = 0; channel < 3; channel++)
                            {
                                channels[channel] = Downsample(channels[channel], size, channel);
                            }
                        }
                    }
                }
                File.Move(temporary, destination);
                var output = existing as LoogaStreamingVirtualTextureAsset;
                bool created = !output;
                if (created)
                {
                    output = ScriptableObject.CreateInstance<LoogaStreamingVirtualTextureAsset>();
                }
                else
                {
                    Undo.RecordObject(output, "Rebake virtual texture");
                }
                output.Initialize(resolution, tile, settings.Repeat, fileName, records.ToArray(), fallback);
                if (created)
                {
                    AssetDatabase.CreateAsset(output, assetPath);
                }
                EditorUtility.SetDirty(output);
                AssetDatabase.SaveAssetIfDirty(output);
                Undo.RecordObject(settings, "Assign baked virtual texture");
                settings.Output = output;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssetIfDirty(settings);
                AssetDatabase.Refresh();
                var bindings = UnityEngine.Object.FindObjectsByType<LoogaStreamingVirtualTexture>(FindObjectsSortMode.None)
                    .Where(binding => binding.Asset == output && binding.isActiveAndEnabled).ToArray();
                foreach (var binding in bindings)
                {
                    binding.enabled = false;
                }
                foreach (var binding in bindings)
                {
                    binding.enabled = true;
                }
                return output;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
                EditorUtility.ClearProgressBar();
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static int Address(int value, int size, bool repeat) =>
            repeat ? ((value % size) + size) % size : Mathf.Clamp(value, 0, size - 1);

        private static Color32[] Read(Texture2D source, int size, Material material, int channel, Color fallback)
        {
            if (!source)
            {
                var pixels = new Color32[size * size];
                Array.Fill(pixels, (Color32)fallback);
                return pixels;
            }
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            RenderTexture previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = false;
                material.SetInt("_Channel", channel);
                Graphics.Blit(source, rt, material);
                RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                texture.Apply(false, false);
                return texture.GetPixels32();
            }
            finally
            {
                GL.sRGBWrite = srgb;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static Color32[] Downsample(Color32[] input, int size, int channel)
        {
            int nextSize = size / 2;
            var output = new Color32[nextSize * nextSize];
            for (int y = 0; y < nextSize; y++)
            {
                for (int x = 0; x < nextSize; x++)
                {
                    Color sum = Color.clear;
                    for (int dy = 0; dy < 2; dy++)
                    {
                        for (int dx = 0; dx < 2; dx++)
                        {
                            Color value = input[(y * 2 + dy) * size + x * 2 + dx];
                            sum += channel == 0 ? value.linear : value;
                        }
                    }
                    sum *= 0.25f;
                    if (channel == 0)
                    {
                        sum = sum.gamma;
                    }
                    else if (channel == 1)
                    {
                        Vector3 normal = new Vector3(sum.r * 2 - 1, sum.g * 2 - 1, sum.b * 2 - 1).normalized;
                        sum = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1);
                    }
                    output[y * nextSize + x] = sum;
                }
            }
            return output;
        }
    }

    [CustomEditor(typeof(LoogaSvtBakeSettings))]
    internal sealed class LoogaSvtBakeSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (GUILayout.Button("Bake Streaming Virtual Texture"))
            {
                var settings = (LoogaSvtBakeSettings)target;
                string path = settings.Output ? AssetDatabase.GetAssetPath(settings.Output) :
                    EditorUtility.SaveFilePanelInProject("Save virtual texture", "Virtual Texture", "asset", "Choose the baked asset path.");
                if (!string.IsNullOrEmpty(path))
                {
                    try
                    {
                        LoogaSvtBaker.Bake(settings, path);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }
        }
    }

    [CustomEditor(typeof(LoogaStreamingVirtualTexture))]
    internal sealed class LoogaStreamingVirtualTextureEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var binding = (LoogaStreamingVirtualTexture)target;
            if (!string.IsNullOrEmpty(binding.Error))
            {
                EditorGUILayout.HelpBox(binding.Error, MessageType.Error);
            }
            var cache = binding.Cache;
            if (cache != null)
            {
                EditorGUILayout.LabelField("Resident pages", cache.ResidentPages + " / " + cache.Capacity);
                EditorGUILayout.LabelField("Pending / failed", cache.PendingPages + " / " + cache.FailedPages);
                EditorGUILayout.LabelField("GPU cache", (cache.GpuBytes / 1048576f).ToString("F1") + " MiB");
                EditorGUILayout.LabelField("Uploaded / evicted", cache.UploadCount + " / " + cache.EvictionCount);
                if (!string.IsNullOrEmpty(cache.LastError))
                {
                    EditorGUILayout.HelpBox(cache.LastError, MessageType.Warning);
                }
                if (GUILayout.Button("Retry Failed Pages"))
                {
                    cache.RetryFailedPages();
                }
            }
        }
    }
}
