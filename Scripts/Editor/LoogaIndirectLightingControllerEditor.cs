using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace LoogaSoft.Lighting.Editor
{
    [CustomEditor(typeof(LoogaIndirectLightingController))]
    public sealed class LoogaIndirectLightingControllerEditor : LoogaEditorBase
    {
        private const string PrecomputeGuid = "7c4d48571288460e8a73e027ad7e84cc";

        private struct RadianceSample
        {
            public Vector3 direction;
            public Color color;
            public float weight;
        }

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            DrawLoogaSoftHeader();
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();

            LoogaIndirectLightingController controller = (LoogaIndirectLightingController)target;
            EditorGUILayout.Space(6);
            bool reflectionBakeSupported = SystemInfo.supportsComputeShaders && SystemInfo.supportsCubemapArrayTextures;
            if (!reflectionBakeSupported || !SystemInfo.supports2DArrayTextures || !SystemInfo.supports3DTextures)
            {
                EditorGUILayout.HelpBox(
                    "The active graphics device does not support every optional indirect-lighting resource. Unsupported features will remain disabled.",
                    MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || !reflectionBakeSupported))
            {
                if (GUILayout.Button("Bake Model-Aware Reflections", GUILayout.Height(26)))
                    BakeReflections(controller);
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || !SystemInfo.supports2DArrayTextures))
            {
                if (GUILayout.Button("Build Auxiliary Lightmap Arrays", GUILayout.Height(24)))
                    BuildAuxiliaryLightmaps(controller);
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || !SystemInfo.supports3DTextures))
            {
                if (GUILayout.Button("Bake Radiance Probe Volume", GUILayout.Height(24)))
                    BakeRadianceVolume(controller);
            }

            EditorGUILayout.HelpBox(
                "Directional Unity lightmaps are decoded automatically at runtime. Auxiliary arrays contain additional directional lobes and are indexed to match Unity lightmap indices.",
                MessageType.Info);
        }

        [MenuItem("GameObject/LoogaSoft/Graphics Pro/Indirect Lighting Controller", false, 10)]
        private static void CreateController(MenuCommand command)
        {
            GameObject gameObject = new GameObject("Looga Indirect Lighting");
            GameObjectUtility.SetParentAndAlign(gameObject, command.context as GameObject);
            gameObject.AddComponent<LoogaIndirectLightingController>();
            Undo.RegisterCreatedObjectUndo(gameObject, "Create Looga Indirect Lighting Controller");
            Selection.activeObject = gameObject;
        }

        private static LoogaIndirectLightingData EnsureDataAsset(LoogaIndirectLightingController controller)
        {
            if (controller.data != null)
                return controller.data;

            string path = EditorUtility.SaveFilePanelInProject(
                "Create Looga Indirect Lighting Data",
                $"{controller.gameObject.scene.name} Indirect Lighting",
                "asset",
                "Choose where to save the generated indirect-lighting data.");
            if (string.IsNullOrEmpty(path))
                return null;

            LoogaIndirectLightingData data = CreateInstance<LoogaIndirectLightingData>();
            AssetDatabase.CreateAsset(data, path);
            Undo.RecordObject(controller, "Assign Looga Indirect Lighting Data");
            controller.data = data;
            EditorUtility.SetDirty(controller);
            return data;
        }

        private static ComputeShader LoadPrecomputeShader()
        {
            string path = AssetDatabase.GUIDToAssetPath(PrecomputeGuid);
            return AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
        }

        private static void BakeReflections(LoogaIndirectLightingController controller)
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsCubemapArrayTextures)
            {
                EditorUtility.DisplayDialog("Looga Lighting", "Model-aware reflection baking requires compute shaders and cubemap arrays.", "OK");
                return;
            }

            LoogaIndirectLightingData data = EnsureDataAsset(controller);
            ComputeShader compute = LoadPrecomputeShader();
            if (data == null || compute == null)
            {
                Debug.LogError("Looga Lighting could not create the data asset or load LoogaIBLPrecompute.compute.");
                return;
            }

            ReflectionProbe[] probes = FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None)
                .Where(probe => probe.enabled && probe.gameObject.activeInHierarchy && probe.gameObject.scene.isLoaded)
                .OrderByDescending(probe => probe.importance)
                .Take(32)
                .ToArray();
            if (probes.Length == 0)
            {
                EditorUtility.DisplayDialog("Looga Lighting", "No enabled Reflection Probes were found in the loaded scenes.", "OK");
                return;
            }

            int resolution = Mathf.NextPowerOfTwo(Mathf.Clamp(controller.reflectionBakeResolution, 32, 512));
            int mipCount = Mathf.FloorToInt(Mathf.Log(resolution, 2f)) + 1;
            CubemapArray ggx = CreateCubemapArray(resolution, probes.Length, "Looga GGX Reflection Probes");
            CubemapArray beckmann = CreateCubemapArray(resolution, probes.Length, "Looga Beckmann Reflection Probes");
            CubemapArray phong = CreateCubemapArray(resolution, probes.Length, "Looga Phong Reflection Probes");
            LoogaIndirectLightingData.ReflectionProbeRecord[] records = new LoogaIndirectLightingData.ReflectionProbeRecord[probes.Length];
            Shader.SetGlobalFloat("_LoogaModelReflectionsEnabled", 0f);

            try
            {
                for (int probeIndex = 0; probeIndex < probes.Length; probeIndex++)
                {
                    ReflectionProbe probe = probes[probeIndex];
                    EditorUtility.DisplayProgressBar("Looga Model-Aware Reflections", $"Capturing {probe.name}", probeIndex / (float)probes.Length);
                    RenderTexture source = CaptureProbe(probe, resolution);
                    if (source == null)
                        continue;

                    FilterProbe(compute, source, ggx, probeIndex, mipCount, "PrefilterGGX", controller.reflectionSampleCount);
                    FilterProbe(compute, source, beckmann, probeIndex, mipCount, "PrefilterBeckmann", controller.reflectionSampleCount);
                    FilterProbe(compute, source, phong, probeIndex, mipCount, "PrefilterPhong", controller.reflectionSampleCount);
                    source.Release();
                    DestroyImmediate(source);

                    Vector3 scale = probe.transform.lossyScale;
                    Vector3 size = Vector3.Scale(probe.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                    records[probeIndex] = new LoogaIndirectLightingData.ReflectionProbeRecord
                    {
                        center = probe.transform.TransformPoint(probe.center),
                        extents = size * 0.5f,
                        capturePosition = probe.transform.TransformPoint(probe.center),
                        rotation = probe.transform.rotation,
                        blendDistance = Mathf.Max(probe.blendDistance, 0.01f),
                        intensity = probe.intensity,
                        slice = probeIndex,
                        boxProjection = probe.boxProjection
                    };
                }

                EditorUtility.DisplayProgressBar("Looga Model-Aware Reflections", "Integrating environment BRDF LUTs", 0.95f);
                Texture2D ggxLut = BakeBrdfLut(compute, "IntegrateGGX", "Looga GGX BRDF LUT", controller.reflectionSampleCount);
                Texture2D beckmannLut = BakeBrdfLut(compute, "IntegrateBeckmann", "Looga Beckmann BRDF LUT", controller.reflectionSampleCount);
                Texture2D phongLut = BakeBrdfLut(compute, "IntegratePhong", "Looga Phong BRDF LUT", controller.reflectionSampleCount);

                ReplaceSubAsset(data, ref data.ggxReflectionProbes, ggx);
                ReplaceSubAsset(data, ref data.beckmannReflectionProbes, beckmann);
                ReplaceSubAsset(data, ref data.phongReflectionProbes, phong);
                ReplaceSubAsset(data, ref data.ggxBrdfLut, ggxLut);
                ReplaceSubAsset(data, ref data.beckmannBrdfLut, beckmannLut);
                ReplaceSubAsset(data, ref data.phongBrdfLut, phongLut);
                data.reflectionProbes = records;
                SaveData(data, controller);
            }
            catch (Exception exception)
            {
                DestroyImmediate(ggx);
                DestroyImmediate(beckmann);
                DestroyImmediate(phong);
                Debug.LogException(exception);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                controller.Upload();
            }
        }

        private static CubemapArray CreateCubemapArray(int resolution, int count, string name)
        {
            return new CubemapArray(resolution, count, TextureFormat.RGBAHalf, true, true)
            {
                name = name,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        private static RenderTexture CaptureProbe(ReflectionProbe probe, int resolution)
        {
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(resolution, resolution, GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormat.D24_UNorm_S8_UInt)
            {
                dimension = TextureDimension.Cube,
                useMipMap = false,
                autoGenerateMips = false,
                msaaSamples = 1
            };
            RenderTexture target = new RenderTexture(descriptor) { name = $"{probe.name} Looga Raw Capture" };
            target.Create();

            GameObject cameraObject = new GameObject("Looga Reflection Capture Camera") { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = probe.transform.TransformPoint(probe.center);
            camera.clearFlags = probe.clearFlags == ReflectionProbeClearFlags.Skybox
                ? CameraClearFlags.Skybox
                : CameraClearFlags.SolidColor;
            camera.backgroundColor = probe.backgroundColor;
            camera.cullingMask = probe.cullingMask;
            camera.nearClipPlane = probe.nearClipPlane;
            camera.farClipPlane = probe.farClipPlane;
            camera.allowHDR = probe.hdr;
            camera.useOcclusionCulling = true;
            camera.enabled = false;
            camera.RenderToCubemap(target);
            if (RenderTexture.active == target)
                RenderTexture.active = null;
            DestroyImmediate(cameraObject);
            return target;
        }

        private static void FilterProbe(ComputeShader compute, RenderTexture source, CubemapArray destination, int slice, int mipCount, string kernelName, int sampleCount)
        {
            int kernel = compute.FindKernel(kernelName);
            if (!compute.IsSupported(kernel))
                throw new InvalidOperationException($"Compute kernel {kernelName} is not supported by the active graphics device.");
            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(destination.width, destination.width, GraphicsFormat.R16G16B16A16_SFloat, 0)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = 6,
                enableRandomWrite = true,
                useMipMap = true,
                autoGenerateMips = false,
                msaaSamples = 1
            };
            RenderTexture filtered = new RenderTexture(descriptor) { name = $"{kernelName} Temporary" };
            filtered.Create();
            compute.SetTexture(kernel, "_SourceCubemap", source);
            compute.SetInt("_SampleCount", Mathf.Clamp(sampleCount, 32, 512));
            RenderTexture previous = RenderTexture.active;

            for (int mip = 0; mip < mipCount; mip++)
            {
                int size = Mathf.Max(destination.width >> mip, 1);
                float roughness = mipCount > 1 ? mip / (float)(mipCount - 1) : 0f;
                compute.SetInt("_OutputSize", size);
                compute.SetFloat("_Roughness", roughness);
                compute.SetFloat("_PhongExponent", Mathf.Max(2f / Mathf.Max(Mathf.Pow(roughness, 4f), 0.0001f) - 2f, 1f));
                compute.SetTexture(kernel, "_OutputCubeMip", filtered, mip);
                compute.Dispatch(kernel, Mathf.CeilToInt(size / 8f), Mathf.CeilToInt(size / 8f), 6);

                Texture2D readback = new Texture2D(size, size, TextureFormat.RGBAHalf, false, true);
                for (int face = 0; face < 6; face++)
                {
                    Graphics.SetRenderTarget(filtered, mip, CubemapFace.Unknown, face);
                    readback.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    readback.Apply(false, false);
                    destination.SetPixels(readback.GetPixels(), (CubemapFace)face, slice, mip);
                }
                DestroyImmediate(readback);
            }

            RenderTexture.active = previous;
            destination.Apply(false, false);
            filtered.Release();
            DestroyImmediate(filtered);
        }

        private static Texture2D BakeBrdfLut(ComputeShader compute, string kernelName, string textureName, int sampleCount)
        {
            const int size = 128;
            int kernel = compute.FindKernel(kernelName);
            if (!compute.IsSupported(kernel))
                throw new InvalidOperationException($"Compute kernel {kernelName} is not supported by the active graphics device.");
            RenderTexture target = new RenderTexture(size, size, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            target.Create();
            compute.SetInt("_OutputSize", size);
            compute.SetInt("_SampleCount", Mathf.Clamp(sampleCount, 32, 512));
            compute.SetTexture(kernel, "_OutputBrdfLut", target);
            compute.Dispatch(kernel, size / 8, size / 8, 1);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBAHalf, false, true)
            {
                name = textureName,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            texture.Apply(false, false);
            RenderTexture.active = previous;
            target.Release();
            DestroyImmediate(target);
            return texture;
        }

        private static void BuildAuxiliaryLightmaps(LoogaIndirectLightingController controller)
        {
            LoogaIndirectLightingData data = EnsureDataAsset(controller);
            if (data == null || data.auxiliarySources == null || data.auxiliarySources.Length == 0)
            {
                EditorUtility.DisplayDialog("Looga Lighting", "Add at least one Auxiliary Lightmap Source to the data asset first.", "OK");
                return;
            }

            Texture2D reference = data.auxiliarySources.Select(source => source.lobe0Radiance).FirstOrDefault(texture => texture != null);
            if (reference == null)
            {
                EditorUtility.DisplayDialog("Looga Lighting", "Each auxiliary set requires at least a Lobe 0 Radiance texture.", "OK");
                return;
            }

            int width = reference.width;
            int height = reference.height;
            int count = data.auxiliarySources.Length;
            Texture2DArray lobe0 = CreateTextureArray(width, height, count, "Looga Auxiliary Lobe 0");
            Texture2DArray lobe1 = CreateTextureArray(width, height, count, "Looga Auxiliary Lobe 1");
            Texture2DArray directions = CreateTextureArray(width, height, count, "Looga Auxiliary Directions");
            RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);

            try
            {
                for (int i = 0; i < count; i++)
                {
                    LoogaIndirectLightingData.AuxiliaryLightmapSource source = data.auxiliarySources[i];
                    CopyTextureToArray(source.lobe0Radiance, temporary, lobe0, i, Color.clear);
                    CopyTextureToArray(source.lobe1Radiance, temporary, lobe1, i, Color.clear);
                    CopyTextureToArray(source.directions, temporary, directions, i, new Color(0.5f, 1f, 0.5f, 1f));
                }

                ReplaceSubAsset(data, ref data.auxiliaryLobe0Array, lobe0);
                ReplaceSubAsset(data, ref data.auxiliaryLobe1Array, lobe1);
                ReplaceSubAsset(data, ref data.auxiliaryDirectionArray, directions);
                SaveData(data, controller);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static Texture2DArray CreateTextureArray(int width, int height, int count, string name)
        {
            return new Texture2DArray(width, height, count, TextureFormat.RGBAHalf, false, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
        }

        private static void CopyTextureToArray(Texture source, RenderTexture temporary, Texture2DArray destination, int slice, Color fallback)
        {
            if (source != null)
            {
                Graphics.Blit(source, temporary);
            }
            else
            {
                RenderTexture clearPrevious = RenderTexture.active;
                RenderTexture.active = temporary;
                GL.Clear(false, true, fallback);
                RenderTexture.active = clearPrevious;
            }
            RenderTexture readbackPrevious = RenderTexture.active;
            RenderTexture.active = temporary;
            Texture2D readback = new Texture2D(temporary.width, temporary.height, TextureFormat.RGBAHalf, false, true);
            readback.ReadPixels(new Rect(0, 0, temporary.width, temporary.height), 0, 0);
            readback.Apply(false, false);
            destination.SetPixels(readback.GetPixels(), slice, 0);
            destination.Apply(false, false);
            DestroyImmediate(readback);
            RenderTexture.active = readbackPrevious;
        }

        private static void BakeRadianceVolume(LoogaIndirectLightingController controller)
        {
            LoogaIndirectLightingData data = EnsureDataAsset(controller);
            if (data == null)
                return;

            Vector3Int resolution = Vector3Int.Max(controller.radianceBakeResolution, Vector3Int.one);
            int probeCount = resolution.x * resolution.y * resolution.z;
            Color[] lobe0 = new Color[probeCount];
            Color[] direction0 = new Color[probeCount];
            Color[] lobe1 = new Color[probeCount];
            Color[] direction1 = new Color[probeCount];
            Bounds bounds = controller.radianceBakeBounds;
            int captureResolution = Mathf.Clamp(controller.radianceCaptureResolution, 16, 128);

            RenderTextureDescriptor descriptor = new RenderTextureDescriptor(captureResolution, captureResolution, GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormat.D24_UNorm_S8_UInt)
            {
                dimension = TextureDimension.Cube,
                useMipMap = false,
                autoGenerateMips = false,
                msaaSamples = 1
            };
            RenderTexture cubemap = new RenderTexture(descriptor) { name = "Looga Radiance Probe Capture" };
            cubemap.Create();
            GameObject cameraObject = new GameObject("Looga Radiance Probe Camera") { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.allowHDR = true;
            camera.cullingMask = controller.radianceCaptureMask;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = Mathf.Max(bounds.size.magnitude * 2f, 100f);
            Shader.SetGlobalFloat("_LoogaRadianceProbeVolumeEnabled", 0f);

            try
            {
                for (int z = 0; z < resolution.z; z++)
                for (int y = 0; y < resolution.y; y++)
                for (int x = 0; x < resolution.x; x++)
                {
                    int index = x + resolution.x * (y + resolution.y * z);
                    EditorUtility.DisplayProgressBar("Looga Radiance Probe Volume", $"Capturing probe {index + 1} of {probeCount}", index / (float)probeCount);
                    Vector3 normalized = new Vector3((x + 0.5f) / resolution.x, (y + 0.5f) / resolution.y, (z + 0.5f) / resolution.z);
                    camera.transform.position = bounds.min + Vector3.Scale(normalized, bounds.size);
                    camera.RenderToCubemap(cubemap);
                    List<RadianceSample> samples = ReadCubemapSamples(cubemap, captureResolution);
                    FitTwoLobes(samples, out Vector3 firstDirection, out Color firstRadiance, out Vector3 secondDirection, out Color secondRadiance);
                    lobe0[index] = new Color(firstRadiance.r, firstRadiance.g, firstRadiance.b, 1f);
                    lobe1[index] = new Color(secondRadiance.r, secondRadiance.g, secondRadiance.b, 1f);
                    direction0[index] = new Color(firstDirection.x * 0.5f + 0.5f, firstDirection.y * 0.5f + 0.5f, firstDirection.z * 0.5f + 0.5f, 1f);
                    direction1[index] = new Color(secondDirection.x * 0.5f + 0.5f, secondDirection.y * 0.5f + 0.5f, secondDirection.z * 0.5f + 0.5f, 1f);
                }

                Texture3D lobe0Texture = CreateTexture3D(resolution, lobe0, "Looga Radiance Lobe 0");
                Texture3D direction0Texture = CreateTexture3D(resolution, direction0, "Looga Radiance Direction 0");
                Texture3D lobe1Texture = CreateTexture3D(resolution, lobe1, "Looga Radiance Lobe 1");
                Texture3D direction1Texture = CreateTexture3D(resolution, direction1, "Looga Radiance Direction 1");
                ReplaceSubAsset(data, ref data.radianceLobe0, lobe0Texture);
                ReplaceSubAsset(data, ref data.radianceDirection0, direction0Texture);
                ReplaceSubAsset(data, ref data.radianceLobe1, lobe1Texture);
                ReplaceSubAsset(data, ref data.radianceDirection1, direction1Texture);
                data.radianceProbeBounds = bounds;
                data.radianceProbeResolution = resolution;
                SaveData(data, controller);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                DestroyImmediate(cameraObject);
                if (RenderTexture.active == cubemap)
                    RenderTexture.active = null;
                cubemap.Release();
                DestroyImmediate(cubemap);
                controller.Upload();
            }
        }

        private static List<RadianceSample> ReadCubemapSamples(RenderTexture cubemap, int resolution)
        {
            List<RadianceSample> samples = new List<RadianceSample>(resolution * resolution * 6);
            Texture2D faceTexture = new Texture2D(resolution, resolution, TextureFormat.RGBAHalf, false, true);
            RenderTexture previous = RenderTexture.active;
            float solidAngle = 4f * Mathf.PI / (6f * resolution * resolution);

            for (int face = 0; face < 6; face++)
            {
                Graphics.SetRenderTarget(cubemap, 0, (CubemapFace)face);
                faceTexture.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                faceTexture.Apply(false, false);
                Color[] colors = faceTexture.GetPixels();
                for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    Color color = colors[x + y * resolution];
                    float luminance = Mathf.Max(color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f, 0f);
                    samples.Add(new RadianceSample
                    {
                        direction = FaceUvToDirection(face, new Vector2((x + 0.5f) / resolution, (y + 0.5f) / resolution)),
                        color = color,
                        weight = luminance * solidAngle
                    });
                }
            }

            RenderTexture.active = previous;
            DestroyImmediate(faceTexture);
            return samples;
        }

        private static void FitTwoLobes(List<RadianceSample> samples, out Vector3 direction0, out Color radiance0, out Vector3 direction1, out Color radiance1)
        {
            Vector3 weightedDirection = Vector3.zero;
            float totalWeight = 0f;
            foreach (RadianceSample sample in samples)
            {
                weightedDirection += sample.direction * sample.weight;
                totalWeight += sample.weight;
            }

            direction0 = weightedDirection.sqrMagnitude > 1e-6f ? weightedDirection.normalized : Vector3.up;
            Vector3 initialDirection = direction0;
            RadianceSample leastAligned = samples.OrderBy(sample => Vector3.Dot(sample.direction, initialDirection)).FirstOrDefault();
            direction1 = leastAligned.direction.sqrMagnitude > 0f ? leastAligned.direction : -direction0;

            for (int iteration = 0; iteration < 6; iteration++)
            {
                Vector3 sum0 = Vector3.zero;
                Vector3 sum1 = Vector3.zero;
                float weight0 = 0f;
                float weight1 = 0f;
                foreach (RadianceSample sample in samples)
                {
                    if (Vector3.Dot(sample.direction, direction0) >= Vector3.Dot(sample.direction, direction1))
                    {
                        sum0 += sample.direction * sample.weight;
                        weight0 += sample.weight;
                    }
                    else
                    {
                        sum1 += sample.direction * sample.weight;
                        weight1 += sample.weight;
                    }
                }
                if (weight0 > 1e-6f) direction0 = sum0.normalized;
                if (weight1 > 1e-6f) direction1 = sum1.normalized;
            }

            radiance0 = Color.clear;
            radiance1 = Color.clear;
            float sampleSolidAngle = 4f * Mathf.PI / Mathf.Max(samples.Count, 1);
            foreach (RadianceSample sample in samples)
            {
                Color contribution = sample.color * (sampleSolidAngle / Mathf.PI);
                if (Vector3.Dot(sample.direction, direction0) >= Vector3.Dot(sample.direction, direction1))
                    radiance0 += contribution;
                else
                    radiance1 += contribution;
            }
        }

        private static Vector3 FaceUvToDirection(int face, Vector2 uv)
        {
            Vector2 p = uv * 2f - Vector2.one;
            return face switch
            {
                0 => new Vector3(1f, -p.y, -p.x).normalized,
                1 => new Vector3(-1f, -p.y, p.x).normalized,
                2 => new Vector3(p.x, 1f, p.y).normalized,
                3 => new Vector3(p.x, -1f, -p.y).normalized,
                4 => new Vector3(p.x, -p.y, 1f).normalized,
                _ => new Vector3(-p.x, -p.y, -1f).normalized
            };
        }

        private static Texture3D CreateTexture3D(Vector3Int resolution, Color[] colors, string name)
        {
            Texture3D texture = new Texture3D(resolution.x, resolution.y, resolution.z, TextureFormat.RGBAHalf, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear
            };
            texture.SetPixels(colors);
            texture.Apply(false, false);
            return texture;
        }

        private static void ReplaceSubAsset<T>(LoogaIndirectLightingData owner, ref T current, T replacement) where T : UnityEngine.Object
        {
            if (current != null && AssetDatabase.IsSubAsset(current))
                DestroyImmediate(current, true);
            current = replacement;
            AssetDatabase.AddObjectToAsset(replacement, owner);
        }

        private static void SaveData(LoogaIndirectLightingData data, LoogaIndirectLightingController controller)
        {
            EditorUtility.SetDirty(data);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(data), ImportAssetOptions.ForceUpdate);
            controller.Upload();
        }
    }
}
