using System.Runtime.InteropServices;
using UnityEngine;

namespace LoogaSoft.Lighting
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("LoogaSoft/Lighting/Looga Indirect Lighting Controller")]
    public sealed class LoogaIndirectLightingController : MonoBehaviour
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct ReflectionProbeGpuData
        {
            public Vector4 centerAndBlend;
            public Vector4 extentsAndIntensity;
            public Vector4 capturePositionAndSlice;
            public Vector4 axisX;
            public Vector4 axisY;
            public Vector4 axisZ;
            public Vector4 options;
        }

        private const int MaxReflectionProbes = 32;
        private const int ReflectionProbeStride = sizeof(float) * 4 * 7;
        [Header("Runtime")]
        [Tooltip("Generated model-aware indirect-lighting resources for this scene.")]
        public LoogaIndirectLightingData data;
        [Tooltip("Use separately prefiltered GGX, Beckmann, and Phong reflection probe arrays and matching BRDF LUTs.")]
        public bool enableModelReflections = true;
        [Tooltip("Re-evaluate Unity directional lightmaps with the active Looga diffuse model.")]
        public bool enableDirectionalLightmapDecoding = true;
        [Tooltip("Add the optional two-lobe directional lightmaps supplied by Unity, Bakery, or another external baker.")]
        public bool enableAuxiliaryLightmaps = true;
        [Tooltip("Use the baked world-space two-lobe radiance grid for objects without lightmaps.")]
        public bool enableRadianceProbeVolume = true;

        [Header("Radiance Volume Baking")]
        public Bounds radianceBakeBounds = new Bounds(Vector3.zero, new Vector3(20f, 10f, 20f));
        public Vector3Int radianceBakeResolution = new Vector3Int(4, 2, 4);
        [Range(16, 128)] public int radianceCaptureResolution = 32;
        public LayerMask radianceCaptureMask = ~0;

        [Header("Reflection Baking")]
        [Range(32, 512)] public int reflectionBakeResolution = 128;
        [Range(32, 512)] public int reflectionSampleCount = 128;

        private static LoogaIndirectLightingController s_Active;
        private static GraphicsBuffer s_FallbackReflectionProbeBuffer;
        private GraphicsBuffer _reflectionProbeBuffer;

        private static readonly int ReflectionProbeCountId = Shader.PropertyToID("_LoogaReflectionProbeCount");
        private static readonly int ReflectionProbeDataId = Shader.PropertyToID("_LoogaReflectionProbeData");
        private static readonly int GgxReflectionArrayId = Shader.PropertyToID("_LoogaGGXReflectionArray");
        private static readonly int BeckmannReflectionArrayId = Shader.PropertyToID("_LoogaBeckmannReflectionArray");
        private static readonly int PhongReflectionArrayId = Shader.PropertyToID("_LoogaPhongReflectionArray");
        private static readonly int GgxBrdfLutId = Shader.PropertyToID("_LoogaGGXBrdfLut");
        private static readonly int BeckmannBrdfLutId = Shader.PropertyToID("_LoogaBeckmannBrdfLut");
        private static readonly int PhongBrdfLutId = Shader.PropertyToID("_LoogaPhongBrdfLut");
        private static readonly int ReflectionMipCountId = Shader.PropertyToID("_LoogaReflectionMipCount");
        private static readonly int ModelReflectionsEnabledId = Shader.PropertyToID("_LoogaModelReflectionsEnabled");
        private static readonly int DirectionalLightmapsEnabledId = Shader.PropertyToID("_LoogaDirectionalLightmapsEnabled");
        private static readonly int AuxiliaryLobe0Id = Shader.PropertyToID("_LoogaAuxiliaryLobe0Array");
        private static readonly int AuxiliaryLobe1Id = Shader.PropertyToID("_LoogaAuxiliaryLobe1Array");
        private static readonly int AuxiliaryDirectionsId = Shader.PropertyToID("_LoogaAuxiliaryDirectionArray");
        private static readonly int AuxiliaryCountId = Shader.PropertyToID("_LoogaAuxiliaryLightmapCount");
        private static readonly int AuxiliaryEnabledId = Shader.PropertyToID("_LoogaAuxiliaryLightmapsEnabled");
        private static readonly int RadianceLobe0Id = Shader.PropertyToID("_LoogaRadianceLobe0");
        private static readonly int RadianceDirection0Id = Shader.PropertyToID("_LoogaRadianceDirection0");
        private static readonly int RadianceLobe1Id = Shader.PropertyToID("_LoogaRadianceLobe1");
        private static readonly int RadianceDirection1Id = Shader.PropertyToID("_LoogaRadianceDirection1");
        private static readonly int RadianceBoundsMinId = Shader.PropertyToID("_LoogaRadianceBoundsMin");
        private static readonly int RadianceBoundsInvSizeId = Shader.PropertyToID("_LoogaRadianceBoundsInvSize");
        private static readonly int RadianceVolumeEnabledId = Shader.PropertyToID("_LoogaRadianceProbeVolumeEnabled");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGlobalState()
        {
            s_Active = null;
            ReleaseFallbackResources();
            DisableGlobalValues();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeGlobalResources()
        {
            EnsureGlobalsAreValid();
        }

        public static void EnsureGlobalsAreValid()
        {
            if (s_Active != null && s_Active.isActiveAndEnabled)
            {
                s_Active.EnsureReflectionProbeBufferIsBound();
                return;
            }

            DisableGlobals();
        }

        private void OnEnable()
        {
            s_Active = this;
            Upload();
        }

        private void OnDisable()
        {
            ReleaseBuffer();
            if (s_Active != this)
                return;

            s_Active = null;
            DisableGlobals();
        }

        private void OnValidate()
        {
            radianceBakeResolution = Vector3Int.Max(Vector3Int.one, radianceBakeResolution);
            if (isActiveAndEnabled)
                Upload();
        }

        private void LateUpdate()
        {
            if (s_Active == this)
                Upload();
        }

        public void Upload()
        {
            Shader.SetGlobalFloat(DirectionalLightmapsEnabledId, enableDirectionalLightmapDecoding ? 1f : 0f);
            UploadReflections();
            UploadAuxiliaryLightmaps();
            UploadRadianceVolume();
        }

        private void UploadReflections()
        {
            bool valid = enableModelReflections && data != null && data.HasReflectionData && SystemInfo.supportsCubemapArrayTextures;
            if (!valid)
            {
                Shader.SetGlobalFloat(ModelReflectionsEnabledId, 0f);
                Shader.SetGlobalInt(ReflectionProbeCountId, 0);
                ReleaseBuffer();
                BindFallbackReflectionProbeBuffer();
                return;
            }

            int count = Mathf.Min(data.reflectionProbes.Length, MaxReflectionProbes);
            ReflectionProbeGpuData[] gpuData = new ReflectionProbeGpuData[count];
            for (int i = 0; i < count; i++)
            {
                LoogaIndirectLightingData.ReflectionProbeRecord probe = data.reflectionProbes[i];
                Quaternion rotation = probe.rotation == default ? Quaternion.identity : probe.rotation;
                gpuData[i] = new ReflectionProbeGpuData
                {
                    centerAndBlend = new Vector4(probe.center.x, probe.center.y, probe.center.z, Mathf.Max(probe.blendDistance, 0.001f)),
                    extentsAndIntensity = new Vector4(probe.extents.x, probe.extents.y, probe.extents.z, probe.intensity),
                    capturePositionAndSlice = new Vector4(probe.capturePosition.x, probe.capturePosition.y, probe.capturePosition.z, probe.slice),
                    axisX = rotation * Vector3.right,
                    axisY = rotation * Vector3.up,
                    axisZ = rotation * Vector3.forward,
                    options = new Vector4(probe.boxProjection ? 1f : 0f, 0f, 0f, 0f)
                };
            }

            int stride = Marshal.SizeOf<ReflectionProbeGpuData>();
            if (_reflectionProbeBuffer == null || _reflectionProbeBuffer.count != count || _reflectionProbeBuffer.stride != stride)
            {
                ReleaseBuffer();
                _reflectionProbeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, stride);
            }

            _reflectionProbeBuffer.SetData(gpuData);
            Shader.SetGlobalFloat(ModelReflectionsEnabledId, 1f);
            Shader.SetGlobalInt(ReflectionProbeCountId, count);
            Shader.SetGlobalBuffer(ReflectionProbeDataId, _reflectionProbeBuffer);
            Shader.SetGlobalTexture(GgxReflectionArrayId, data.ggxReflectionProbes);
            Shader.SetGlobalTexture(BeckmannReflectionArrayId, data.beckmannReflectionProbes);
            Shader.SetGlobalTexture(PhongReflectionArrayId, data.phongReflectionProbes);
            Shader.SetGlobalTexture(GgxBrdfLutId, data.ggxBrdfLut);
            Shader.SetGlobalTexture(BeckmannBrdfLutId, data.beckmannBrdfLut);
            Shader.SetGlobalTexture(PhongBrdfLutId, data.phongBrdfLut);
            Shader.SetGlobalFloat(ReflectionMipCountId, data.ggxReflectionProbes.mipmapCount);
        }

        private void UploadAuxiliaryLightmaps()
        {
            bool valid = enableAuxiliaryLightmaps && data != null && data.HasAuxiliaryLightmaps && SystemInfo.supports2DArrayTextures;
            if (!valid)
            {
                Shader.SetGlobalFloat(AuxiliaryEnabledId, 0f);
                Shader.SetGlobalInt(AuxiliaryCountId, 0);
                return;
            }

            Shader.SetGlobalFloat(AuxiliaryEnabledId, 1f);
            Shader.SetGlobalTexture(AuxiliaryLobe0Id, data.auxiliaryLobe0Array);
            Shader.SetGlobalTexture(AuxiliaryLobe1Id, data.auxiliaryLobe1Array);
            Shader.SetGlobalTexture(AuxiliaryDirectionsId, data.auxiliaryDirectionArray);
            Shader.SetGlobalInt(AuxiliaryCountId, data.auxiliaryLobe0Array.depth);
        }

        private void UploadRadianceVolume()
        {
            bool valid = enableRadianceProbeVolume && data != null && data.HasRadianceVolume && SystemInfo.supports3DTextures;
            if (!valid)
            {
                Shader.SetGlobalFloat(RadianceVolumeEnabledId, 0f);
                return;
            }

            Bounds bounds = data.radianceProbeBounds;
            Vector3 size = bounds.size;
            Vector3 inverseSize = new Vector3(1f / Mathf.Max(size.x, 0.001f), 1f / Mathf.Max(size.y, 0.001f), 1f / Mathf.Max(size.z, 0.001f));
            Shader.SetGlobalFloat(RadianceVolumeEnabledId, 1f);
            Shader.SetGlobalTexture(RadianceLobe0Id, data.radianceLobe0);
            Shader.SetGlobalTexture(RadianceDirection0Id, data.radianceDirection0);
            Shader.SetGlobalTexture(RadianceLobe1Id, data.radianceLobe1);
            Shader.SetGlobalTexture(RadianceDirection1Id, data.radianceDirection1);
            Shader.SetGlobalVector(RadianceBoundsMinId, bounds.min);
            Shader.SetGlobalVector(RadianceBoundsInvSizeId, inverseSize);
        }

        private void ReleaseBuffer()
        {
            _reflectionProbeBuffer?.Dispose();
            _reflectionProbeBuffer = null;
        }

        private void EnsureReflectionProbeBufferIsBound()
        {
            if (_reflectionProbeBuffer != null)
            {
                Shader.SetGlobalBuffer(ReflectionProbeDataId, _reflectionProbeBuffer);
                return;
            }

            BindFallbackReflectionProbeBuffer();
        }

        private static void DisableGlobals()
        {
            DisableGlobalValues();
            BindFallbackReflectionProbeBuffer();
        }

        private static void DisableGlobalValues()
        {
            Shader.SetGlobalInt(ReflectionProbeCountId, 0);
            Shader.SetGlobalInt(AuxiliaryCountId, 0);
            Shader.SetGlobalFloat(DirectionalLightmapsEnabledId, 0f);
            Shader.SetGlobalFloat(ModelReflectionsEnabledId, 0f);
            Shader.SetGlobalFloat(AuxiliaryEnabledId, 0f);
            Shader.SetGlobalFloat(RadianceVolumeEnabledId, 0f);
        }

        private static void BindFallbackReflectionProbeBuffer()
        {
            if (s_FallbackReflectionProbeBuffer == null)
            {
                s_FallbackReflectionProbeBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    1,
                    ReflectionProbeStride);
                s_FallbackReflectionProbeBuffer.SetData(new ReflectionProbeGpuData[1]);
            }

            Shader.SetGlobalBuffer(ReflectionProbeDataId, s_FallbackReflectionProbeBuffer);
        }

        public static void ReleaseFallbackResources()
        {
            s_FallbackReflectionProbeBuffer?.Dispose();
            s_FallbackReflectionProbeBuffer = null;
        }
    }
}
