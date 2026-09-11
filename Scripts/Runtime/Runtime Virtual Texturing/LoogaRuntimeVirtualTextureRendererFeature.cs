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

        [Tooltip("Assign terrain clipmap writers exported from matching native surface producers.")]
        public Material[] terrainWriters = System.Array.Empty<Material>();
        [HideInInspector] public Shader writerShader;
        [HideInInspector] public Shader clearShader;

        private LoogaRuntimeVirtualTexturePass _pass;
        private bool _callbacksRegistered;
        private static uint _refreshVersion;
        private static event System.Action<Bounds> RegionChanged;
        private static event System.Action<Renderer> WriterChanged;
        private static event System.Action DiscoveryChanged;

        /// <summary>Requests updates only where these world bounds intersect a camera cache.</summary>
        public static void RequestRefresh(Bounds bounds)
        {
            RegionChanged?.Invoke(bounds);
        }

        /// <summary>Updates a writer's spatial entry and refreshes its previous and current bounds.</summary>
        public static void NotifyWriterChanged(Renderer renderer, Bounds previousBounds)
        {
            WriterChanged?.Invoke(renderer);
            RequestRefresh(previousBounds);
            if (renderer)
            {
                RequestRefresh(renderer.bounds);
            }
        }

        /// <summary>
        /// Requests one cache rebuild for every camera during its next render.
        /// </summary>
        public static void RequestRefresh()
        {
            _refreshVersion++;
            DiscoveryChanged?.Invoke();
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

            if (!clearShader)
            {
                clearShader = Shader.Find("Hidden/LoogaSoft/Runtime Virtual Texture/Clear");
            }
            _pass ??= new LoogaRuntimeVirtualTexturePass();
            _pass.renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses;
            RegisterCallbacks();
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
            UnregisterCallbacks();
            Shader.SetGlobalInteger(LoogaRuntimeVirtualTextureShaderIds.Enabled, 0);
            Shader.SetGlobalInteger(LoogaRuntimeVirtualTextureShaderIds.ClipmapCount, 0);
            Shader.SetGlobalTexture(LoogaRuntimeVirtualTextureShaderIds.AlbedoAtlas, Texture2D.blackTexture);
            Shader.SetGlobalTexture(LoogaRuntimeVirtualTextureShaderIds.NormalAtlas, Texture2D.blackTexture);
            Shader.SetGlobalTexture(LoogaRuntimeVirtualTextureShaderIds.HeightAtlas, Texture2D.blackTexture);
            _pass?.Dispose();
            _pass = null;
        }

        private void RegisterCallbacks()
        {
            if (_callbacksRegistered)
                return;

            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            TerrainCallbacks.heightmapChanged += OnTerrainHeightChanged;
            TerrainCallbacks.textureChanged += OnTerrainTextureChanged;

            RegionChanged += OnRegionChanged;
            WriterChanged += OnWriterChanged;
            DiscoveryChanged += OnDiscoveryChanged;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.hierarchyChanged += OnHierarchyChanged;
#endif
            _callbacksRegistered = true;
        }

        private void UnregisterCallbacks()
        {
            if (!_callbacksRegistered)
                return;

            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            TerrainCallbacks.heightmapChanged -= OnTerrainHeightChanged;
            TerrainCallbacks.textureChanged -= OnTerrainTextureChanged;

            RegionChanged -= OnRegionChanged;
            WriterChanged -= OnWriterChanged;
            DiscoveryChanged -= OnDiscoveryChanged;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.hierarchyChanged -= OnHierarchyChanged;
#endif
            _callbacksRegistered = false;
        }

        private void OnRegionChanged(Bounds bounds) => _pass?.InvalidateRegion(bounds);
        private void OnWriterChanged(Renderer renderer) => _pass?.UpdateWriter(renderer);
        private void OnDiscoveryChanged() => _pass?.InvalidateDiscovery();
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => RequestRefresh();
        private void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene) => RequestRefresh();
#if UNITY_EDITOR
        private void OnHierarchyChanged() => RequestRefresh();
