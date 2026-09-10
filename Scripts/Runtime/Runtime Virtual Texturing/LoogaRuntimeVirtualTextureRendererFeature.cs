using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace LoogaSoft.Rendering.VirtualTexturing
{
    [DisallowMultipleRendererFeature("Looga Runtime Virtual Texture")]
    public sealed class LoogaRuntimeVirtualTextureRendererFeature : ScriptableRendererFeature
    {
        private const string FeatureDisplayName = "Looga Runtime Virtual Texture";
        private const string WriterShaderName = "Hidden/LoogaSoft/Runtime Virtual Texture/Writer";

        public enum CameraScope
        {
            Game = 0,
            [InspectorName("Game And Scene View")]
            GameAndSceneView = 1
        }

        public enum CacheUpdateMode
        {
            [InspectorName("On Page Movement")]
            OnPageMovement = 0,
            EveryFrame = 1
        }

        [Header("Coverage")]
        [Range(1, LoogaRuntimeVirtualTextureMath.MaximumClipmapCount)]
        public int clipmapCount = 4;
        [Min(16f)] public float firstClipmapExtent = 128f;
        public LayerMask writerLayerMask = ~0;
        public CameraScope cameraScope = CameraScope.GameAndSceneView;

        [Header("Cache")]
        [Range(512, 4096)] public int atlasResolution = 2048;
        [Range(4, 64)] public int pagesPerClipmapAxis = 16;
        [Range(0.01f, 0.4f)] public float clipmapBlendWidth = 0.15f;
        public CacheUpdateMode updateMode = CacheUpdateMode.OnPageMovement;

        [Header("Height Encoding")]
        public float minimumWorldHeight = -512f;
        public float maximumWorldHeight = 2048f;

        [Header("Capture")]
        [Min(1f)] public float capturePadding = 64f;
        [Tooltip("Capture only opaque geometry. Transparent surfaces can read the cache but do not write to it.")]
        public bool includeAlphaTestedGeometry = true;

        [HideInInspector] public Shader writerShader;

        private LoogaRuntimeVirtualTexturePass _pass;
        private static uint _refreshVersion;

        /// <summary>
        /// Requests one cache rebuild for every camera during its next render.
        /// </summary>
        public static void RequestRefresh()
        {
            _refreshVersion++;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            name = FeatureDisplayName;
            clipmapCount = Mathf.Clamp(
                clipmapCount,
                1,
                LoogaRuntimeVirtualTextureMath.MaximumClipmapCount);
            atlasResolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(atlasResolution, 512, 4096));
            pagesPerClipmapAxis = Mathf.ClosestPowerOfTwo(Mathf.Clamp(pagesPerClipmapAxis, 4, 64));
            firstClipmapExtent = Mathf.Max(16f, firstClipmapExtent);
            maximumWorldHeight = Mathf.Max(minimumWorldHeight + 1f, maximumWorldHeight);

            if (writerShader == null)
                writerShader = Shader.Find(WriterShaderName);
        }
#endif

        public override void Create()
        {
            name = FeatureDisplayName;
            if (writerShader == null)
                writerShader = Shader.Find(WriterShaderName);

            _pass ??= new LoogaRuntimeVirtualTexturePass();
            _pass.renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses;
            Shader.SetGlobalInteger(LoogaRuntimeVirtualTextureShaderIds.Enabled, 0);
        }

        public override void AddRenderPasses(
            ScriptableRenderer renderer,
            ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (!isActive || writerShader == null || !ShouldRenderCamera(camera))
                return;

            _pass.Setup(this, writerShader, camera);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            Shader.SetGlobalInteger(LoogaRuntimeVirtualTextureShaderIds.Enabled, 0);
            _pass?.Dispose();
            _pass = null;
        }

        private bool ShouldRenderCamera(Camera camera)
        {
            if (camera == null || camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
                return false;

            if (camera.cameraType == CameraType.SceneView)
                return cameraScope == CameraScope.GameAndSceneView;

            return camera.cameraType == CameraType.Game;
        }

        private sealed class LoogaRuntimeVirtualTexturePass : ScriptableRenderPass
        {
            private static readonly ShaderTagId[] ShaderTags =
            {
                new("UniversalGBuffer"),
                new("UniversalForward"),
                new("UniversalForwardOnly"),
                new("SRPDefaultUnlit"),
                new("LightweightForward")
            };

            private readonly ProfilingSampler _profilingSampler = new("Looga Runtime Virtual Texture");
            private readonly List<ShaderTagId> _shaderTags = new(ShaderTags);
            private readonly Vector4[] _centerExtents = new Vector4[LoogaRuntimeVirtualTextureMath.MaximumClipmapCount];
            private readonly Vector4[] _atlasRects = new Vector4[LoogaRuntimeVirtualTextureMath.MaximumClipmapCount];
            private readonly Matrix4x4[] _viewMatrices = new Matrix4x4[LoogaRuntimeVirtualTextureMath.MaximumClipmapCount];
            private readonly Matrix4x4[] _projectionMatrices = new Matrix4x4[LoogaRuntimeVirtualTextureMath.MaximumClipmapCount];
            private readonly Dictionary<int, CameraResources> _cameraResources = new();

            private LoogaRuntimeVirtualTextureRendererFeature _settings;
            private Shader _writerShader;
            private Camera _camera;

            private sealed class CameraResources
            {
                public readonly Vector2[] Centers = new Vector2[LoogaRuntimeVirtualTextureMath.MaximumClipmapCount];
                public RTHandle AlbedoAtlas;
                public RTHandle NormalAtlas;
                public RTHandle HeightAtlas;
                public RTHandle DepthAtlas;
                public bool HasValidCenters;
                public int ConfigurationHash;
                public uint RefreshVersion;

                public void Release()
                {
                    AlbedoAtlas?.Release();
                    NormalAtlas?.Release();
                    HeightAtlas?.Release();
                    DepthAtlas?.Release();
                    AlbedoAtlas = null;
                    NormalAtlas = null;
                    HeightAtlas = null;
                    DepthAtlas = null;
                    HasValidCenters = false;
                    ConfigurationHash = 0;
                    RefreshVersion = 0;
                }
            }

            private sealed class PassData
            {
                public RendererListHandle RendererList;
                public int ClipmapCount;
                public int TileResolution;
                public float MinimumHeight;
                public float MaximumHeight;
                public float BlendWidth;
                public Vector4[] CenterExtents;
                public Vector4[] AtlasRects;
                public Matrix4x4[] ViewMatrices;
                public Matrix4x4[] ProjectionMatrices;
                public Matrix4x4 CameraView;
                public Matrix4x4 CameraProjection;
                public bool RebuildCache;
            }

            public void Dispose()
            {
                foreach (CameraResources resources in _cameraResources.Values)
                    resources.Release();

                _cameraResources.Clear();
            }

            public void Setup(
                LoogaRuntimeVirtualTextureRendererFeature settings,
                Shader writerShader,
                Camera camera)
            {
                _settings = settings;
                _writerShader = writerShader;
                _camera = camera;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_settings == null || _writerShader == null || _camera == null)
                    return;

                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                int clipmapCount = Mathf.Clamp(
                    _settings.clipmapCount,
                    1,
                    LoogaRuntimeVirtualTextureMath.MaximumClipmapCount);
                int atlasResolution = Mathf.ClosestPowerOfTwo(
                    Mathf.Clamp(_settings.atlasResolution, 512, 4096));
                int tileResolution = atlasResolution / 2;
                float minimumHeight = _settings.minimumWorldHeight;
                float maximumHeight = Mathf.Max(minimumHeight + 1f, _settings.maximumWorldHeight);

                CameraResources resources = GetCameraResources(_camera);
                bool resourcesChanged = EnsureResources(resources, atlasResolution);
                bool centerChanged = BuildClipmaps(
                    cameraData,
                    clipmapCount,
                    tileResolution,
                    minimumHeight,
                    maximumHeight,
                    resources);
                int configurationHash = GetConfigurationHash(clipmapCount, atlasResolution);
                bool configurationChanged = resources.ConfigurationHash != configurationHash;
                bool refreshRequested = resources.RefreshVersion != _refreshVersion;
                bool rebuildCache =
                    _settings.updateMode == CacheUpdateMode.EveryFrame ||
                    resourcesChanged ||
                    centerChanged ||
                    configurationChanged ||
                    refreshRequested ||
                    !resources.HasValidCenters;
                resources.HasValidCenters = true;
                resources.ConfigurationHash = configurationHash;
                resources.RefreshVersion = _refreshVersion;

                RendererListHandle rendererList = CreateRendererList(
                    renderGraph,
                    renderingData,
                    cameraData,
                    lightData);
                if (!rendererList.IsValid())
                    return;

                TextureHandle albedoAtlas = renderGraph.ImportTexture(resources.AlbedoAtlas);
                TextureHandle normalAtlas = renderGraph.ImportTexture(resources.NormalAtlas);
                TextureHandle heightAtlas = renderGraph.ImportTexture(resources.HeightAtlas);
                TextureHandle depthAtlas = renderGraph.ImportTexture(resources.DepthAtlas);

                using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(
                    "Looga Runtime Virtual Texture",
                    out PassData passData,
                    _profilingSampler);

                passData.RendererList = rendererList;
                passData.ClipmapCount = clipmapCount;
                passData.TileResolution = tileResolution;
                passData.MinimumHeight = minimumHeight;
                passData.MaximumHeight = maximumHeight;
                passData.BlendWidth = _settings.clipmapBlendWidth;
                passData.CenterExtents = _centerExtents;
                passData.AtlasRects = _atlasRects;
                passData.ViewMatrices = _viewMatrices;
                passData.ProjectionMatrices = _projectionMatrices;
                passData.CameraView = cameraData.GetViewMatrix();
                passData.CameraProjection = cameraData.GetProjectionMatrix();
                passData.RebuildCache = rebuildCache;

                builder.UseRendererList(rendererList);
                builder.SetRenderAttachment(albedoAtlas, 0, AccessFlags.Write);
                builder.SetRenderAttachment(normalAtlas, 1, AccessFlags.Write);
                builder.SetRenderAttachment(heightAtlas, 2, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depthAtlas, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(
                    albedoAtlas,
                    LoogaRuntimeVirtualTextureShaderIds.AlbedoAtlas);
                builder.SetGlobalTextureAfterPass(
                    normalAtlas,
                    LoogaRuntimeVirtualTextureShaderIds.NormalAtlas);
                builder.SetGlobalTextureAfterPass(
                    heightAtlas,
                    LoogaRuntimeVirtualTextureShaderIds.HeightAtlas);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalInteger(
                        LoogaRuntimeVirtualTextureShaderIds.Enabled,
                        0);
                    context.cmd.SetGlobalVector(
                        LoogaRuntimeVirtualTextureShaderIds.HeightRange,
                        new Vector4(
                            data.MinimumHeight,
                            data.MaximumHeight,
                            1f / (data.MaximumHeight - data.MinimumHeight),
                            data.BlendWidth));

                    if (data.RebuildCache)
                    {
                        float clearDepth = SystemInfo.usesReversedZBuffer ? 0f : 1f;
                        context.cmd.ClearRenderTarget(
                            RTClearFlags.All,
                            Color.clear,
                            clearDepth,
                            0);

                        for (int level = 0; level < data.ClipmapCount; level++)
                        {
                            int tileX = level & 1;
                            int tileY = level >> 1;
                            context.cmd.SetViewport(new Rect(
                                tileX * data.TileResolution,
                                tileY * data.TileResolution,
                                data.TileResolution,
                                data.TileResolution));
                            context.cmd.SetViewProjectionMatrices(
                                data.ViewMatrices[level],
                                data.ProjectionMatrices[level]);
                            context.cmd.DrawRendererList(data.RendererList);
                        }
                    }

                    context.cmd.SetViewProjectionMatrices(
                        data.CameraView,
                        data.CameraProjection);
                    context.cmd.SetGlobalVectorArray(
                        LoogaRuntimeVirtualTextureShaderIds.CenterExtents,
                        data.CenterExtents);
                    context.cmd.SetGlobalVectorArray(
                        LoogaRuntimeVirtualTextureShaderIds.AtlasRects,
                        data.AtlasRects);
                    context.cmd.SetGlobalInteger(
                        LoogaRuntimeVirtualTextureShaderIds.ClipmapCount,
                        data.ClipmapCount);
                    context.cmd.SetGlobalInteger(
                        LoogaRuntimeVirtualTextureShaderIds.Enabled,
                        1);
                });
            }

            private bool BuildClipmaps(
                UniversalCameraData cameraData,
                int clipmapCount,
                int tileResolution,
                float minimumHeight,
                float maximumHeight,
                CameraResources resources)
            {
                Vector3 cameraPosition = cameraData.worldSpaceCameraPos;
                float nearPlane = Mathf.Max(0.01f, _settings.capturePadding);
                float farPlane = maximumHeight - minimumHeight + nearPlane * 2f;
                bool centerChanged = false;

                for (int level = 0; level < LoogaRuntimeVirtualTextureMath.MaximumClipmapCount; level++)
                {
                    float extent = LoogaRuntimeVirtualTextureMath.GetExtent(
                        _settings.firstClipmapExtent,
                        Mathf.Min(level, clipmapCount - 1));
                    Vector2 center = LoogaRuntimeVirtualTextureMath.SnapCenter(
                        cameraPosition,
                        extent,
                        _settings.pagesPerClipmapAxis);
                    centerChanged |= resources.Centers[level] != center;
                    resources.Centers[level] = center;
                    _centerExtents[level] = new Vector4(
                        center.x,
                        center.y,
                        extent * 0.5f,
                        extent / tileResolution);
                    _atlasRects[level] = LoogaRuntimeVirtualTextureMath.GetAtlasRect(level);

                    Vector3 capturePosition = new(
                        center.x,
                        maximumHeight + nearPlane,
                        center.y);
                    Quaternion captureRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                    _viewMatrices[level] = Matrix4x4.TRS(
                        capturePosition,
                        captureRotation,
                        Vector3.one).inverse;
                    Matrix4x4 projection = Matrix4x4.Ortho(
                        -extent * 0.5f,
                        extent * 0.5f,
                        -extent * 0.5f,
                        extent * 0.5f,
                        nearPlane,
                        farPlane);
                    _projectionMatrices[level] = GL.GetGPUProjectionMatrix(projection, true);
                }

                return centerChanged;
            }

            private RendererListHandle CreateRendererList(
                RenderGraph renderGraph,
                UniversalRenderingData renderingData,
                UniversalCameraData cameraData,
                UniversalLightData lightData)
            {
                RenderQueueRange queueRange = _settings.includeAlphaTestedGeometry
                    ? RenderQueueRange.opaque
                    : new RenderQueueRange
                    {
                        lowerBound = (int)RenderQueue.Background,
                        upperBound = (int)RenderQueue.AlphaTest - 1
                    };
                FilteringSettings filteringSettings = new(
                    queueRange,
                    _settings.writerLayerMask);
                DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                    _shaderTags,
                    renderingData,
                    cameraData,
                    lightData,
                    SortingCriteria.CommonOpaque);
                drawingSettings.overrideShader = _writerShader;
                drawingSettings.overrideShaderPassIndex = 0;

                RendererListParams rendererListParams = new(
                    renderingData.cullResults,
                    drawingSettings,
                    filteringSettings);
                return renderGraph.CreateRendererList(rendererListParams);
            }

            private CameraResources GetCameraResources(Camera camera)
            {
                int cameraId = camera.GetInstanceID();
                if (_cameraResources.TryGetValue(cameraId, out CameraResources resources))
                    return resources;

                resources = new CameraResources();
                _cameraResources.Add(cameraId, resources);
                return resources;
            }

            private int GetConfigurationHash(int clipmapCount, int atlasResolution)
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + clipmapCount;
                    hash = hash * 31 + atlasResolution;
                    hash = hash * 31 + _settings.pagesPerClipmapAxis;
                    hash = hash * 31 + _settings.firstClipmapExtent.GetHashCode();
                    hash = hash * 31 + _settings.minimumWorldHeight.GetHashCode();
                    hash = hash * 31 + _settings.maximumWorldHeight.GetHashCode();
                    hash = hash * 31 + _settings.capturePadding.GetHashCode();
                    hash = hash * 31 + _settings.writerLayerMask.value;
                    hash = hash * 31 + (_settings.includeAlphaTestedGeometry ? 1 : 0);
                    return hash;
                }
            }

            private static bool EnsureResources(CameraResources resources, int resolution)
            {
                RenderTextureDescriptor colorDescriptor = new(resolution, resolution)
                {
                    graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm,
                    depthStencilFormat = GraphicsFormat.None,
                    msaaSamples = 1,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                bool changed = RenderingUtils.ReAllocateHandleIfNeeded(
                    ref resources.AlbedoAtlas,
                    colorDescriptor,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    name: "Looga RVT Albedo");
                changed |= RenderingUtils.ReAllocateHandleIfNeeded(
                    ref resources.NormalAtlas,
                    colorDescriptor,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    name: "Looga RVT Normal Material");
                changed |= RenderingUtils.ReAllocateHandleIfNeeded(
                    ref resources.HeightAtlas,
                    colorDescriptor,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    name: "Looga RVT Height Mask");

                RenderTextureDescriptor depthDescriptor = colorDescriptor;
                depthDescriptor.graphicsFormat = GraphicsFormat.None;
                depthDescriptor.depthStencilFormat = GraphicsFormat.D32_SFloat;
                changed |= RenderingUtils.ReAllocateHandleIfNeeded(
                    ref resources.DepthAtlas,
                    depthDescriptor,
                    FilterMode.Point,
                    TextureWrapMode.Clamp,
                    name: "Looga RVT Depth");
                return changed;
            }
        }
    }

    internal static class LoogaRuntimeVirtualTextureShaderIds
    {
        public static readonly int Enabled = Shader.PropertyToID("_LoogaRvtEnabled");
        public static readonly int ClipmapCount = Shader.PropertyToID("_LoogaRvtClipmapCount");
        public static readonly int CenterExtents = Shader.PropertyToID("_LoogaRvtCenterExtents");
        public static readonly int AtlasRects = Shader.PropertyToID("_LoogaRvtAtlasRects");
        public static readonly int HeightRange = Shader.PropertyToID("_LoogaRvtHeightRange");
        public static readonly int AlbedoAtlas = Shader.PropertyToID("_LoogaRvtAlbedoAtlas");
        public static readonly int NormalAtlas = Shader.PropertyToID("_LoogaRvtNormalAtlas");
        public static readonly int HeightAtlas = Shader.PropertyToID("_LoogaRvtHeightAtlas");
    }
}
