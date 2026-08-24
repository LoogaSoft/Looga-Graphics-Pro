using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LoogaSoft.Lighting
{
    [DisallowMultipleRendererFeature("Looga GTAO")]
    public sealed class LoogaGtaoRendererFeature : ScriptableRendererFeature
    {
        private const string FeatureDisplayName = "Looga GTAO";
        private const float MinRadiusPixels = 4.0f;
        private const float MaxRadiusPixels = 180.0f;

        public enum GtaoDebugMode
        {
            Off = 0,
            Occlusion = 1,
            OcclusionLoss = 2,
            BentNormal = 3,
            BentNormalDifference = 4,
            MaterialAO = 5,
            CombinedAO = 6
        }

        [InspectorName("Enable")]
        public bool enable = true;

        [InspectorName("Generate Bent Normals")]
        public bool generateBentNormals = true;

        [InspectorName("Debug Mode")]
        public GtaoDebugMode debugMode;

        [InspectorName("Radius"), Range(0.05f, 3.0f)]
        public float radius = 0.3f;

        [InspectorName("Intensity"), Range(0.0f, 6.0f)]
        public float intensity = 1.0f;

        [InspectorName("Slice Count"), Range(1, 8)]
        public int sliceCount = 3;

        [InspectorName("Step Count"), Range(2, 16)]
        public int stepCount = 8;

        [InspectorName("Direct Light Strength"), Range(0.0f, 1.0f)]
        public float directLightStrength = 0.5f;

        [InspectorName("Indirect Light Strength"), Range(0.0f, 1.0f)]
        public float indirectLightStrength = 1.0f;

        [InspectorName("Blur Radius"), Range(0, 4)]
        public int blurRadius = 2;

        [HideInInspector]
        public ComputeShader gtaoCompute;

        [HideInInspector]
        public ComputeShader blurCompute;

        private LoogaGtaoPass _pass;

        private static readonly int GtaoEnabledId = Shader.PropertyToID("_LoogaGTBNEnabled");
        private static readonly int DebugModeId = Shader.PropertyToID("_LoogaGTBNDebugMode");
        private static readonly int DirectStrengthId = Shader.PropertyToID("_GTBNDirectLightStrength");
        private static readonly int IndirectStrengthId = Shader.PropertyToID("_GTBNIndirectLightStrength");
        private static readonly int BentNormalsEnabledId = Shader.PropertyToID("_LoogaBentNormalsEnabled");

#if UNITY_EDITOR
        private void OnValidate()
        {
            bool needsSave = false;

            if (name != FeatureDisplayName)
            {
                name = FeatureDisplayName;
                needsSave = true;
            }

            AssignCompute(ref gtaoCompute, "LoogaGTBN", ref needsSave);
            AssignCompute(ref blurCompute, "LoogaGTBNBlur", ref needsSave);

            if (needsSave)
                EditorUtility.SetDirty(this);
        }

        private static void AssignCompute(
            ref ComputeShader compute,
            string assetName,
            ref bool needsSave)
        {
            if (compute != null)
                return;

            string[] guids = AssetDatabase.FindAssets($"{assetName} t:ComputeShader");
            if (guids.Length == 0)
                return;

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            needsSave = compute != null;
        }
#endif

        public override void Create()
        {
            name = FeatureDisplayName;
            _pass ??= new LoogaGtaoPass();
        }

        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            CameraType cameraType = renderingData.cameraData.cameraType;
            bool supportedCamera = cameraType == CameraType.Game || cameraType == CameraType.SceneView;

            if (!supportedCamera)
                return;

            if (!isActive || !enable ||
                !IsDeferredPlusRenderer(renderer) ||
                gtaoCompute == null || blurCompute == null)
            {
                DisableGlobals();
                return;
            }

            Shader.SetGlobalInteger(GtaoEnabledId, 1);
            Shader.SetGlobalInteger(DebugModeId, (int)debugMode);
            Shader.SetGlobalFloat(DirectStrengthId, directLightStrength);
            Shader.SetGlobalFloat(IndirectStrengthId, indirectLightStrength);
            Shader.SetGlobalInteger(BentNormalsEnabledId, generateBentNormals ? 1 : 0);

            _pass ??= new LoogaGtaoPass();
            _pass.Setup(
                gtaoCompute,
                blurCompute,
                this,
                UsesAccurateGBufferNormals(renderer));
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            DisableGlobals();
            _pass = null;
            base.Dispose(disposing);
        }

        private static void DisableGlobals()
        {
            Shader.SetGlobalInteger(GtaoEnabledId, 0);
            Shader.SetGlobalInteger(DebugModeId, 0);
            Shader.SetGlobalFloat(DirectStrengthId, 0.0f);
            Shader.SetGlobalFloat(IndirectStrengthId, 0.0f);
            Shader.SetGlobalInteger(BentNormalsEnabledId, 0);
        }

        private static bool UsesAccurateGBufferNormals(ScriptableRenderer renderer)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = renderer?.GetType().GetProperty("accurateGbufferNormals", flags);

            return property != null && property.PropertyType == typeof(bool) &&
                   (bool)property.GetValue(renderer);
        }

        private static bool IsDeferredPlusRenderer(ScriptableRenderer renderer)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = renderer?.GetType().GetProperty("renderingModeActual", flags);
            object value = property?.GetValue(renderer);

            return value != null && value.ToString() == "DeferredPlus";
        }

        private sealed class LoogaGtaoPass : ScriptableRenderPass
        {
            private ComputeShader _gtaoCompute;
            private ComputeShader _blurCompute;
            private LoogaGtaoRendererFeature _feature;
            private bool _useAccurateGBufferNormals;
            private int _gtaoKernel;
            private int _blurHorizontalKernel;
            private int _blurVerticalKernel;

            private static readonly int GtaoTextureId = Shader.PropertyToID("_GTBNTexture");
            private static readonly int GBufferNormalsAreOctId = Shader.PropertyToID("_LoogaGBufferNormalsAreOct");
            private static readonly int BentNormalsEnabledId = Shader.PropertyToID("_LoogaBentNormalsEnabled");

            public LoogaGtaoPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingDeferredLights - 1;
            }

            public void Setup(
                ComputeShader gtaoCompute,
                ComputeShader blurCompute,
                LoogaGtaoRendererFeature feature,
                bool useAccurateGBufferNormals)
            {
                _gtaoCompute = gtaoCompute;
                _blurCompute = blurCompute;
                _feature = feature;
                _useAccurateGBufferNormals = useAccurateGBufferNormals;

                _gtaoKernel = _gtaoCompute.FindKernel("CSMain");
                _blurHorizontalKernel = _blurCompute.FindKernel("BlurHorizontal");
                _blurVerticalKernel = _blurCompute.FindKernel("BlurVertical");
            }

            public override void RecordRenderGraph(
                RenderGraph renderGraph,
                ContextContainer frameData)
            {
                if (_gtaoCompute == null || _blurCompute == null)
                    return;

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                RenderTextureDescriptor descriptor = cameraData.cameraTargetDescriptor;
                descriptor.colorFormat = RenderTextureFormat.ARGBHalf;
                descriptor.depthBufferBits = 0;
                descriptor.enableRandomWrite = true;

                TextureHandle gtaoTarget = renderGraph.CreateTexture(
                    new TextureDesc(descriptor)
                    {
                        name = "Looga GTAO Target",
                        enableRandomWrite = true
                    });
                TextureHandle blurPingPong = renderGraph.CreateTexture(
                    new TextureDesc(descriptor)
                    {
                        name = "Looga GTAO Blur Ping Pong",
                        enableRandomWrite = true
                    });

                using IComputeRenderGraphBuilder builder = renderGraph.AddComputePass<PassData>(
                    "Compute Looga GTAO",
                    out PassData passData);

                passData.depthTexture = resourceData.activeDepthTexture;
                passData.normalsTexture = resourceData.gBuffer[2];
                passData.gtaoTarget = gtaoTarget;
                passData.blurPingPong = blurPingPong;

                Matrix4x4 view = cameraData.camera.worldToCameraMatrix;
                Matrix4x4 projection = cameraData.camera.projectionMatrix;
                passData.projectionScale =
                    0.5f * cameraData.cameraTargetDescriptor.height * projection.m11;
                passData.targetWidth = cameraData.cameraTargetDescriptor.width;
                passData.targetHeight = cameraData.cameraTargetDescriptor.height;

                if (passData.depthTexture.IsValid())
                    builder.UseTexture(passData.depthTexture, AccessFlags.Read);
                if (passData.normalsTexture.IsValid())
                    builder.UseTexture(passData.normalsTexture, AccessFlags.Read);

                builder.UseTexture(passData.gtaoTarget, AccessFlags.ReadWrite);
                builder.UseTexture(passData.blurPingPong, AccessFlags.ReadWrite);
                builder.SetGlobalTextureAfterPass(gtaoTarget, GtaoTextureId);
                builder.AllowGlobalStateModification(true);

                Matrix4x4 inverseProjection = projection.inverse;
                Vector3 GetViewRay(float ndcX, float ndcY)
                {
                    Vector4 viewPosition =
                        inverseProjection * new Vector4(ndcX, ndcY, 0.0f, 1.0f);
                    Vector3 ray = new Vector3(
                        viewPosition.x,
                        viewPosition.y,
                        viewPosition.z) / viewPosition.w;
                    return ray / -ray.z;
                }

                Vector3 bottomLeft = GetViewRay(-1.0f, -1.0f);
                passData.bottomLeftCorner = bottomLeft;
                passData.xExtent = GetViewRay(1.0f, -1.0f) - bottomLeft;
                passData.yExtent = GetViewRay(-1.0f, 1.0f) - bottomLeft;
                passData.viewMatrix = view;
                passData.inverseViewMatrix = cameraData.camera.cameraToWorldMatrix;
                passData.useAccurateGBufferNormals = _useAccurateGBufferNormals;
                passData.generateBentNormals = _feature.generateBentNormals;

                builder.SetRenderFunc((PassData data, ComputeGraphContext context) =>
                {
                    ComputeCommandBuffer commandBuffer = context.cmd;
                    int threadGroupsX = Mathf.CeilToInt(data.targetWidth / 8.0f);
                    int threadGroupsY = Mathf.CeilToInt(data.targetHeight / 8.0f);

                    commandBuffer.SetComputeMatrixParam(
                        _gtaoCompute,
                        "_ViewMatrix",
                        data.viewMatrix);
                    commandBuffer.SetComputeMatrixParam(
                        _gtaoCompute,
                        "_InvViewMatrix",
                        data.inverseViewMatrix);
                    commandBuffer.SetComputeVectorParam(
                        _gtaoCompute,
                        "_GTBNParams1",
                        new Vector4(
                            _feature.radius,
                            MaxRadiusPixels,
                            _feature.sliceCount,
                            _feature.stepCount));
                    commandBuffer.SetComputeVectorParam(
                        _gtaoCompute,
                        "_GTBNParams2",
                        new Vector4(
                            _feature.intensity,
                            0.0f,
                            data.projectionScale,
                            MinRadiusPixels));
                    commandBuffer.SetComputeIntParam(
                        _gtaoCompute,
                        GBufferNormalsAreOctId,
                        data.useAccurateGBufferNormals ? 1 : 0);
                    commandBuffer.SetComputeIntParam(
                        _gtaoCompute,
                        BentNormalsEnabledId,
                        data.generateBentNormals ? 1 : 0);

                    if (data.depthTexture.IsValid())
                        commandBuffer.SetGlobalTexture("_CameraDepthTexture", data.depthTexture);
                    if (data.normalsTexture.IsValid())
                        commandBuffer.SetGlobalTexture("_GBuffer2", data.normalsTexture);

                    commandBuffer.SetComputeVectorParam(
                        _gtaoCompute,
                        "_CameraViewBottomLeftCorner",
                        data.bottomLeftCorner);
                    commandBuffer.SetComputeVectorParam(
                        _gtaoCompute,
                        "_CameraViewXExtent",
                        data.xExtent);
                    commandBuffer.SetComputeVectorParam(
                        _gtaoCompute,
                        "_CameraViewYExtent",
                        data.yExtent);
                    commandBuffer.SetComputeTextureParam(
                        _gtaoCompute,
                        _gtaoKernel,
                        "_RW_GTBNTarget",
                        data.gtaoTarget);
                    commandBuffer.DispatchCompute(
                        _gtaoCompute,
                        _gtaoKernel,
                        threadGroupsX,
                        threadGroupsY,
                        1);

                    commandBuffer.SetComputeFloatParam(
                        _blurCompute,
                        "_BlurRadius",
                        _feature.blurRadius);
                    commandBuffer.SetComputeVectorParam(
                        _blurCompute,
                        "_BlurDirection",
                        new Vector2(1.0f, 0.0f));
                    commandBuffer.SetComputeIntParam(
                        _blurCompute,
                        GBufferNormalsAreOctId,
                        data.useAccurateGBufferNormals ? 1 : 0);
                    commandBuffer.SetComputeTextureParam(
                        _blurCompute,
                        _blurHorizontalKernel,
                        "_SourceTex",
                        data.gtaoTarget);
                    commandBuffer.SetComputeTextureParam(
                        _blurCompute,
                        _blurHorizontalKernel,
                        "_RW_BlurTarget",
                        data.blurPingPong);
                    commandBuffer.DispatchCompute(
                        _blurCompute,
                        _blurHorizontalKernel,
                        threadGroupsX,
                        threadGroupsY,
                        1);

                    commandBuffer.SetComputeVectorParam(
                        _blurCompute,
                        "_BlurDirection",
                        new Vector2(0.0f, 1.0f));
                    commandBuffer.SetComputeTextureParam(
                        _blurCompute,
                        _blurVerticalKernel,
                        "_SourceTex",
                        data.blurPingPong);
                    commandBuffer.SetComputeTextureParam(
                        _blurCompute,
                        _blurVerticalKernel,
                        "_RW_BlurTarget",
                        data.gtaoTarget);
                    commandBuffer.DispatchCompute(
                        _blurCompute,
                        _blurVerticalKernel,
                        threadGroupsX,
                        threadGroupsY,
                        1);
                });
            }

            private sealed class PassData
            {
                public TextureHandle depthTexture;
                public TextureHandle normalsTexture;
                public TextureHandle gtaoTarget;
                public TextureHandle blurPingPong;
                public Vector4 bottomLeftCorner;
                public Vector4 xExtent;
                public Vector4 yExtent;
                public Matrix4x4 viewMatrix;
                public Matrix4x4 inverseViewMatrix;
                public float projectionScale;
                public int targetWidth;
                public int targetHeight;
                public bool useAccurateGBufferNormals;
                public bool generateBentNormals;
            }
        }
    }
}