#endif

        private void OnTerrainHeightChanged(UnityEngine.Terrain terrain, RectInt region, bool synchronized)
        {
            if (isActive && terrain && terrain.terrainData)
            {
                RefreshTerrainRegion(terrain, region, terrain.terrainData.heightmapResolution - 1);
            }
        }

        private void OnTerrainTextureChanged(UnityEngine.Terrain terrain, string textureName, RectInt region, bool synchronized)
        {
            if (isActive && terrain && terrain.terrainData)
            {
                int resolution = textureName == TerrainData.HolesTextureName
                    ? terrain.terrainData.holesResolution : terrain.terrainData.alphamapResolution;
                RefreshTerrainRegion(terrain, region, resolution);
            }
        }

        private void RefreshTerrainRegion(UnityEngine.Terrain terrain, RectInt region, int resolution)
        {
            Vector3 size = terrain.terrainData.size;
            Vector3 origin = terrain.transform.position;
            Vector3 minimum = origin + new Vector3((region.xMin - 2f) / resolution * size.x, 0,
                (region.yMin - 2f) / resolution * size.z);
            Vector3 maximum = origin + new Vector3((region.xMax + 2f) / resolution * size.x, size.y,
                (region.yMax + 2f) / resolution * size.z);
            _pass?.InvalidateRegion(new Bounds((minimum + maximum) * 0.5f, maximum - minimum));
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            Shader.SetGlobalInteger(LoogaRuntimeVirtualTextureShaderIds.Enabled, 0);
        }

        /// <summary>Reports rebuild count and the latest mesh collection and bounds-filter cost for a camera.</summary>
        public bool TryGetStatistics(Camera camera, out int captures, out double cullMilliseconds)
        {
            captures = 0;
            cullMilliseconds = 0;
            return _pass != null && _pass.TryGetStatistics(camera, out captures, out cullMilliseconds);
        }

        /// <summary>Gets the latest update area and spatial-index workload for a camera.</summary>
        public bool TryGetUpdateStatistics(Camera camera, out int updatedTexels, out int copiedTexels,
            out int discoveries, out int candidates)
        {
            updatedTexels = copiedTexels = discoveries = candidates = 0;
            return _pass != null && _pass.TryGetUpdateStatistics(camera, out updatedTexels, out copiedTexels,
                out discoveries, out candidates);
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
            private readonly ProfilingSampler _profilingSampler = new("Looga Runtime Virtual Texture");

            private readonly Vector4[] _centerExtents = new Vector4[LoogaRuntimeVirtualTextureMath.MaximumClipmapCount];
            private readonly Vector4[] _atlasRects = new Vector4[LoogaRuntimeVirtualTextureMath.MaximumClipmapCount];
            private readonly Matrix4x4[] _viewMatrices = new Matrix4x4[LoogaRuntimeVirtualTextureMath.MaximumClipmapCount];
            private readonly Matrix4x4[] _projectionMatrices = new Matrix4x4[LoogaRuntimeVirtualTextureMath.MaximumClipmapCount];
            private readonly Dictionary<int, CameraResources> _cameraResources = new();
            private readonly List<int> _expiredCameras = new();

            private LoogaRuntimeVirtualTextureRendererFeature _settings;
            private readonly LoogaRuntimeVirtualTextureTerrainCapture _terrainCapture = new();
            private readonly LoogaRuntimeVirtualTextureMeshCapture _meshCapture = new();
            private Shader _writerShader;
            private Camera _camera;
            private Material _clearMaterial;
            private readonly Vector2[] _previousCenters = new Vector2[4];
            private readonly List<UpdateRegion> _updates = new();
            private readonly List<Bounds> _updateBounds = new();
            private readonly List<ShiftRegion> _shifts = new();

            private struct UpdateRegion
            {
                public int Level;
                public RectInt Pixels;
            }

            private struct ShiftRegion
            {
                public RectInt Source;
                public Vector2Int Destination;
            }

            private sealed class ShiftData
            {
                public RenderTexture[] Atlases;
                public RenderTexture Scratch;
                public ShiftRegion[] Shifts;
            }

            private sealed class CameraResources
            {
                public Camera Owner;
                public readonly Vector2[] Centers = new Vector2[LoogaRuntimeVirtualTextureMath.MaximumClipmapCount];
                public RTHandle AlbedoAtlas;
                public RTHandle NormalAtlas;
                public RTHandle HeightAtlas;
                public RTHandle DepthAtlas;
                public RTHandle Scratch;
                public readonly List<Bounds> DirtyRegions = new();
                public bool FullDirty;
                public int UpdatedTexels;
                public int CopiedTexels;
                public bool HasValidCenters;
                public int ConfigurationHash;
                public uint RefreshVersion;
                public int CaptureCount;
                public double LastCullMilliseconds;

                public void Release()
                {
                    AlbedoAtlas?.Release();
                    NormalAtlas?.Release();
                    HeightAtlas?.Release();
                    DepthAtlas?.Release();
                    Scratch?.Release();
                    Scratch = null;
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
                public UpdateRegion[] Updates;
                public Material ClearMaterial;
                public LoogaRuntimeVirtualTextureTerrainCapture.Draw[] Terrains;

                public LoogaRuntimeVirtualTextureMeshCapture.Draw[] Meshes;
            }

            public void Dispose()
            {
                foreach (CameraResources resources in _cameraResources.Values)
                    resources.Release();

                _cameraResources.Clear();
                _terrainCapture.Dispose();
                _meshCapture.Dispose();
                CoreUtils.Destroy(_clearMaterial);
            }

            public void InvalidateDiscovery() => _meshCapture.InvalidateDiscovery();
            public void UpdateWriter(Renderer renderer) => _meshCapture.UpdateRenderer(renderer);

            public void InvalidateRegion(Bounds bounds)
            {
                foreach (CameraResources resources in _cameraResources.Values)
                {
                    if (resources.DirtyRegions.Count >= 32)
                    {
                        resources.FullDirty = true;
                        resources.DirtyRegions.Clear();
                    }
                    else if (!resources.FullDirty)
                    {
                        resources.DirtyRegions.Add(bounds);
                    }
                }
            }

            public void Setup(
                LoogaRuntimeVirtualTextureRendererFeature settings,
                Shader writerShader,
                Camera camera)
            {
                _settings = settings;
                _writerShader = writerShader;
                _camera = camera;
                if (!_clearMaterial && settings.clearShader)
                {
                    _clearMaterial = CoreUtils.CreateEngineMaterial(settings.clearShader);
                }
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_settings == null || _writerShader == null || _camera == null || !_clearMaterial)
                    return;

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

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
                Shader.SetGlobalTexture(
                    LoogaRuntimeVirtualTextureShaderIds.AlbedoAtlas,
                    resources.AlbedoAtlas);
                Shader.SetGlobalTexture(
                    LoogaRuntimeVirtualTextureShaderIds.NormalAtlas,
                    resources.NormalAtlas);
                Shader.SetGlobalTexture(
                    LoogaRuntimeVirtualTextureShaderIds.HeightAtlas,
                    resources.HeightAtlas);
                System.Array.Copy(resources.Centers, _previousCenters, 4);
                BuildClipmaps(
                    cameraData,
                    clipmapCount,
                    tileResolution,
                    minimumHeight,
                    maximumHeight,
                    resources);
                int configurationHash = GetConfigurationHash(clipmapCount, atlasResolution);
                bool configurationChanged = resources.ConfigurationHash != configurationHash;
                bool refreshRequested = resources.RefreshVersion != _refreshVersion;
                bool fullUpdate = _settings.updateMode == CacheUpdateMode.EveryFrame || resourcesChanged
                    || configurationChanged || refreshRequested || !resources.HasValidCenters || resources.FullDirty;
                BuildUpdates(resources, clipmapCount, tileResolution, fullUpdate);
                bool rebuildCache = _updates.Count > 0;
                if (rebuildCache)
                {
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    _updateBounds.Clear();
                    foreach (UpdateRegion update in _updates)
                    {
                        _updateBounds.Add(UpdateBounds(update, _centerExtents[update.Level], tileResolution,
                            minimumHeight, maximumHeight));
                    }
                    _meshCapture.Prepare(_writerShader, _settings.writerLayerMask.value & _camera.cullingMask,
                        _centerExtents[clipmapCount - 1], minimumHeight, maximumHeight,
                        _settings.includeAlphaTestedGeometry, _camera.scene, _updateBounds);
                    resources.LastCullMilliseconds = timer.Elapsed.TotalMilliseconds;
                    resources.CaptureCount++;
                }
                TextureHandle albedoAtlas = renderGraph.ImportTexture(resources.AlbedoAtlas);
                TextureHandle normalAtlas = renderGraph.ImportTexture(resources.NormalAtlas);
                TextureHandle heightAtlas = renderGraph.ImportTexture(resources.HeightAtlas);
                TextureHandle depthAtlas = renderGraph.ImportTexture(resources.DepthAtlas);
                if (_shifts.Count > 0)
                {
                    TextureHandle scratch = renderGraph.ImportTexture(resources.Scratch);
                    using var shiftBuilder = renderGraph.AddUnsafePass<ShiftData>("Looga RVT scroll", out var shiftData);
                    shiftData.Atlases = new[] { resources.AlbedoAtlas.rt, resources.NormalAtlas.rt, resources.HeightAtlas.rt };
                    shiftData.Scratch = resources.Scratch.rt;
                    shiftData.Shifts = _shifts.ToArray();
                    shiftBuilder.UseTexture(albedoAtlas, AccessFlags.ReadWrite);
                    shiftBuilder.UseTexture(normalAtlas, AccessFlags.ReadWrite);
                    shiftBuilder.UseTexture(heightAtlas, AccessFlags.ReadWrite);
                    shiftBuilder.UseTexture(scratch, AccessFlags.ReadWrite);
                    shiftBuilder.AllowPassCulling(false);
                    shiftBuilder.SetRenderFunc(static (ShiftData data, UnsafeGraphContext context) =>
                    {
                        var command = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        foreach (ShiftRegion shift in data.Shifts)
                        {
                            foreach (RenderTexture atlas in data.Atlases)
                            {
                                RectInt source = shift.Source;
                                command.CopyTexture(atlas, 0, 0, source.x, source.y, source.width, source.height,
                                    data.Scratch, 0, 0, 0, 0);
                                command.CopyTexture(data.Scratch, 0, 0, 0, 0, source.width, source.height,
                                    atlas, 0, 0, shift.Destination.x, shift.Destination.y);
                            }
                        }
                    });
                }

                using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(
                    "Looga Runtime Virtual Texture",
                    out PassData passData,
                    _profilingSampler);

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
                // SetViewProjectionMatrices applies the graphics API conversion.
                passData.CameraProjection = cameraData.GetProjectionMatrix();
                passData.RebuildCache = rebuildCache;
                passData.Updates = _updates.ToArray();
                passData.ClearMaterial = _clearMaterial;
                if (rebuildCache)
                {
                    _terrainCapture.Prepare(_settings.terrainWriters ?? System.Array.Empty<Material>(),
                        _settings.writerLayerMask.value & _camera.cullingMask, _centerExtents[clipmapCount - 1], _camera.scene);
                    passData.Terrains = _terrainCapture.Draws.ToArray();
                    passData.Meshes = _meshCapture.Draws.ToArray();
                }
                resources.HasValidCenters = true;
                resources.ConfigurationHash = configurationHash;
                resources.RefreshVersion = _refreshVersion;
                resources.DirtyRegions.Clear();
                resources.FullDirty = false;

                if (rebuildCache)
                {
                    builder.SetRenderAttachment(albedoAtlas, 0, AccessFlags.ReadWrite);
                    builder.SetRenderAttachment(normalAtlas, 1, AccessFlags.ReadWrite);
                    builder.SetRenderAttachment(heightAtlas, 2, AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(depthAtlas, AccessFlags.Write);
                }
                else
                {
                    builder.UseTexture(albedoAtlas, AccessFlags.Read);
                    builder.UseTexture(normalAtlas, AccessFlags.Read);
                    builder.UseTexture(heightAtlas, AccessFlags.Read);
                }
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
                        // Depth is temporary. Only the surface atlases retain data between captures.
                        context.cmd.ClearRenderTarget(RTClearFlags.Depth, Color.clear, 1f, 0);
                        foreach (UpdateRegion update in data.Updates)
                        {
                            int level = update.Level;
                            int tileX = level & 1;
                            int tileY = level >> 1;
                            context.cmd.SetViewport(new Rect(
                                tileX * data.TileResolution,
                                tileY * data.TileResolution,
                                data.TileResolution,
                                data.TileResolution));
                            context.cmd.EnableScissorRect(new Rect(tileX * data.TileResolution + update.Pixels.x,
                                tileY * data.TileResolution + update.Pixels.y, update.Pixels.width, update.Pixels.height));
                            context.cmd.DrawProcedural(Matrix4x4.identity, data.ClearMaterial, 0, MeshTopology.Triangles, 3);
                            context.cmd.SetViewProjectionMatrices(
                                data.ViewMatrices[level],
                                data.ProjectionMatrices[level]);
                            Bounds updateBounds = UpdateBounds(update, data.CenterExtents[level], data.TileResolution,
                                data.MinimumHeight, data.MaximumHeight);
                            foreach (LoogaRuntimeVirtualTextureMeshCapture.Draw mesh in data.Meshes)
                            {
                                if (!mesh.Bounds.Intersects(updateBounds))
                                {
                                    continue;
                                }
                                context.cmd.DrawRenderer(mesh.Renderer, mesh.Material, mesh.Submesh, 0);
                            }
                            Vector4 region = data.CenterExtents[level];
                            foreach (LoogaRuntimeVirtualTextureTerrainCapture.Draw terrain in data.Terrains)
                            {
                                if (terrain.Origin.x > updateBounds.max.x || terrain.Origin.z > updateBounds.max.z
                                    || terrain.Origin.x + terrain.Size.x < updateBounds.min.x
                                    || terrain.Origin.z + terrain.Size.z < updateBounds.min.z)
                                {
                                    continue;
                                }
                                terrain.Properties.SetVector("_LoogaVTPageRect", new Vector4(
                                    (region.x - region.z - terrain.Origin.x) / terrain.Size.x,
                                    (region.y - region.z - terrain.Origin.z) / terrain.Size.z,
                                    region.z * 2f / terrain.Size.x, region.z * 2f / terrain.Size.z));
                                context.cmd.DrawProcedural(Matrix4x4.identity, terrain.Material, 0,
                                    MeshTopology.Triangles, 3, 1, terrain.Properties);
                            }
                        }
                    }

                    context.cmd.DisableScissorRect();
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

            private static Bounds UpdateBounds(UpdateRegion update, Vector4 region, int resolution,
                float minimumHeight, float maximumHeight)
            {
                float texel = region.z * 2f / resolution;
                // Include a texel at each edge for raster coverage.
                Vector3 minimum = new(region.x - region.z + (update.Pixels.xMin - 1) * texel,
                    minimumHeight, region.y - region.z + (update.Pixels.yMin - 1) * texel);
                Vector3 maximum = new(region.x - region.z + (update.Pixels.xMax + 1) * texel,
                    maximumHeight, region.y - region.z + (update.Pixels.yMax + 1) * texel);
                return new Bounds((minimum + maximum) * 0.5f, maximum - minimum);
            }

            private void BuildUpdates(CameraResources resources, int count, int resolution, bool full)
            {
                _updates.Clear();
                _shifts.Clear();
                resources.UpdatedTexels = 0;
                resources.CopiedTexels = 0;
                for (int level = 0; level < count; level++)
                {
                    Vector4 region = _centerExtents[level];
                    Vector2 delta = (resources.Centers[level] - _previousCenters[level]) / region.w;
                    int dx = Mathf.RoundToInt(delta.x);
                    int dy = Mathf.RoundToInt(delta.y);
                    if (full || Mathf.Abs(dx) >= resolution || Mathf.Abs(dy) >= resolution)
                    {
                        AddUpdate(resources, level, new RectInt(0, 0, resolution, resolution));
                        continue;
                    }
                    if (dx != 0 || dy != 0)
                    {
                        int originX = (level & 1) * resolution;
                        int originY = (level >> 1) * resolution;
                        var source = new RectInt(originX + Mathf.Max(0, dx), originY + Mathf.Max(0, dy),
                            resolution - Mathf.Abs(dx), resolution - Mathf.Abs(dy));
                        _shifts.Add(new ShiftRegion { Source = source,
                            Destination = new Vector2Int(originX + Mathf.Max(0, -dx), originY + Mathf.Max(0, -dy)) });
                        resources.CopiedTexels += source.width * source.height;
                        if (dx != 0)
                        {
                            AddUpdate(resources, level, new RectInt(dx > 0 ? resolution - dx : 0, 0, Mathf.Abs(dx), resolution));
                        }
                        if (dy != 0)
                        {
                            AddUpdate(resources, level, new RectInt(0, dy > 0 ? resolution - dy : 0, resolution, Mathf.Abs(dy)));
                        }
                    }
                    RectInt dirty = default;
                    bool any = false;
                    foreach (Bounds bounds in resources.DirtyRegions)
                    {
                        int x0 = Mathf.Clamp(Mathf.FloorToInt((bounds.min.x - region.x + region.z) / region.w) - 1, 0, resolution);
                        int y0 = Mathf.Clamp(Mathf.FloorToInt((bounds.min.z - region.y + region.z) / region.w) - 1, 0, resolution);
                        int x1 = Mathf.Clamp(Mathf.CeilToInt((bounds.max.x - region.x + region.z) / region.w) + 1, 0, resolution);
                        int y1 = Mathf.Clamp(Mathf.CeilToInt((bounds.max.z - region.y + region.z) / region.w) + 1, 0, resolution);
                        if (x1 <= x0 || y1 <= y0)
                        {
                            continue;
                        }
                        if (any)
                        {
                            x0 = Mathf.Min(x0, dirty.xMin); y0 = Mathf.Min(y0, dirty.yMin);
                            x1 = Mathf.Max(x1, dirty.xMax); y1 = Mathf.Max(y1, dirty.yMax);
                        }
                        dirty = new RectInt(x0, y0, x1 - x0, y1 - y0);
                        any = true;
                    }
                    if (any)
                    {
                        AddUpdate(resources, level, dirty);
                    }
                }
            }

            private void AddUpdate(CameraResources resources, int level, RectInt pixels)
            {
                _updates.Add(new UpdateRegion { Level = level, Pixels = pixels });
                resources.UpdatedTexels += pixels.width * pixels.height;
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

                    _viewMatrices[level] = LoogaRuntimeVirtualTextureMath.GetCaptureView(
                        center, maximumHeight + nearPlane);
                    Matrix4x4 projection = Matrix4x4.Ortho(
                        -extent * 0.5f,
                        extent * 0.5f,
                        -extent * 0.5f,
                        extent * 0.5f,
                        nearPlane,
                        farPlane);
                    // Match terrain writer UVs: positive world Z maps to increasing texture Y.
                    Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(projection, false);
                    _projectionMatrices[level] = LoogaRuntimeVirtualTextureMath.ToForwardDepthProjection(
                        gpuProjection, SystemInfo.usesReversedZBuffer);
                }

                return centerChanged;
            }

            public bool TryGetStatistics(Camera camera, out int captures, out double cullMilliseconds)
            {
                captures = 0;
                cullMilliseconds = 0;
                if (!camera || !_cameraResources.TryGetValue(camera.GetInstanceID(), out CameraResources resources))
                    return false;
                captures = resources.CaptureCount;
                cullMilliseconds = resources.LastCullMilliseconds;
                return true;
            }

            public bool TryGetUpdateStatistics(Camera camera, out int updatedTexels, out int copiedTexels,
                out int discoveries, out int candidates)
            {
                discoveries = _meshCapture.DiscoveryCount;
                candidates = _meshCapture.CandidateCount;
                updatedTexels = copiedTexels = 0;
                if (!camera || !_cameraResources.TryGetValue(camera.GetInstanceID(), out CameraResources resources)) return false;
                updatedTexels = resources.UpdatedTexels;
                copiedTexels = resources.CopiedTexels;
                return true;
            }

            private CameraResources GetCameraResources(Camera camera)
            {
                _expiredCameras.Clear();
                foreach (var entry in _cameraResources)
                {
                    if (!entry.Value.Owner)
                    {
                        _expiredCameras.Add(entry.Key);
                    }
                }
                foreach (int expired in _expiredCameras)
                {
                    _cameraResources[expired].Release();
                    _cameraResources.Remove(expired);
                }
                int cameraId = camera.GetInstanceID();
                if (_cameraResources.TryGetValue(cameraId, out CameraResources resources))
                    return resources;

                resources = new CameraResources { Owner = camera };
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
                    hash = hash * 31 + _camera.cullingMask;
                    hash = hash * 31 + (_settings.includeAlphaTestedGeometry ? 1 : 0);
                    foreach (Material writer in _settings.terrainWriters ?? System.Array.Empty<Material>())
                    {
                        hash = hash * 31 + (writer ? writer.GetInstanceID() : 0);
                    }
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

                RenderTextureDescriptor scratchDescriptor = colorDescriptor;
                scratchDescriptor.width = scratchDescriptor.height = resolution / 2;
                changed |= RenderingUtils.ReAllocateHandleIfNeeded(ref resources.Scratch, scratchDescriptor,
                    FilterMode.Point, TextureWrapMode.Clamp, name: "Looga RVT scroll scratch");
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
