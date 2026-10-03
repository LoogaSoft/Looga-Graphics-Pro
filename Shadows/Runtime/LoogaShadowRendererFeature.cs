using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;
using UnityEngine.Serialization;

namespace LoogaSoft.Shadows
{
    /// <summary>
    /// Renders package-owned, camera-centered directional clipmaps and publishes the resolved result
    /// through URP's screen-space main-light shadow contract. The renderer never samples URP's atlas.
    /// </summary>
    [SupportedOnRenderer(typeof(UniversalRendererData))]
    [DisallowMultipleRendererFeature("Looga Shadows")]
    [Tooltip("Renders package-owned directional shadow clipmaps and resolves them for URP lighting.")]
    public sealed class LoogaShadowRendererFeature : ScriptableRendererFeature
    {
#if UNITY_EDITOR
        [UnityEditor.ShaderKeywordFilter.SelectIf(true, keywordNames: "_MAIN_LIGHT_SHADOWS_SCREEN")]
        private const bool RequiresScreenSpaceShadowReceiverVariant = true;

        [UnityEditor.ShaderKeywordFilter.SelectIf(true, keywordNames: "_MAIN_LIGHT_SHADOWS_CASCADE")]
        private const bool RequiresTransparentCascadeShadowReceiverVariant = true;
#endif

        private const string FeatureName = "Looga Shadows";
        private const string ShaderName = "Hidden/LoogaSoft/Shadows/VirtualShadowResolve";
        private const string CasterCacheShaderName = "Hidden/LoogaSoft/Shadows/CasterCache";
        private const string CopyDepthShaderName = "Hidden/Universal Render Pipeline/CopyDepth";
        private const int MaximumClipmapCount = 4;

        // Each cached level redraws at most two exposed strips per frame, each culled by its own split.
        private const int MaximumCachedLevels = 2;
        private const int MaximumStaticSplits = MaximumCachedLevels * LoogaShadowCasterCache.MaximumExposedRects;

        // Re-culling additional lights repeats URP's splits: one per spot light, one per cube face.
        // URP widens point-light faces by a guard angle chosen from its internal atlas slice size;
        // this is its largest angle for slices of 64 texels and up, which keeps a superset of casters.
        private const int PointLightShadowSplitCount = 6;
        private const float PointLightCullingFovBias = 12.7f;

        // Texels at the tile border that the resolve shader never samples (matches the shader guard).
        private const float ClipmapGuardTexels = 1.5f;
        private const int FrustumFitIterations = 20;

        // The finest level is sized so its texels are this fraction of a screen pixel at the
        // nearest visible receiver. Below one, low sun angles still resolve: the light stretches
        // a texel across the ground by 1 / sin(elevation).
        private const float ShadowTexelsPerScreenPixel = 0.5f;

        // Fits start slightly in front of the measured nearest receiver, which is a frame old.
        private const float ReceiverSliceStartScale = 0.9f;

        private const string ReceiverBoundsShaderPath =
            "Packages/com.loogasoft.loogagraphicspro/Shadows/Runtime/Shaders/LoogaReceiverDepthBounds.compute";
        private const int RendererSettingsVersion = 1;

        // URP soft-shadow parity. Smaller values let surfaces near the
        // light grazing angle shadow themselves.
        private const float CasterNormalBiasTexels = 3.5f;

        // Hardware slope-scaled bias for shadow casters. Lower values leave faint
        // self-shadow stripes on surfaces that the light reaches at a grazing angle.
        private const float CasterSlopeBias = 6f;

        [SerializeField]
        private LoogaShadowSettings _settings = LoogaShadowSettings.Default;

        // Kept hidden for one release so renderer assets using the former profile mode can
        // copy that profile into their inline settings without changing their rendered result.
        [SerializeField, HideInInspector, FormerlySerializedAs("_profile")]
        private LoogaShadowProfile _legacyProfile;

        [SerializeField, HideInInspector, FormerlySerializedAs("_settingsSource")]
        private int _legacySettingsSource;

        [SerializeField, HideInInspector]
        private int _rendererSettingsVersion;

        [SerializeField, HideInInspector]
        private Shader _resolveShader;

        [SerializeField, HideInInspector]
        private ComputeShader _receiverBoundsShader;

        [SerializeField, HideInInspector]
        private Shader _casterCacheShader;

        private readonly Matrix4x4[] _worldToShadow = new Matrix4x4[MaximumClipmapCount];
        private readonly Matrix4x4[] _viewMatrices = new Matrix4x4[MaximumClipmapCount];
        private readonly Matrix4x4[] _projectionMatrices = new Matrix4x4[MaximumClipmapCount];
        private readonly Vector4[] _clipmapCenters = new Vector4[MaximumClipmapCount];
        private readonly Vector4[] _clipmapRadii = new Vector4[MaximumClipmapCount];
        private readonly Vector4[] _clipmapRects = new Vector4[MaximumClipmapCount];
        private readonly ShadowSplitData[] _clipmapSplits = new ShadowSplitData[MaximumClipmapCount];
        private readonly Plane[] _cameraFrustumPlanes = new Plane[6];
        private readonly Plane[] _splitPlanes = new Plane[ShadowSplitData.maximumCullingPlaneCount];
        private readonly Vector3[] _frustumCorners = new Vector3[4];
        private readonly Vector3[] _nearSliceCorners = new Vector3[4];

        private Material _resolveMaterial;
        private Texture2D _blueNoiseTexture;
        private LoogaShadowReceiverBounds _receiverBounds;

        // Cached coarse levels: this frame's mode and cache entry per level, and the culling splits that
        // redraw static casters into the caches, after the level splits. Each split covers one exposed rect.
        private Material _casterCacheMaterial;
        private LoogaShadowCasterCache _casterCache;
        private readonly LoogaCachedLevelMode[] _levelModes = new LoogaCachedLevelMode[MaximumClipmapCount];
        private readonly LoogaShadowCasterCache.Level[] _cachedLevels =
            new LoogaShadowCasterCache.Level[MaximumClipmapCount];
        private readonly ShadowSplitData[] _staticSplits = new ShadowSplitData[MaximumStaticSplits];
        private readonly int[] _staticSplitLevels = new int[MaximumStaticSplits];
        private readonly RectInt[] _staticSplitRects = new RectInt[MaximumStaticSplits];
        private int _staticSplitCount;

        // The camera whose culling shadow distance OnCameraPreCull raised, and URP's own value.
        private Camera _raisedShadowDistanceCamera;
        private float _urpMaxShadowDistance;

        // The URP asset whose cascade count OnCameraPreCull lowered, and its own count.
        private UniversalRenderPipelineAsset _reducedCascadeAsset;
        private int _urpShadowCascadeCount;

        // The light whose instanced shadow culls OnCameraPreCull suspended for URP's cull.
        private Light _ignoredInstanceCullLight;
        private ClipmapAtlasPass _atlasPass;
        private ResolvePass _resolvePass;
        private TransparentShadowReceiverPass _transparentShadowReceiverPass;
        private DebugOverlayPass _debugOverlayPass;
        private ScriptableRenderer _cachedRenderer;
        private bool _cachedRendererUsesDeferredLighting;

        private sealed class LoogaShadowFrameData : ContextItem
        {
            public readonly TextureHandle[] Clipmaps = new TextureHandle[MaximumClipmapCount];
            public readonly TextureHandle[] DepthClipmaps = new TextureHandle[MaximumClipmapCount];
            public TextureHandle RawVisibility = TextureHandle.nullHandle;
            public TextureHandle ResolvedVisibility = TextureHandle.nullHandle;
            public bool HasShadowCasters;

            public override void Reset()
            {
                for (int level = 0; level < MaximumClipmapCount; level++)
                {
                    Clipmaps[level] = TextureHandle.nullHandle;
                    DepthClipmaps[level] = TextureHandle.nullHandle;
                }
                RawVisibility = TextureHandle.nullHandle;
                ResolvedVisibility = TextureHandle.nullHandle;
                HasShadowCasters = false;
            }
        }

        public LoogaShadowSettings Settings => _settings;

        /// <summary>
        /// Redraws the cached static shadow casters of every camera's coarse levels. Call this after static shadow
        /// casters change at runtime: renderers marked Static Shadow Caster that move, appear or disappear. Scene
        /// loads, editor changes and Looga Instancing changes invalidate the caches without a call.
        /// </summary>
        public static void InvalidateCachedShadows()
        {
            LoogaShadowCasterCache.Invalidate();
        }

        public override void Create()
        {
            MigrateLegacySettings();
            name = FeatureName;
            EnsureMaterial();

            _atlasPass ??= new ClipmapAtlasPass();
            _resolvePass ??= new ResolvePass();
            _transparentShadowReceiverPass ??= new TransparentShadowReceiverPass();
            _debugOverlayPass ??= new DebugOverlayPass();
            _receiverBounds ??= new LoogaShadowReceiverBounds();
            _casterCache ??= new LoogaShadowCasterCache(MaximumClipmapCount);
            EnsureReceiverBoundsShader();
            _receiverBounds.SetShader(_receiverBoundsShader);

            _atlasPass.renderPassEvent = (RenderPassEvent)((int)RenderPassEvent.BeforeRenderingShadows + 1);
            _transparentShadowReceiverPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            _debugOverlayPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }

        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
        {
            _raisedShadowDistanceCamera = null;
            RestoreUrpShadowCascades();
            Camera camera = cameraData.camera;
            if (camera == null)
                return;

            // The main light is not known before culling. URP prefers RenderSettings.sun.
            LoogaShadowLightRegistry.TryGet(RenderSettings.sun, out LoogaShadowLight shadowLight);
            LoogaShadowResolvedSettings settings = LoogaShadowResolvedSettings.Resolve(
                _settings,
                shadowLight);
            if (!ShouldRenderCamera(camera, settings.RenderSceneView) || !EnsureMaterial())
                return;

            // URP culls shadow casters for every cascade of its main-light shadow map before
            // AddRenderPasses can turn that map off, and Looga then culls its own levels, which
            // replaces URP's result. Cull a single cascade for URP until AddRenderPasses restores
            // the asset. With GPU Resident Drawer, each cascade is a culling job over every instance.
            ReduceUrpShadowCascades();
            // Looga Instancing runs a compute cull for every shadow cull. Skip URP's, which nothing draws.
            if (RenderSettings.sun != null)
            {
                _ignoredInstanceCullLight = RenderSettings.sun;
                LoogaSoft.Instancing.InstanceShadowSplits.IgnoreCulls(_ignoredInstanceCullLight);
            }

            // The engine drops shadow casters beyond the culling shadow distance, which URP takes
            // from its asset. Raise it to the Looga shadow distance for the cull. AddRenderPasses
            // restores URP's value before URP's shadow passes read it, so additional-light shadows
            // keep their own range and fade.
            ref float maxShadowDistance = ref cameraData.maxShadowDistance;
            float shadowDistance = Mathf.Min(settings.ShadowDistance, camera.farClipPlane);
            if (maxShadowDistance <= 0f || shadowDistance <= maxShadowDistance)
                return;

            _urpMaxShadowDistance = maxShadowDistance;
            _raisedShadowDistanceCamera = camera;
            maxShadowDistance = shadowDistance;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            RestoreUrpShadowDistance(ref renderingData);
            RestoreUrpShadowCascades();
            if (!isActive || !EnsureMaterial())
                return;

            Camera camera = renderingData.cameraData.camera;
            int mainLightIndex = renderingData.lightData.mainLightIndex;
            if (camera == null || mainLightIndex < 0)
                return;

            VisibleLight visibleLight = renderingData.lightData.visibleLights[mainLightIndex];
            Light mainLight = visibleLight.light;
            if (mainLight == null || visibleLight.lightType != LightType.Directional || mainLight.shadows == LightShadows.None)
                return;

            LoogaShadowLightRegistry.TryGet(mainLight, out LoogaShadowLight shadowLight);
            LoogaShadowResolvedSettings settings = LoogaShadowResolvedSettings.Resolve(
                _settings,
                shadowLight);
            if (!ShouldRenderCamera(camera, settings.RenderSceneView))
                return;

            BuildClipmaps(camera, mainLight, settings);
            bool usesDeferredLighting = GetUsesDeferredLighting(renderer);
            bool usesAccurateGBufferNormals =
                usesDeferredLighting && GetUsesAccurateGBufferNormals(renderer);

            _atlasPass.Setup(
                mainLightIndex,
                visibleLight,
                settings,
                _worldToShadow,
                _viewMatrices,
                _projectionMatrices,
                _clipmapCenters,
                _clipmapRadii,
                _clipmapRects,
                _clipmapSplits);
            _atlasPass.SetupCache(
                _casterCacheMaterial,
                _levelModes,
                _cachedLevels,
                _staticSplits,
                _staticSplitLevels,
                _staticSplitRects,
                _staticSplitCount);

            float softShadowQuality = GetSoftShadowQuality(
                mainLight,
                renderingData.shadowData.supportsSoftShadows);
            Vector4 mainLightShadowParams = GetMainLightShadowParams(
                mainLight.shadowStrength,
                softShadowQuality,
                settings.ShadowDistance);

            _resolvePass.renderPassEvent = usesDeferredLighting
                ? (RenderPassEvent)((int)RenderPassEvent.AfterRenderingGbuffer + 1)
                : (RenderPassEvent)((int)RenderPassEvent.AfterRenderingPrePasses + 1);
            _resolvePass.Setup(
                _resolveMaterial,
                _receiverBounds,
                settings,
                _worldToShadow,
                _clipmapCenters,
                _clipmapRadii,
                _clipmapRects,
                -mainLight.transform.forward,
                mainLightShadowParams,
                usesDeferredLighting,
                usesAccurateGBufferNormals);
            _transparentShadowReceiverPass.Setup(
                settings,
                _worldToShadow,
                _clipmapCenters,
                _clipmapRadii,
                mainLightShadowParams);

            renderer.EnqueuePass(_atlasPass);
            renderer.EnqueuePass(_resolvePass);
            renderer.EnqueuePass(_transparentShadowReceiverPass);

            // Looga renders the main light's shadows, so URP's own cascaded shadow map would be drawn
            // for nothing. Without main-light shadow support URP's pass binds an empty shadow map and
            // default parameters instead; the passes above replace those. URP has already culled its
            // shadow map by now, as one cascade (see OnCameraPreCull).
            renderingData.shadowData.supportsMainLightShadows = false;

            if (settings.DebugView != LoogaShadowDebugView.Off)
            {
                _debugOverlayPass.Setup(
                    _resolveMaterial,
                    settings,
                    _worldToShadow,
                    _clipmapCenters,
                    _clipmapRadii,
                    _clipmapRects,
                    -mainLight.transform.forward,
                    usesDeferredLighting,
                    usesAccurateGBufferNormals);
                renderer.EnqueuePass(_debugOverlayPass);
            }

            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            LoogaShadowRuntimeDiagnostics.RecordCamera(
                camera.name,
                descriptor.width,
                descriptor.height,
                settings.AtlasResolution,
                settings.ClipmapCount,
                settings.DebugView,
                mainLight.name,
                settings.SettingsSource);
        }

        protected override void Dispose(bool disposing)
        {
            RestoreUrpShadowCascades();
            Shader.SetGlobalInteger(LoogaShadowShaderIds.ShadowsEnabled, 0);
            _atlasPass?.Dispose();
            _receiverBounds?.Dispose();
            _receiverBounds = null;
            _casterCache?.Dispose();
            _casterCache = null;
            CoreUtils.Destroy(_resolveMaterial);
            _resolveMaterial = null;
            CoreUtils.Destroy(_casterCacheMaterial);
            _casterCacheMaterial = null;
            _blueNoiseTexture = null;
            LoogaShadowRuntimeDiagnostics.Reset();
            base.Dispose(disposing);
        }

        private void OnValidate()
        {
            MigrateLegacySettings();
            _settings.EnsureInitialized();
            _settings.Validate();
        }

        private void MigrateLegacySettings()
        {
            if (_rendererSettingsVersion >= RendererSettingsVersion)
                return;

            // The former Profile enum value was zero. Renderer-feature mode was one.
            if (_legacySettingsSource == 0 && _legacyProfile != null)
                _settings = _legacyProfile.Settings;
            else
                _settings.EnsureInitialized();

            _settings.Validate();
            _legacyProfile = null;
            _legacySettingsSource = 1;
            _rendererSettingsVersion = RendererSettingsVersion;
        }

        private static bool ShouldRenderCamera(Camera camera, bool renderSceneView)
        {
            if (camera.cameraType == CameraType.Game)
                return true;

            return camera.cameraType == CameraType.SceneView && renderSceneView;
        }

        private bool EnsureMaterial()
        {
            if (_resolveMaterial == null)
            {
                if (_resolveShader == null)
                    _resolveShader = Shader.Find(ShaderName);

                if (_resolveShader == null)
                    return false;

                _resolveMaterial = CoreUtils.CreateEngineMaterial(_resolveShader);
                if (_resolveMaterial == null)
                    return false;
            }

            EnsureBlueNoiseTexture();
            return true;
        }

        // Without the shader, every level is drawn uncached.
        private bool EnsureCasterCacheMaterial()
        {
            if (_casterCacheMaterial != null)
                return true;

            if (_casterCacheShader == null)
                _casterCacheShader = Shader.Find(CasterCacheShaderName);

            if (_casterCacheShader == null)
                return false;

            _casterCacheMaterial = CoreUtils.CreateEngineMaterial(_casterCacheShader);
            return _casterCacheMaterial != null;
        }

        private void RestoreUrpShadowDistance(ref RenderingData renderingData)
        {
            if (_raisedShadowDistanceCamera == null)
                return;

            if (_raisedShadowDistanceCamera == renderingData.cameraData.camera)
                renderingData.cameraData.maxShadowDistance = _urpMaxShadowDistance;

            _raisedShadowDistanceCamera = null;
        }

        private void ReduceUrpShadowCascades()
        {
            UniversalRenderPipelineAsset asset = UniversalRenderPipeline.asset;
            if (asset == null || asset.shadowCascadeCount <= 1)
                return;

            _reducedCascadeAsset = asset;
            _urpShadowCascadeCount = asset.shadowCascadeCount;
            asset.shadowCascadeCount = 1;
        }

        // The asset is shared and serialized, so the count is put back within the same camera
        // render, and again on the next cull or on dispose if a render stopped early.
        private void RestoreUrpShadowCascades()
        {
            if (_ignoredInstanceCullLight != null)
                LoogaSoft.Instancing.InstanceShadowSplits.ResumeCulls(_ignoredInstanceCullLight);
            _ignoredInstanceCullLight = null;

            if (_reducedCascadeAsset == null)
                return;

            _reducedCascadeAsset.shadowCascadeCount = _urpShadowCascadeCount;
            _reducedCascadeAsset = null;
        }

        private void EnsureReceiverBoundsShader()
        {
#if UNITY_EDITOR
            // Compute shaders have no Shader.Find, so the renderer asset keeps a reference
            // that player builds include.
            if (_receiverBoundsShader != null)
                return;

            _receiverBoundsShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(
                ReceiverBoundsShaderPath);
            if (_receiverBoundsShader != null)
                UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        private void EnsureBlueNoiseTexture()
        {
            if (_blueNoiseTexture == null)
            {
                UniversalRenderPipelineRuntimeTextures runtimeTextures =
                    GraphicsSettings.GetRenderPipelineSettings<UniversalRenderPipelineRuntimeTextures>();
                _blueNoiseTexture = runtimeTextures?.blueNoise64LTex;
            }

            bool available = _blueNoiseTexture != null;
            if (available)
                _resolveMaterial.SetTexture(LoogaShadowShaderIds.BlueNoiseTexture, _blueNoiseTexture);

            _resolveMaterial.SetFloat(
                LoogaShadowShaderIds.BlueNoiseAvailable,
                available ? 1f : 0f);
        }

        private bool GetUsesDeferredLighting(ScriptableRenderer renderer)
        {
            if (renderer == null)
                return false;

            if (_cachedRenderer == renderer)
                return _cachedRendererUsesDeferredLighting;

            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            System.Reflection.PropertyInfo property = renderer.GetType().GetProperty("usesDeferredLighting", flags);
            _cachedRenderer = renderer;
            _cachedRendererUsesDeferredLighting =
                property?.PropertyType == typeof(bool) && (bool)property.GetValue(renderer);
            return _cachedRendererUsesDeferredLighting;
        }

        private static bool GetUsesAccurateGBufferNormals(ScriptableRenderer renderer)
        {
            if (renderer == null)
                return false;

            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;
            System.Reflection.PropertyInfo property =
                renderer.GetType().GetProperty("accurateGbufferNormals", flags);
            return property?.PropertyType == typeof(bool) &&
                (bool)property.GetValue(renderer);
        }

        private static LoogaShadowNormalsSource GetEffectiveNormalsSource(
            LoogaShadowNormalsSource requestedSource,
            bool usesDeferredLighting)
        {
            if (requestedSource == LoogaShadowNormalsSource.GBuffer && !usesDeferredLighting)
                return LoogaShadowNormalsSource.ReconstructFromDepth;

            return requestedSource;
        }

        private static ScriptableRenderPassInput GetRequiredInputs(
            LoogaShadowNormalsSource normalsSource)
        {
            ScriptableRenderPassInput inputs = ScriptableRenderPassInput.Depth;
            if (normalsSource == LoogaShadowNormalsSource.DepthNormalsPass)
                inputs |= ScriptableRenderPassInput.Normal;

            return inputs;
        }

        private static TextureHandle GetNormalsTexture(
            UniversalResourceData resourceData,
            LoogaShadowNormalsSource normalsSource)
        {
            return normalsSource switch
            {
                LoogaShadowNormalsSource.GBuffer => resourceData.gBuffer[2],
                LoogaShadowNormalsSource.DepthNormalsPass => resourceData.cameraNormalsTexture,
                _ => TextureHandle.nullHandle
            };
        }

        private void BuildClipmaps(Camera camera, Light light, LoogaShadowResolvedSettings settings)
        {
            // Shadow cameras sit toward the light and look along the rays toward the scene.
            // Unity's directional-light transform.forward is the direction the light travels.
            Vector3 lightDirection = light.transform.forward.normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(lightDirection, Vector3.up)) > 0.98f
                ? Vector3.forward
                : Vector3.up;
            Quaternion lightRotation = Quaternion.LookRotation(lightDirection, up);
            Matrix4x4 lightWorldToLocal = Matrix4x4.Rotate(Quaternion.Inverse(lightRotation));
            Matrix4x4 lightLocalToWorld = Matrix4x4.Rotate(lightRotation);

            float baseRadius = GetBaseClipmapRadius(
                camera,
                settings,
                out float nearDistance);
            float farDistance = Mathf.Max(
                Mathf.Min(settings.ShadowDistance, camera.farClipPlane),
                nearDistance);
            GetLightSpaceFrustumCorners(camera, nearDistance, lightWorldToLocal, _nearSliceCorners);

            // Visible receivers lie inside the view frustum up to the shadow distance.
            GeometryUtility.CalculateFrustumPlanes(camera, _cameraFrustumPlanes);
            Transform cameraTransform = camera.transform;
            _cameraFrustumPlanes[5] = new Plane(
                -cameraTransform.forward,
                cameraTransform.position + cameraTransform.forward * farDistance);
            Vector2 previousCenter = Vector2.zero;
            float previousRadius = 0f;
            _staticSplitCount = 0;
            bool canCache = _casterCache != null && !camera.stereoEnabled && EnsureCasterCacheMaterial();
            _casterCache?.RemoveDestroyedCameras();

            for (int level = 0; level < MaximumClipmapCount; level++)
            {
                bool cached = canCache && level < settings.ClipmapCount && settings.IsCachedLevel(level);
                _levelModes[level] = LoogaCachedLevelMode.Uncached;
                _cachedLevels[level] = null;
                if (!cached)
                    _casterCache?.ReleaseLevel(camera, level);

                if (level >= settings.ClipmapCount)
                {
                    _worldToShadow[level] = Matrix4x4.zero;
                    _viewMatrices[level] = Matrix4x4.identity;
                    _projectionMatrices[level] = Matrix4x4.identity;
                    _clipmapCenters[level] = Vector4.zero;
                    _clipmapRadii[level] = Vector4.zero;
                    _clipmapRects[level] = Vector4.zero;
                    _clipmapSplits[level] = default;
                    continue;
                }

                float clipmapT = settings.ClipmapCount > 1
                    ? level / (settings.ClipmapCount - 1f)
                    : 0f;
                float coverageRatio = Mathf.Max(
                    settings.ShadowDistance / baseRadius,
                    1f);
                float radius = baseRadius * Mathf.Pow(coverageRatio, clipmapT);
                int clipmapResolution = settings.TileResolution;
                float worldTexelSize = radius * 2f / clipmapResolution;

                // The resolve shader hands receivers in the outer guard and blend band to the next
                // level (LoogaClipmapEdgeBlend), so only the inner square serves this level alone.
                float handoffLocal = ClipmapGuardTexels / clipmapResolution +
                    (level + 1 < settings.ClipmapCount ? settings.ClipmapBlend : 0f);
                float usableHalfExtent = radius * Mathf.Max(1f - 2f * handoffLocal, 0.25f);

                // Centre the level on the longest slice of the view frustum that fits its usable
                // square, the way cascades are fitted, so the texels land where the camera looks.
                FitFrustumSlice(
                    camera,
                    lightWorldToLocal,
                    nearDistance,
                    farDistance,
                    usableHalfExtent,
                    out Vector3 sliceMin,
                    out Vector3 sliceMax);
                Vector3 lightSpaceCenter = (sliceMin + sliceMax) * 0.5f;

                // Keep the previous level inside this level's usable square, so its handoff band
                // always blends into valid texels here.
                if (level > 0)
                {
                    float slack = Mathf.Max(usableHalfExtent - previousRadius - worldTexelSize, 0f);
                    lightSpaceCenter.x = previousCenter.x +
                        Mathf.Clamp(lightSpaceCenter.x - previousCenter.x, -slack, slack);
                    lightSpaceCenter.y = previousCenter.y +
                        Mathf.Clamp(lightSpaceCenter.y - previousCenter.y, -slack, slack);
                }

                // Quantized origins keep texels stationary under sub-texel camera motion.
                float centerTexelX = Mathf.Floor(lightSpaceCenter.x / worldTexelSize);
                float centerTexelY = Mathf.Floor(lightSpaceCenter.y / worldTexelSize);
                lightSpaceCenter.x = centerTexelX * worldTexelSize;
                lightSpaceCenter.y = centerTexelY * worldTexelSize;
                previousCenter = new Vector2(lightSpaceCenter.x, lightSpaceCenter.y);
                previousRadius = radius;

                // A cached level's depth values depend on the depth of its centre along the light.
                if (cached)
                {
                    lightSpaceCenter.z = _casterCache.HoldLightDepth(
                        camera,
                        level,
                        lightRotation,
                        lightSpaceCenter.z,
                        settings.DepthRange);
                }

                Vector3 center = lightLocalToWorld.MultiplyPoint3x4(lightSpaceCenter);
                Vector3 eye = center - lightDirection * settings.DepthRange * 0.5f;

                // Unity camera/view space looks down -Z, while Transform.forward is +Z.
                // Match Camera.worldToCameraMatrix so the orthographic projection's near/far
                // range and the receiver shadow transform use the same depth convention.
                Matrix4x4 view =
                    Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) *
                    Matrix4x4.TRS(eye, lightRotation, Vector3.one).inverse;
                Matrix4x4 projection = Matrix4x4.Ortho(
                    -radius,
                    radius,
                    -radius,
                    radius,
                    0.01f,
                    settings.DepthRange);

                _viewMatrices[level] = view;
                _projectionMatrices[level] = projection;
                _worldToShadow[level] = GetAtlasShadowTransform(view, projection, level);
                _clipmapCenters[level] = new Vector4(center.x, center.y, center.z, radius);
                _clipmapRadii[level] = new Vector4(
                    radius,
                    worldTexelSize,
                    1f / worldTexelSize,
                    1f / clipmapResolution);
                _clipmapRects[level] = Vector4.zero;
                _clipmapSplits[level] = CreateBoxSplit(
                    center,
                    radius,
                    radius,
                    lightRotation,
                    lightDirection,
                    settings.DepthRange,
                    Mathf.Min(settings.MaximumPenumbra, radius * 0.45f),
                    projection * view,
                    true);

                if (cached)
                {
                    PlanCachedLevel(
                        camera,
                        light,
                        level,
                        settings,
                        lightRotation,
                        lightDirection,
                        lightSpaceCenter.z,
                        new Vector2Int(
                            (int)centerTexelX - clipmapResolution / 2,
                            (int)centerTexelY - clipmapResolution / 2),
                        center,
                        radius,
                        worldTexelSize,
                        projection * view);
                }
            }
        }

        // Decides how a cached level is drawn this frame, and adds a static-caster split for each rect of the
        // level that its cache must redraw.
        private void PlanCachedLevel(
            Camera camera,
            Light light,
            int level,
            LoogaShadowResolvedSettings settings,
            Quaternion lightRotation,
            Vector3 lightDirection,
            float lightDepth,
            Vector2Int origin,
            Vector3 center,
            float radius,
            float worldTexelSize,
            Matrix4x4 viewProjection)
        {
            int resolution = settings.TileResolution;
            LoogaShadowCasterCache.Key key = new(
                light.GetInstanceID(),
                lightRotation,
                radius,
                resolution,
                settings.DepthRange,
                lightDepth,
                ClipmapAtlasPass.GetCasterShadowBias(worldTexelSize, settings));
            _levelModes[level] = _casterCache.Plan(
                camera,
                level,
                key,
                lightRotation,
                lightDepth,
                origin,
                resolution);
            LoogaShadowCasterCache.Level entry = _casterCache.GetLevel(camera, level);
            _cachedLevels[level] = entry;

            Vector3 right = lightRotation * Vector3.right;
            Vector3 up = lightRotation * Vector3.up;
            for (int index = 0; index < entry.ExposedCount && _staticSplitCount < MaximumStaticSplits; index++)
            {
                // Cached casters must not depend on the view, which changes while they stay cached.
                RectInt rect = entry.Exposed[index];
                Vector3 rectCenter = center +
                    right * ((rect.xMin + rect.xMax) * 0.5f * worldTexelSize - radius) +
                    up * ((rect.yMin + rect.yMax) * 0.5f * worldTexelSize - radius);
                _staticSplits[_staticSplitCount] = CreateBoxSplit(
                    rectCenter,
                    rect.width * 0.5f * worldTexelSize,
                    rect.height * 0.5f * worldTexelSize,
                    lightRotation,
                    lightDirection,
                    settings.DepthRange,
                    0f,
                    viewProjection,
                    false);
                _staticSplitLevels[_staticSplitCount] = level;
                _staticSplitRects[_staticSplitCount] = rect;
                _staticSplitCount++;
            }
        }

        // One shadow-caster culling split per clipmap level, so the engine and every
        // BatchRendererGroup cull casters against the levels that draw them. Cached levels add
        // splits for the parts of the level their caches redraw, without the view bounds.
        private ShadowSplitData CreateBoxSplit(
            Vector3 center,
            float halfWidth,
            float halfHeight,
            Quaternion lightRotation,
            Vector3 lightDirection,
            float depthRange,
            float blockerSearchWorld,
            Matrix4x4 viewProjection,
            bool cullByView)
        {
            Vector3 right = lightRotation * Vector3.right;
            Vector3 up = lightRotation * Vector3.up;
            Vector3 far = center + lightDirection * depthRange * 0.5f;
            int planeCount = 0;

            // The box in light space. The side toward the light stays open: casters
            // beyond the near plane are clamped onto it and still shadow the level.
            _splitPlanes[planeCount++] = new Plane(right, center - right * halfWidth);
            _splitPlanes[planeCount++] = new Plane(-right, center + right * halfWidth);
            _splitPlanes[planeCount++] = new Plane(up, center - up * halfHeight);
            _splitPlanes[planeCount++] = new Plane(-up, center + up * halfHeight);
            _splitPlanes[planeCount++] = new Plane(-lightDirection, far);

            // A caster matters only if its shadow reaches a visible receiver, that is, if moving
            // along the light from it enters the view frustum. Each frustum plane that the light
            // does not cross inward therefore bounds the casters too. The planes widen by the
            // blocker search radius, because penumbrae of visible receivers sample that far.
            for (int index = 0; cullByView && index < _cameraFrustumPlanes.Length; index++)
            {
                Plane plane = _cameraFrustumPlanes[index];
                if (Vector3.Dot(plane.normal, lightDirection) > 0f)
                    continue;

                if (planeCount >= _splitPlanes.Length)
                    break;

                _splitPlanes[planeCount++] =
                    new Plane(plane.normal, plane.distance + blockerSearchWorld);
            }

            ShadowSplitData split = default;
            split.cullingPlaneCount = planeCount;
            for (int index = 0; index < planeCount; index++)
                split.SetCullingPlane(index, _splitPlanes[index]);

            // Encloses the whole box, depth included. Cascade blend culling stays off: a caster
            // inside a finer level can still shadow receivers that only this level covers.
            float sphereRadius = Mathf.Sqrt(
                halfWidth * halfWidth + halfHeight * halfHeight + 0.25f * depthRange * depthRange);
            split.cullingSphere = new Vector4(center.x, center.y, center.z, sphereRadius);
            split.shadowCascadeBlendCullingFactor = 0f;
            split.cullingMatrix = viewProjection;
            return split;
        }

        // Sizes the finest level for the nearest visible receiver. Its texels must resolve that
        // receiver's screen pixels; a smaller level is wasted on space the camera cannot see, and
        // a level too small to reach the receivers pushes them onto a much coarser one. Near
        // Clipmap Radius is the floor, and the result snaps to half-octave steps so that texel
        // sizes stay fixed while the camera moves. The fit start distance is where the frustum
        // slices begin: the space between the camera and the first surface holds no receivers
        // and would waste the fine levels.
        private float GetBaseClipmapRadius(
            Camera camera,
            LoogaShadowResolvedSettings settings,
            out float fitStartDistance)
        {
            fitStartDistance = Mathf.Max(camera.nearClipPlane, 0.0001f);
            if (_receiverBounds == null)
                return settings.NearClipmapRadius;

            float pixelHeight = Mathf.Max(camera.pixelHeight, 1);
            float radiusPerFootprint =
                ShadowTexelsPerScreenPixel * settings.TileResolution * 0.5f;
            float maximumStep = Mathf.Floor(2f * Mathf.Log(
                Mathf.Max(settings.ShadowDistance / settings.NearClipmapRadius, 1f),
                2f));
            float idealRadius;
            if (camera.orthographic)
            {
                idealRadius = 2f * camera.orthographicSize / pixelHeight * radiusPerFootprint;
            }
            else
            {
                float radiusPerDepth = 2f *
                    Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / pixelHeight *
                    radiusPerFootprint;
                if (_receiverBounds.TryGetNearestReceiverDepth(camera, out float nearestReceiverDepth))
                {
                    idealRadius = nearestReceiverDepth * radiusPerDepth;
                    fitStartDistance = Mathf.Max(
                        fitStartDistance,
                        nearestReceiverDepth * ReceiverSliceStartScale);
                }
                else
                {
                    idealRadius = settings.NearClipmapRadius;
                }

                _receiverBounds.RecordAllocation(
                    camera,
                    radiusPerDepth,
                    settings.NearClipmapRadius,
                    maximumStep,
                    fitStartDistance);
            }

            int step = _receiverBounds.SelectRadiusStep(
                camera,
                LoogaShadowReceiverBounds.GetRadiusStep(
                    idealRadius,
                    settings.NearClipmapRadius,
                    maximumStep));
            return settings.NearClipmapRadius * Mathf.Pow(2f, step * 0.5f);
        }

        // Light-space bounds of a frustum slice grow with its far distance, so bisection finds the
        // longest slice whose bounds fit inside a square of the given half extent.
        private float FitFrustumSlice(
            Camera camera,
            Matrix4x4 lightWorldToLocal,
            float nearDistance,
            float farDistance,
            float halfExtent,
            out Vector3 sliceMin,
            out Vector3 sliceMax)
        {
            if (TryGetFrustumSliceBounds(camera, lightWorldToLocal, farDistance, halfExtent, out sliceMin, out sliceMax))
                return farDistance;

            // The slice at the near plane is kept even when it does not fit, so the camera stays covered.
            TryGetFrustumSliceBounds(camera, lightWorldToLocal, nearDistance, halfExtent, out sliceMin, out sliceMax);
            float low = nearDistance;
            float high = farDistance;
            for (int iteration = 0; iteration < FrustumFitIterations; iteration++)
            {
                float middle = (low + high) * 0.5f;
                if (TryGetFrustumSliceBounds(camera, lightWorldToLocal, middle, halfExtent, out Vector3 min, out Vector3 max))
                {
                    low = middle;
                    sliceMin = min;
                    sliceMax = max;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        private bool TryGetFrustumSliceBounds(
            Camera camera,
            Matrix4x4 lightWorldToLocal,
            float farDistance,
            float halfExtent,
            out Vector3 sliceMin,
            out Vector3 sliceMax)
        {
            GetLightSpaceFrustumCorners(camera, farDistance, lightWorldToLocal, _frustumCorners);
            sliceMin = Vector3.positiveInfinity;
            sliceMax = Vector3.negativeInfinity;
            for (int corner = 0; corner < 4; corner++)
            {
                sliceMin = Vector3.Min(sliceMin, Vector3.Min(_nearSliceCorners[corner], _frustumCorners[corner]));
                sliceMax = Vector3.Max(sliceMax, Vector3.Max(_nearSliceCorners[corner], _frustumCorners[corner]));
            }

            return Mathf.Max(sliceMax.x - sliceMin.x, sliceMax.y - sliceMin.y) <= halfExtent * 2f;
        }

        private static void GetLightSpaceFrustumCorners(
            Camera camera,
            float distance,
            Matrix4x4 lightWorldToLocal,
            Vector3[] corners)
        {
            camera.CalculateFrustumCorners(
                new Rect(0f, 0f, 1f, 1f),
                distance,
                Camera.MonoOrStereoscopicEye.Mono,
                corners);
            Transform cameraTransform = camera.transform;
            for (int corner = 0; corner < 4; corner++)
            {
                corners[corner] = lightWorldToLocal.MultiplyPoint3x4(
                    cameraTransform.TransformPoint(corners[corner]));
            }
        }

        // URP's per-light soft-shadow quality (0 off, 1 low, 2 medium, 3 high), as its main-light
        // pass encodes it in _MainLightShadowParams.y. Only transparent receivers sample it; opaque
        // receivers read the resolved screen-space shadow. URP keeps the pipeline-wide quality
        // internal, so lights that follow it use Low, as this pass always did.
        private static float GetSoftShadowQuality(Light light, bool supportsSoftShadows)
        {
            if (light.shadows != LightShadows.Soft || !supportsSoftShadows)
                return 0f;

            SoftShadowQuality quality = SoftShadowQuality.Low;
            if (light.TryGetComponent(out UniversalAdditionalLightData additionalLightData) &&
                additionalLightData.softShadowQuality != SoftShadowQuality.UsePipelineSettings)
            {
                quality = additionalLightData.softShadowQuality;
            }

            return Mathf.Max((int)quality, (int)SoftShadowQuality.Low);
        }

        // URP's _MainLightShadowParams: strength, soft-shadow quality, and a fade over the last tenth
        // of the shadow distance, evaluated on squared camera distance by GetMainLightShadowFade.
        private static Vector4 GetMainLightShadowParams(
            float shadowStrength,
            float softShadowQuality,
            float shadowDistance)
        {
            float shadowDistanceSquared = shadowDistance * shadowDistance;
            float fadeStartSquared = shadowDistanceSquared * 0.81f;
            float fadeRangeSquared = Mathf.Max(
                shadowDistanceSquared - fadeStartSquared,
                0.0001f);
            return new Vector4(
                shadowStrength,
                softShadowQuality,
                1f / fadeRangeSquared,
                -fadeStartSquared / fadeRangeSquared);
        }

        private static Matrix4x4 GetAtlasShadowTransform(
            Matrix4x4 view,
            Matrix4x4 projection,
            int level)
        {
            // Match URP's ShadowUtils.GetShadowTransform. SetViewProjectionMatrices applies
            // the render-target convention while drawing; receiver coordinates only need the
            // platform's reversed-Z correction here.
            Matrix4x4 shadowProjection = projection;
            if (SystemInfo.usesReversedZBuffer)
            {
                shadowProjection.m20 = -shadowProjection.m20;
                shadowProjection.m21 = -shadowProjection.m21;
                shadowProjection.m22 = -shadowProjection.m22;
                shadowProjection.m23 = -shadowProjection.m23;
            }

            Matrix4x4 textureScaleBias = Matrix4x4.identity;
            textureScaleBias.m00 = 0.5f;
            textureScaleBias.m11 = 0.5f;
            textureScaleBias.m22 = 0.5f;
            textureScaleBias.m03 = 0.5f;
            textureScaleBias.m13 = 0.5f;
            textureScaleBias.m23 = 0.5f;

            int tileX = level & 1;
            int tileY = level >> 1;
            Matrix4x4 atlasTransform = Matrix4x4.identity;
            atlasTransform.m00 = 0.5f;
            atlasTransform.m11 = 0.5f;
            atlasTransform.m03 = tileX * 0.5f;
            atlasTransform.m13 = tileY * 0.5f;
            return atlasTransform * textureScaleBias * shadowProjection * view;
        }

        private sealed class ClipmapAtlasPass : ScriptableRenderPass
        {
            private static readonly int ShadowBias = Shader.PropertyToID("_ShadowBias");
            private static readonly int LightDirection = Shader.PropertyToID("_LightDirection");
            private static readonly int LightPosition = Shader.PropertyToID("_LightPosition");
            private static readonly int WorldSpaceCameraPosition = Shader.PropertyToID("_WorldSpaceCameraPos");

            private readonly ProfilingSampler _profilingSampler = new("Looga Shadows Render Clipmaps");
            private CopyDepthPass _copyDepthPass;
            private int _mainLightIndex;
            private VisibleLight _mainLight;
            private LoogaShadowResolvedSettings _settings;
            private Matrix4x4[] _worldToShadow;
            private Matrix4x4[] _viewMatrices;
            private Matrix4x4[] _projectionMatrices;
            private Vector4[] _clipmapCenters;
            private Vector4[] _clipmapRadii;
            private Vector4[] _clipmapRects;
            private ShadowSplitData[] _clipmapSplits;
            private Material _casterCacheMaterial;
            private LoogaCachedLevelMode[] _levelModes;
            private LoogaShadowCasterCache.Level[] _cachedLevels;
            private ShadowSplitData[] _staticSplits;
            private int[] _staticSplitLevels;
            private RectInt[] _staticSplitRects;
            private int _staticSplitCount;
            private readonly TextureHandle[] _cacheHandles = new TextureHandle[MaximumClipmapCount];

            private static readonly int CasterCacheId = Shader.PropertyToID("_LoogaShadowCasterCache");
            private static readonly int CasterCacheOffsetId = Shader.PropertyToID("_LoogaShadowCasterCacheOffset");
            private const int CacheClearShaderPass = 0;
            private const int CacheUnwrapShaderPass = 1;

            public ClipmapAtlasPass()
            {
                EnsureCopyDepthPass();
            }

            public void Dispose()
            {
                _copyDepthPass?.Dispose();
                _copyDepthPass = null;
            }

            private bool EnsureCopyDepthPass()
            {
                if (_copyDepthPass != null)
                    return true;

                Shader copyDepthShader = null;
                if (GraphicsSettings.TryGetRenderPipelineSettings<UniversalRendererResources>(
                        out var rendererResources))
                {
                    copyDepthShader = rendererResources.copyDepthPS;
                }

                if (copyDepthShader == null)
                    copyDepthShader = Shader.Find(CopyDepthShaderName);

                if (copyDepthShader == null)
                    return false;

                _copyDepthPass = new CopyDepthPass(
                    RenderPassEvent.BeforeRenderingShadows,
                    copyDepthShader,
                    copyResolvedDepth: true,
                    customPassName: "Looga Shadows Copy Raw Depth");
                return true;
            }

            private sealed class PackedPassData
            {
                public readonly RendererListHandle[] RendererLists =
                    new RendererListHandle[MaximumClipmapCount];
                public readonly RTHandle[] CacheTextures = new RTHandle[MaximumClipmapCount];
                public readonly Vector4[] CacheOffsets = new Vector4[MaximumClipmapCount];
                public Material CacheMaterial;
                public MaterialPropertyBlock PropertyBlock;
                public VisibleLight MainLight;
                public LoogaShadowResolvedSettings Settings;
                public Matrix4x4[] WorldToShadow;
                public Matrix4x4[] ViewMatrices;
                public Matrix4x4[] ProjectionMatrices;
                public Vector4[] ClipmapCenters;
                public Vector4[] ClipmapRadii;
                public Matrix4x4 CameraView;
                public Matrix4x4 CameraProjection;
                public Vector3 CameraPosition;
            }

            private sealed class SeparatePassData
            {
                public RendererListHandle RendererList;
                public int Level;
                public VisibleLight MainLight;
                public LoogaShadowResolvedSettings Settings;
                public Matrix4x4[] WorldToShadow;
                public Matrix4x4[] ViewMatrices;
                public Matrix4x4[] ProjectionMatrices;
                public Vector4[] ClipmapCenters;
                public Vector4[] ClipmapRadii;
                public Matrix4x4 CameraView;
                public Matrix4x4 CameraProjection;
                public Vector3 CameraPosition;
            }

            public void Setup(
                int mainLightIndex,
                VisibleLight mainLight,
                LoogaShadowResolvedSettings settings,
                Matrix4x4[] worldToShadow,
                Matrix4x4[] viewMatrices,
                Matrix4x4[] projectionMatrices,
                Vector4[] clipmapCenters,
                Vector4[] clipmapRadii,
                Vector4[] clipmapRects,
                ShadowSplitData[] clipmapSplits)
            {
                _mainLightIndex = mainLightIndex;
                _mainLight = mainLight;
                _settings = settings;
                _worldToShadow = worldToShadow;
                _viewMatrices = viewMatrices;
                _projectionMatrices = projectionMatrices;
                _clipmapCenters = clipmapCenters;
                _clipmapRadii = clipmapRadii;
                _clipmapRects = clipmapRects;
                _clipmapSplits = clipmapSplits;
            }

            public void SetupCache(
                Material casterCacheMaterial,
                LoogaCachedLevelMode[] levelModes,
                LoogaShadowCasterCache.Level[] cachedLevels,
                ShadowSplitData[] staticSplits,
                int[] staticSplitLevels,
                RectInt[] staticSplitRects,
                int staticSplitCount)
            {
                _casterCacheMaterial = casterCacheMaterial;
                _levelModes = levelModes;
                _cachedLevels = cachedLevels;
                _staticSplits = staticSplits;
                _staticSplitLevels = staticSplitLevels;
                _staticSplitRects = staticSplitRects;
                _staticSplitCount = staticSplitCount;
            }

            // True when the level draws its static casters from its cache and only its other casters itself.
            private bool UsesCache(int level)
            {
                return _levelModes[level] != LoogaCachedLevelMode.Uncached && _cachedLevels[level] != null;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                // Blocker searches read stored depths. Where the atlas itself can be sampled as raw depth, that
                // replaces a full-atlas copy every frame.
                bool useRawShadowDepth = SupportsRawShadowDepthSampling();
                if (_mainLight.light == null ||
                    _mainLightIndex < 0 ||
                    (!useRawShadowDepth && !EnsureCopyDepthPass()))
                {
                    return;
                }

                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                CullingResults shadowCullResults = renderingData.cullResults;
                LoogaShadowFrameData shadowFrameData = frameData.GetOrCreate<LoogaShadowFrameData>();
                shadowFrameData.HasShadowCasters =
                    shadowCullResults.GetShadowCasterBounds(
                        _mainLightIndex,
                        out _);
                if (!shadowFrameData.HasShadowCasters)
                    return;

                CullClipmapShadowCasters(
                    frameData.Get<CullContextData>(),
                    shadowCullResults,
                    frameData.Get<UniversalShadowData>().supportsAdditionalLightShadows);
                RecordPackedAtlas(
                    renderGraph,
                    frameData,
                    cameraData,
                    shadowCullResults,
                    shadowFrameData,
                    useRawShadowDepth);
            }

            private void RecordPackedAtlas(
                RenderGraph renderGraph,
                ContextContainer frameData,
                UniversalCameraData cameraData,
                CullingResults shadowCullResults,
                LoogaShadowFrameData shadowFrameData,
                bool useRawShadowDepth)
            {
                RenderTextureDescriptor descriptor = new(
                    _settings.AtlasResolution,
                    _settings.AtlasResolution,
                    RenderTextureFormat.Shadowmap,
                    32)
                {
                    shadowSamplingMode = ShadowSamplingMode.CompareDepths,
                    msaaSamples = 1,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                TextureHandle atlas = renderGraph.CreateTexture(new TextureDesc(descriptor)
                {
                    name = "Looga Virtual Shadow Atlas",
                    clearBuffer = true,
                    clearColor = SystemInfo.usesReversedZBuffer ? Color.black : Color.white,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                });
                TextureHandle depthAtlas = useRawShadowDepth
                    ? atlas
                    : renderGraph.CreateTexture(new TextureDesc(
                        _settings.AtlasResolution,
                        _settings.AtlasResolution)
                    {
                        name = "Looga Virtual Shadow Raw Depth",
                        format = GraphicsFormat.R16_UNorm,
                        clearBuffer = false,
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp
                    });

                for (int level = 0; level < MaximumClipmapCount; level++)
                {
                    shadowFrameData.Clipmaps[level] = atlas;
                    shadowFrameData.DepthClipmaps[level] = depthAtlas;
                }

                RecordCasterCaches(renderGraph, cameraData, shadowCullResults);

                IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(
                    "Looga Shadows Render Clipmaps",
                    out PackedPassData passData,
                    _profilingSampler);
                passData.CacheMaterial = _casterCacheMaterial;
                passData.PropertyBlock ??= new MaterialPropertyBlock();
                for (int level = 0; level < _settings.ClipmapCount; level++)
                {
                    // A cached level takes its static casters from its cache and draws the others itself.
                    bool usesCache = UsesCache(level);
                    RendererListHandle rendererList = CreateShadowRendererList(
                        renderGraph,
                        shadowCullResults,
                        level,
                        usesCache ? ShadowObjectsFilter.DynamicOnly : ShadowObjectsFilter.AllObjects);
                    passData.RendererLists[level] = rendererList;
                    builder.UseRendererList(rendererList);
                    passData.CacheTextures[level] = usesCache ? _cachedLevels[level].Texture : null;
                    if (!usesCache)
                        continue;

                    builder.UseTexture(_cacheHandles[level], AccessFlags.Read);
                    Vector2Int wrapOffset = _cachedLevels[level].WrapOffset;
                    passData.CacheOffsets[level] = new Vector4(
                        wrapOffset.x,
                        wrapOffset.y,
                        _settings.TileResolution,
                        0f);
                }
                passData.MainLight = _mainLight;
                passData.Settings = _settings;
                passData.WorldToShadow = _worldToShadow;
                passData.ViewMatrices = _viewMatrices;
                passData.ProjectionMatrices = _projectionMatrices;
                passData.ClipmapCenters = _clipmapCenters;
                passData.ClipmapRadii = _clipmapRadii;
                passData.CameraView = cameraData.GetViewMatrix();
                passData.CameraProjection = cameraData.GetProjectionMatrix();
                passData.CameraPosition = cameraData.worldSpaceCameraPos;

                builder.SetRenderAttachmentDepth(atlas, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PackedPassData data, RasterGraphContext context) =>
                {
                    int tileResolution = data.Settings.TileResolution;
                    Vector3 direction = -data.MainLight.light.transform.forward.normalized;
                    context.cmd.SetGlobalVector(WorldSpaceCameraPosition, data.CameraPosition);
                    context.cmd.SetGlobalVector(LightDirection, new Vector4(direction.x, direction.y, direction.z, 0f));
                    context.cmd.SetGlobalVector(LightPosition, new Vector4(-direction.x, -direction.y, -direction.z, 0f));
                    context.cmd.SetKeyword(LoogaShadowShaderIds.CastingPunctualLightShadow, false);
                    context.cmd.SetGlobalDepthBias(1f, CasterSlopeBias);
                    for (int level = 0; level < data.Settings.ClipmapCount; level++)
                    {
                        int tileX = level & 1;
                        int tileY = level >> 1;
                        context.cmd.SetViewport(new Rect(
                            tileX * tileResolution,
                            tileY * tileResolution,
                            tileResolution,
                            tileResolution));
                        context.cmd.SetGlobalVector(
                            ShadowBias,
                            GetCasterShadowBias(
                                data.ClipmapRadii[level].y,
                                data.Settings));
                        context.cmd.SetViewProjectionMatrices(
                            data.ViewMatrices[level],
                            data.ProjectionMatrices[level]);
                        if (data.CacheTextures[level] != null)
                        {
                            // Writes stored depths, which already carry the caster bias.
                            context.cmd.SetGlobalDepthBias(0f, 0f);
                            data.PropertyBlock.SetTexture(CasterCacheId, data.CacheTextures[level]);
                            data.PropertyBlock.SetVector(CasterCacheOffsetId, data.CacheOffsets[level]);
                            context.cmd.DrawProcedural(
                                Matrix4x4.identity,
                                data.CacheMaterial,
                                CacheUnwrapShaderPass,
                                MeshTopology.Triangles,
                                3,
                                1,
                                data.PropertyBlock);
                            context.cmd.SetGlobalDepthBias(1f, CasterSlopeBias);
                        }
                        context.cmd.DrawRendererList(data.RendererLists[level]);
                    }
                    context.cmd.SetGlobalDepthBias(0f, 0f);
                    context.cmd.SetViewProjectionMatrices(data.CameraView, data.CameraProjection);
                    context.cmd.SetGlobalMatrixArray(
                        LoogaShadowShaderIds.WorldToShadow,
                        data.WorldToShadow);
                    context.cmd.SetGlobalVectorArray(
                        LoogaShadowShaderIds.ClipmapCenters,
                        data.ClipmapCenters);
                    context.cmd.SetGlobalVectorArray(
                        LoogaShadowShaderIds.ClipmapRadii,
                        data.ClipmapRadii);
                    context.cmd.SetGlobalInteger(
                        LoogaShadowShaderIds.ClipmapCount,
                        data.Settings.ClipmapCount);
                });
                builder.Dispose();

                if (!useRawShadowDepth)
                {
                    _copyDepthPass.Render(
                        renderGraph,
                        frameData,
                        depthAtlas,
                        atlas,
                        passName: "Looga Shadows Copy Raw Depth");
                }
            }

            private sealed class CachePassData
            {
                // A rect of a level crosses the cache's wrap seams in at most two places, so it draws in up to
                // four pieces, each with the projection of its part of the level. A renderer list executes once,
                // so each piece has its own list of the rect's split.
                public const int MaximumPieces = LoogaShadowCasterCache.MaximumExposedRects * 4;
                public readonly RendererListHandle[] RendererLists = new RendererListHandle[MaximumPieces];
                public readonly Rect[] Viewports = new Rect[MaximumPieces];
                public readonly Matrix4x4[] Projections = new Matrix4x4[MaximumPieces];
                public int PieceCount;
                public Matrix4x4 View;
                public Vector4 ShadowBias;
                public Material Material;
                public Vector3 LightDirection;
                public Vector3 CameraPosition;
                public Matrix4x4 CameraView;
                public Matrix4x4 CameraProjection;
            }

            // Redraws the exposed rects of each cached level into its cache, and marks what the caches hold.
            private void RecordCasterCaches(
                RenderGraph renderGraph,
                UniversalCameraData cameraData,
                CullingResults shadowCullResults)
            {
                for (int level = 0; level < MaximumClipmapCount; level++)
                {
                    _cacheHandles[level] = TextureHandle.nullHandle;
                    if (level >= _settings.ClipmapCount || !UsesCache(level))
                        continue;

                    LoogaShadowCasterCache.Level entry = _cachedLevels[level];
                    _cacheHandles[level] = renderGraph.ImportTexture(entry.Texture);
                    if (entry.Mode == LoogaCachedLevelMode.Reuse)
                    {
                        LoogaShadowCasterCache.Commit(entry);
                        continue;
                    }

                    using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(
                        "Looga Shadows Cache Static Casters",
                        out CachePassData passData,
                        _profilingSampler);
                    int resolution = _settings.TileResolution;
                    float radius = _clipmapRadii[level].x;
                    passData.PieceCount = 0;
                    for (int split = 0; split < _staticSplitCount; split++)
                    {
                        if (_staticSplitLevels[split] != level)
                            continue;

                        int firstPiece = passData.PieceCount;
                        AddCachePieces(
                            passData,
                            _staticSplitRects[split],
                            entry.WrapOffset,
                            resolution,
                            radius,
                            _settings.DepthRange);
                        for (int piece = firstPiece; piece < passData.PieceCount; piece++)
                        {
                            RendererListHandle rendererList = CreateShadowRendererList(
                                renderGraph,
                                shadowCullResults,
                                _settings.ClipmapCount + split,
                                ShadowObjectsFilter.StaticOnly);
                            builder.UseRendererList(rendererList);
                            passData.RendererLists[piece] = rendererList;
                        }
                    }

                    passData.View = _viewMatrices[level];
                    passData.ShadowBias = GetCasterShadowBias(_clipmapRadii[level].y, _settings);
                    passData.Material = _casterCacheMaterial;
                    passData.LightDirection = -_mainLight.light.transform.forward.normalized;
                    passData.CameraPosition = cameraData.worldSpaceCameraPos;
                    passData.CameraView = cameraData.GetViewMatrix();
                    passData.CameraProjection = cameraData.GetProjectionMatrix();
                    builder.SetRenderAttachmentDepth(_cacheHandles[level], AccessFlags.ReadWrite);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (CachePassData data, RasterGraphContext context) =>
                    {
                        Vector3 direction = data.LightDirection;
                        context.cmd.SetGlobalVector(WorldSpaceCameraPosition, data.CameraPosition);
                        context.cmd.SetGlobalVector(LightDirection, new Vector4(direction.x, direction.y, direction.z, 0f));
                        context.cmd.SetGlobalVector(LightPosition, new Vector4(-direction.x, -direction.y, -direction.z, 0f));
                        context.cmd.SetKeyword(LoogaShadowShaderIds.CastingPunctualLightShadow, false);
                        context.cmd.SetGlobalVector(ShadowBias, data.ShadowBias);
                        for (int piece = 0; piece < data.PieceCount; piece++)
                        {
                            context.cmd.SetViewport(data.Viewports[piece]);
                            context.cmd.SetGlobalDepthBias(0f, 0f);
                            context.cmd.DrawProcedural(
                                Matrix4x4.identity,
                                data.Material,
                                CacheClearShaderPass,
                                MeshTopology.Triangles,
                                3);
                            context.cmd.SetGlobalDepthBias(1f, CasterSlopeBias);
                            context.cmd.SetViewProjectionMatrices(data.View, data.Projections[piece]);
                            context.cmd.DrawRendererList(data.RendererLists[piece]);
                        }
                        context.cmd.SetGlobalDepthBias(0f, 0f);
                        context.cmd.SetViewProjectionMatrices(data.CameraView, data.CameraProjection);
                    });
                    LoogaShadowCasterCache.Commit(entry);
                }
            }

            // Level texel p is stored at cache texel (p + wrapOffset) mod resolution. A level rect therefore splits
            // at the level texel that wraps to zero, on each axis.
            private static void AddCachePieces(
                CachePassData data,
                RectInt rect,
                Vector2Int wrapOffset,
                int resolution,
                float radius,
                float depthRange)
            {
                int seamX = resolution - wrapOffset.x;
                int seamY = resolution - wrapOffset.y;
                for (int sideY = 0; sideY < 2; sideY++)
                {
                    int yMin = sideY == 0 ? rect.yMin : Mathf.Max(rect.yMin, seamY);
                    int yMax = sideY == 0 ? Mathf.Min(rect.yMax, seamY) : rect.yMax;
                    if (yMax <= yMin)
                        continue;

                    for (int sideX = 0; sideX < 2; sideX++)
                    {
                        int xMin = sideX == 0 ? rect.xMin : Mathf.Max(rect.xMin, seamX);
                        int xMax = sideX == 0 ? Mathf.Min(rect.xMax, seamX) : rect.xMax;
                        if (xMax <= xMin)
                            continue;

                        // The piece's part of the level's orthographic box, with the level's depth range.
                        int piece = data.PieceCount++;
                        int cacheX = xMin + wrapOffset.x - (sideX == 0 ? 0 : resolution);
                        int cacheY = yMin + wrapOffset.y - (sideY == 0 ? 0 : resolution);
                        data.Viewports[piece] = new Rect(cacheX, cacheY, xMax - xMin, yMax - yMin);
                        float scale = 2f * radius / resolution;
                        data.Projections[piece] = Matrix4x4.Ortho(
                            -radius + xMin * scale,
                            -radius + xMax * scale,
                            -radius + yMin * scale,
                            -radius + yMax * scale,
                            0.01f,
                            depthRange);
                    }
                }
            }

            private void RecordAsymmetricClipmaps(
                RenderGraph renderGraph,
                ContextContainer frameData,
                UniversalCameraData cameraData,
                CullingResults shadowCullResults,
                LoogaShadowFrameData shadowFrameData,
                bool useRawShadowDepth)
            {
                for (int level = 0; level < _settings.ClipmapCount; level++)
                {
                    int resolution = _settings.GetClipmapResolution(level);
                    RenderTextureDescriptor descriptor = new(
                        resolution,
                        resolution,
                        RenderTextureFormat.Shadowmap,
                        32)
                    {
                        shadowSamplingMode = ShadowSamplingMode.CompareDepths,
                        msaaSamples = 1,
                        useMipMap = false,
                        autoGenerateMips = false
                    };
                    TextureHandle clipmap = renderGraph.CreateTexture(new TextureDesc(descriptor)
                    {
                        name = $"Looga Virtual Shadow Clipmap {level}",
                        clearBuffer = true,
                        clearColor = SystemInfo.usesReversedZBuffer ? Color.black : Color.white,
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp
                    });
                    TextureHandle depthClipmap = useRawShadowDepth
                        ? clipmap
                        : renderGraph.CreateTexture(new TextureDesc(resolution, resolution)
                        {
                            name = $"Looga Virtual Shadow Raw Depth {level}",
                            format = GraphicsFormat.R16_UNorm,
                            clearBuffer = false,
                            filterMode = FilterMode.Point,
                            wrapMode = TextureWrapMode.Clamp
                        });
                    shadowFrameData.Clipmaps[level] = clipmap;
                    shadowFrameData.DepthClipmaps[level] = depthClipmap;

                    IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(
                        $"Looga Shadows Render Clipmap {level}",
                        out SeparatePassData passData,
                        _profilingSampler);
                    passData.RendererList = CreateShadowRendererList(
                        renderGraph,
                        shadowCullResults,
                        level);
                    builder.UseRendererList(passData.RendererList);
                    passData.Level = level;
                    passData.MainLight = _mainLight;
                    passData.Settings = _settings;
                    passData.WorldToShadow = _worldToShadow;
                    passData.ViewMatrices = _viewMatrices;
                    passData.ProjectionMatrices = _projectionMatrices;
                    passData.ClipmapCenters = _clipmapCenters;
                    passData.ClipmapRadii = _clipmapRadii;
                    passData.CameraView = cameraData.GetViewMatrix();
                    passData.CameraProjection = cameraData.GetProjectionMatrix();
                    passData.CameraPosition = cameraData.worldSpaceCameraPos;

                    builder.SetRenderAttachmentDepth(clipmap, AccessFlags.Write);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (SeparatePassData data, RasterGraphContext context) =>
                    {
                        int level = data.Level;
                        int resolution = data.Settings.GetClipmapResolution(level);
                        Vector3 direction = -data.MainLight.light.transform.forward.normalized;
                        context.cmd.SetGlobalVector(WorldSpaceCameraPosition, data.CameraPosition);
                        context.cmd.SetGlobalVector(LightDirection, new Vector4(direction.x, direction.y, direction.z, 0f));
                        context.cmd.SetGlobalVector(LightPosition, new Vector4(-direction.x, -direction.y, -direction.z, 0f));
                        context.cmd.SetKeyword(LoogaShadowShaderIds.CastingPunctualLightShadow, false);
                        context.cmd.SetViewport(new Rect(0f, 0f, resolution, resolution));
                        context.cmd.SetGlobalDepthBias(1f, CasterSlopeBias);
                        context.cmd.SetGlobalVector(
                            ShadowBias,
                            GetCasterShadowBias(
                                data.ClipmapRadii[level].y,
                                data.Settings));
                        context.cmd.SetViewProjectionMatrices(
                            data.ViewMatrices[level],
                            data.ProjectionMatrices[level]);
                        context.cmd.DrawRendererList(data.RendererList);
                        context.cmd.SetGlobalDepthBias(0f, 0f);
                        context.cmd.SetViewProjectionMatrices(
                            data.CameraView,
                            data.CameraProjection);
                        context.cmd.SetGlobalMatrixArray(
                            LoogaShadowShaderIds.WorldToShadow,
                            data.WorldToShadow);
                        context.cmd.SetGlobalVectorArray(
                            LoogaShadowShaderIds.ClipmapCenters,
                            data.ClipmapCenters);
                        context.cmd.SetGlobalVectorArray(
                            LoogaShadowShaderIds.ClipmapRadii,
                            data.ClipmapRadii);
                        context.cmd.SetGlobalInteger(
                            LoogaShadowShaderIds.ClipmapCount,
                            data.Settings.ClipmapCount);
                    });
                    builder.Dispose();

                    if (!useRawShadowDepth)
                    {
                        _copyDepthPass.Render(
                            renderGraph,
                            frameData,
                            depthClipmap,
                            clipmap,
                            passName: $"Looga Shadows Copy Raw Depth {level}");
                    }
                }

                for (int level = _settings.ClipmapCount; level < MaximumClipmapCount; level++)
                {
                    shadowFrameData.Clipmaps[level] = shadowFrameData.Clipmaps[0];
                    shadowFrameData.DepthClipmaps[level] = shadowFrameData.DepthClipmaps[0];
                }
            }

            // The resolve reads stored depth through its own point sampler, so the atlas's comparison sampler does
            // not matter where textures and samplers are separate. Unity's flag also reports false for Direct3D 12.
            private static bool SupportsRawShadowDepthSampling()
            {
                if (SystemInfo.supportsRawShadowDepthSampling)
                    return true;

                GraphicsDeviceType device = SystemInfo.graphicsDeviceType;
                return device != GraphicsDeviceType.OpenGLCore &&
                    device != GraphicsDeviceType.OpenGLES3 &&
                    device != GraphicsDeviceType.Null;
            }

            internal static Vector4 GetCasterShadowBias(
                float worldTexelSize,
                LoogaShadowResolvedSettings settings)
            {
                // Filter softness must not inflate per-vertex displacement.
                // A larger displacement opens gaps along mesh-normal seams.
                // The normal offset follows the texel size of each clipmap
                // level, because blended levels need the same protection.
                float depthBias = Mathf.Max(
                    settings.DepthBias,
                    worldTexelSize * 0.02f);
                float normalBias = Mathf.Max(
                    settings.NormalBias,
                    worldTexelSize * CasterNormalBiasTexels);
                return new Vector4(
                    -depthBias,
                    -normalBias,
                    (float)LightType.Directional,
                    0f);
            }

            // Culls the main light's casters with one split per clipmap level instead of URP's
            // cascades, so each level draws exactly the casters inside its own box, once, and
            // BatchRendererGroup culling callbacks receive the clipmap levels as their splits.
            // URP culled every shadowed light earlier in the frame, and a second call replaces the
            // casters of all lights, so the spot and point lights get URP's splits again.
            private void CullClipmapShadowCasters(
                CullContextData cullContextData,
                CullingResults cullResults,
                bool cullAdditionalLights)
            {
                NativeArray<VisibleLight> visibleLights = cullResults.visibleLights;
                NativeArray<LightShadowCasterCullingInfo> perLightInfos =
                    new(visibleLights.Length, Allocator.Temp);
                int mainLightSplitCount = _settings.ClipmapCount + _staticSplitCount;
                NativeArray<ShadowSplitData> splitBuffer = new(
                    mainLightSplitCount + visibleLights.Length * PointLightShadowSplitCount,
                    Allocator.Temp);
                int splitCount = 0;
                for (int level = 0; level < _settings.ClipmapCount; level++)
                    splitBuffer[splitCount++] = _clipmapSplits[level];
                for (int index = 0; index < _staticSplitCount; index++)
                    splitBuffer[splitCount++] = _staticSplits[index];

                perLightInfos[_mainLightIndex] = new LightShadowCasterCullingInfo
                {
                    splitRange = new RangeInt(0, mainLightSplitCount),
                    projectionType = BatchCullingProjectionType.Orthographic
                };

                for (int lightIndex = 0; cullAdditionalLights && lightIndex < visibleLights.Length; lightIndex++)
                {
                    VisibleLight visibleLight = visibleLights[lightIndex];
                    if (lightIndex == _mainLightIndex ||
                        visibleLight.light == null ||
                        visibleLight.light.shadows == LightShadows.None)
                    {
                        continue;
                    }

                    int firstSplit = splitCount;
                    if (visibleLight.lightType == LightType.Spot)
                    {
                        cullResults.ComputeSpotShadowMatricesAndCullingPrimitives(
                            lightIndex,
                            out _,
                            out _,
                            out ShadowSplitData splitData);
                        splitBuffer[splitCount++] = splitData;
                    }
                    else if (visibleLight.lightType == LightType.Point)
                    {
                        for (int face = 0; face < PointLightShadowSplitCount; face++)
                        {
                            cullResults.ComputePointShadowMatricesAndCullingPrimitives(
                                lightIndex,
                                (CubemapFace)face,
                                PointLightCullingFovBias,
                                out _,
                                out _,
                                out ShadowSplitData splitData);
                            splitBuffer[splitCount++] = splitData;
                        }
                    }
                    else
                    {
                        continue;
                    }

                    perLightInfos[lightIndex] = new LightShadowCasterCullingInfo
                    {
                        splitRange = new RangeInt(firstSplit, splitCount - firstSplit),
                        projectionType = BatchCullingProjectionType.Perspective
                    };
                }

                // GPU-driven instancing measures casters in each level's texels: it skips casters too small for
                // a level and lowers their LOD there. The culling context has no shadow-map resolution.
                System.Span<float> texelSizes = stackalloc float[mainLightSplitCount];
                for (int level = 0; level < _settings.ClipmapCount; level++)
                    texelSizes[level] = _clipmapRadii[level].y;
                for (int index = 0; index < _staticSplitCount; index++)
                    texelSizes[_settings.ClipmapCount + index] = _clipmapRadii[_staticSplitLevels[index]].y;
                // Cached levels draw static casters from their caches, so static instancing skips their splits.
                int dynamicOnlySplits = 0;
                for (int level = 0; level < _settings.ClipmapCount; level++)
                {
                    if (UsesCache(level))
                        dynamicOnlySplits |= 1 << level;
                }
                LoogaSoft.Instancing.InstanceShadowSplits.SetTexelSizes(
                    _mainLight.light,
                    texelSizes,
                    dynamicOnlySplits);

                cullContextData.CullShadowCasters(
                    cullResults,
                    new ShadowCastersCullingInfos
                    {
                        perLightInfos = perLightInfos,
                        splitBuffer = splitBuffer.GetSubArray(0, splitCount)
                    });
            }

            private RendererListHandle CreateShadowRendererList(
                RenderGraph renderGraph,
                CullingResults shadowCullResults,
                int splitIndex,
                ShadowObjectsFilter objectsFilter = ShadowObjectsFilter.AllObjects)
            {
                // The split's culling data and projection come from CullClipmapShadowCasters.
                ShadowDrawingSettings shadowDrawingSettings = new(
                    shadowCullResults,
                    _mainLightIndex)
                {
                    splitIndex = splitIndex,
                    objectsFilter = objectsFilter,
                    useRenderingLayerMaskTest =
                        UniversalRenderPipeline.asset != null &&
                        UniversalRenderPipeline.asset.useRenderingLayers
                };
                return renderGraph.CreateShadowRendererList(
                    ref shadowDrawingSettings);
            }

        }

        private sealed class ResolvePass : ScriptableRenderPass
        {
            private const int ResolveShaderPass = 0;
            private const int DenoiseShaderPass = 8;
            private const int RefilterShaderPass = 9;
            private readonly ProfilingSampler _profilingSampler = new("Looga Shadows Resolve Virtual Clipmaps");
            private Material _material;
            private LoogaShadowReceiverBounds _receiverBounds;
            private LoogaShadowResolvedSettings _settings;
            private Matrix4x4[] _worldToShadow;
            private Vector4[] _clipmapCenters;
            private Vector4[] _clipmapRadii;
            private Vector4[] _clipmapRects;
            private Vector3 _lightDirection;
            private Vector4 _mainLightShadowParams;
            private LoogaShadowNormalsSource _normalsSource;
            private bool _requiresCameraNormals;
            private bool _normalsOctEncoded;

            private sealed class PassData
            {
                public Vector4 MainLightShadowParams;
                public TextureHandle Clipmap0;
                public TextureHandle Clipmap1;
                public TextureHandle Clipmap2;
                public TextureHandle Clipmap3;
                public TextureHandle DepthClipmap0;
                public TextureHandle DepthClipmap1;
                public TextureHandle DepthClipmap2;
                public TextureHandle DepthClipmap3;
                public TextureHandle RawTarget;
                public TextureHandle DenoiseTarget;
                public TextureHandle Target;
                public TextureHandle BlockerTarget;
                public TextureHandle BlockerDenoiseTarget;
                public TextureHandle CameraDepth;
                public TextureHandle CameraNormals;
                public Material Material;
                public LoogaShadowResolvedSettings Settings;
                public Matrix4x4[] WorldToShadow;
                public Vector4[] ClipmapCenters;
                public Vector4[] ClipmapRadii;
                public Vector4[] ClipmapRects;
                public Vector3 LightDirection;
                public LoogaShadowNormalsSource NormalsSource;
                public bool RequiresCameraNormals;
                public bool NormalsOctEncoded;
            }

            // One bilateral denoise pass along a direction in pixels of the source texture.
            private static void DenoiseInto(
                UnsafeCommandBuffer command,
                Material material,
                RTHandle source,
                RTHandle destination,
                Vector4 direction)
            {
                command.SetRenderTarget(
                    destination,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.Store);
                command.SetGlobalVector(LoogaShadowShaderIds.DenoiseDirection, direction);
                Blitter.BlitTexture(command, source, Vector2.one, material, DenoiseShaderPass);
            }

            private static Vector4 GetTargetSize(RTHandle target)
            {
                int width = target.rt.width;
                int height = target.rt.height;
                return new Vector4(width, height, 1f / width, 1f / height);
            }

            private sealed class FullyLitPassData
            {
            }

            public void Setup(
                Material material,
                LoogaShadowReceiverBounds receiverBounds,
                LoogaShadowResolvedSettings settings,
                Matrix4x4[] worldToShadow,
                Vector4[] clipmapCenters,
                Vector4[] clipmapRadii,
                Vector4[] clipmapRects,
                Vector3 lightDirection,
                Vector4 mainLightShadowParams,
                bool usesDeferredLighting,
                bool usesAccurateGBufferNormals)
            {
                _material = material;
                _receiverBounds = receiverBounds;
                _settings = settings;
                _worldToShadow = worldToShadow;
                _clipmapCenters = clipmapCenters;
                _clipmapRadii = clipmapRadii;
                _clipmapRects = clipmapRects;
                _lightDirection = lightDirection;
                _mainLightShadowParams = mainLightShadowParams;
                _normalsSource = GetEffectiveNormalsSource(
                    settings.NormalsSource,
                    usesDeferredLighting);
                _requiresCameraNormals =
                    _normalsSource != LoogaShadowNormalsSource.ReconstructFromDepth;
                _normalsOctEncoded =
                    _requiresCameraNormals && usesAccurateGBufferNormals;
                _material.DisableKeyword("_LOOGA_SEPARATE_CLIPMAPS");
                ConfigureInput(GetRequiredInputs(_normalsSource));
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null)
                    return;

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                if (!frameData.Contains<LoogaShadowFrameData>())
                    return;

                LoogaShadowFrameData shadowFrameData = frameData.Get<LoogaShadowFrameData>();
                if (!shadowFrameData.HasShadowCasters)
                {
                    RecordFullyLitShadow(
                        renderGraph,
                        cameraData,
                        shadowFrameData);
                    return;
                }

                TextureHandle clipmap0 = shadowFrameData.Clipmaps[0];
                TextureHandle clipmap1 = shadowFrameData.Clipmaps[1];
                TextureHandle clipmap2 = shadowFrameData.Clipmaps[2];
                TextureHandle clipmap3 = shadowFrameData.Clipmaps[3];
                TextureHandle depthClipmap0 = shadowFrameData.DepthClipmaps[0];
                TextureHandle depthClipmap1 = shadowFrameData.DepthClipmaps[1];
                TextureHandle depthClipmap2 = shadowFrameData.DepthClipmaps[2];
                TextureHandle depthClipmap3 = shadowFrameData.DepthClipmaps[3];
                TextureHandle cameraDepth = resourceData.cameraDepthTexture.IsValid()
                    ? resourceData.cameraDepthTexture
                    : resourceData.activeDepthTexture;
                TextureHandle cameraNormals = GetNormalsTexture(
                    resourceData,
                    _normalsSource);
                if (!clipmap0.IsValid() || !clipmap1.IsValid() ||
                    !clipmap2.IsValid() || !clipmap3.IsValid() ||
                    !depthClipmap0.IsValid() || !depthClipmap1.IsValid() ||
                    !depthClipmap2.IsValid() || !depthClipmap3.IsValid() ||
                    !cameraDepth.IsValid() ||
                    (_requiresCameraNormals && !cameraNormals.IsValid()))
                    return;

                _receiverBounds?.RecordPass(renderGraph, cameraData, cameraDepth);

                RenderTextureDescriptor descriptor = cameraData.cameraTargetDescriptor;
                descriptor.depthStencilFormat = GraphicsFormat.None;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.graphicsFormat = GraphicsFormat.R16G16_SFloat;
                descriptor.useMipMap = false;
                descriptor.autoGenerateMips = false;
                TextureHandle rawTarget = renderGraph.CreateTexture(new TextureDesc(descriptor)
                {
                    name = "Looga Main Light Shadow Raw",
                    clearBuffer = true,
                    clearColor = Color.white,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                });
                TextureHandle denoiseTarget = renderGraph.CreateTexture(new TextureDesc(descriptor)
                {
                    name = "Looga Main Light Shadow Horizontal Reconstruction",
                    clearBuffer = true,
                    clearColor = Color.white,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                });
                TextureHandle target = renderGraph.CreateTexture(new TextureDesc(descriptor)
                {
                    name = "Looga Main Light Shadow",
                    clearBuffer = true,
                    clearColor = Color.white,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                });

                // The blocker search only estimates penumbra widths, which its denoise smooths over many pixels
                // anyway, so it runs at half resolution. B keeps each texel's eye depth for the upsample.
                RenderTextureDescriptor halfDescriptor = descriptor;
                halfDescriptor.width = Mathf.Max(1, (descriptor.width + 1) / 2);
                halfDescriptor.height = Mathf.Max(1, (descriptor.height + 1) / 2);
                halfDescriptor.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                TextureHandle blockerTarget = renderGraph.CreateTexture(new TextureDesc(halfDescriptor)
                {
                    name = "Looga Main Light Shadow Penumbra",
                    clearBuffer = false,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                });
                TextureHandle blockerDenoiseTarget = renderGraph.CreateTexture(new TextureDesc(halfDescriptor)
                {
                    name = "Looga Main Light Shadow Penumbra Reconstruction",
                    clearBuffer = false,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                });

                shadowFrameData.RawVisibility = rawTarget;
                shadowFrameData.ResolvedVisibility = target;

                using IUnsafeRenderGraphBuilder builder = renderGraph.AddUnsafePass(
                    "Looga Shadows Resolve Virtual Clipmaps",
                    out PassData passData,
                    _profilingSampler);

                passData.Clipmap0 = clipmap0;
                passData.Clipmap1 = clipmap1;
                passData.Clipmap2 = clipmap2;
                passData.Clipmap3 = clipmap3;
                passData.DepthClipmap0 = depthClipmap0;
                passData.DepthClipmap1 = depthClipmap1;
                passData.DepthClipmap2 = depthClipmap2;
                passData.DepthClipmap3 = depthClipmap3;
                passData.RawTarget = rawTarget;
                passData.DenoiseTarget = denoiseTarget;
                passData.Target = target;
                passData.BlockerTarget = blockerTarget;
                passData.BlockerDenoiseTarget = blockerDenoiseTarget;
                passData.CameraDepth = cameraDepth;
                passData.CameraNormals = cameraNormals;
                passData.Material = _material;
                passData.Settings = _settings;
                passData.WorldToShadow = _worldToShadow;
                passData.ClipmapCenters = _clipmapCenters;
                passData.ClipmapRadii = _clipmapRadii;
                passData.ClipmapRects = _clipmapRects;
                passData.LightDirection = _lightDirection;
                passData.NormalsSource = _normalsSource;
                passData.RequiresCameraNormals = _requiresCameraNormals;
                passData.NormalsOctEncoded = _normalsOctEncoded;
                passData.MainLightShadowParams = _mainLightShadowParams;

                builder.UseAllGlobalTextures(true);
                builder.UseTexture(clipmap0, AccessFlags.Read);
                builder.UseTexture(clipmap1, AccessFlags.Read);
                builder.UseTexture(clipmap2, AccessFlags.Read);
                builder.UseTexture(clipmap3, AccessFlags.Read);
                builder.UseTexture(depthClipmap0, AccessFlags.Read);
                builder.UseTexture(depthClipmap1, AccessFlags.Read);
                builder.UseTexture(depthClipmap2, AccessFlags.Read);
                builder.UseTexture(depthClipmap3, AccessFlags.Read);
                builder.UseTexture(rawTarget, AccessFlags.ReadWrite);
                builder.UseTexture(denoiseTarget, AccessFlags.ReadWrite);
                builder.UseTexture(target, AccessFlags.ReadWrite);
                builder.UseTexture(blockerTarget, AccessFlags.ReadWrite);
                builder.UseTexture(blockerDenoiseTarget, AccessFlags.ReadWrite);
                builder.UseTexture(cameraDepth, AccessFlags.Read);
                if (_requiresCameraNormals)
                    builder.UseTexture(cameraNormals, AccessFlags.Read);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetGlobalTextureAfterPass(target, LoogaShadowShaderIds.MainLightShadowTexture);
                builder.SetGlobalTextureAfterPass(target, LoogaShadowShaderIds.UrpScreenSpaceShadowTexture);
                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    RTHandle clipmap0 = data.Clipmap0;
                    RTHandle clipmap1 = data.Clipmap1;
                    RTHandle clipmap2 = data.Clipmap2;
                    RTHandle clipmap3 = data.Clipmap3;
                    RTHandle depthClipmap0 = data.DepthClipmap0;
                    RTHandle depthClipmap1 = data.DepthClipmap1;
                    RTHandle depthClipmap2 = data.DepthClipmap2;
                    RTHandle depthClipmap3 = data.DepthClipmap3;
                    RTHandle rawTarget = data.RawTarget;
                    RTHandle denoiseTarget = data.DenoiseTarget;
                    RTHandle target = data.Target;
                    RTHandle blockerTarget = data.BlockerTarget;
                    RTHandle blockerDenoiseTarget = data.BlockerDenoiseTarget;
                    RTHandle cameraDepth = data.CameraDepth;
                    RTHandle cameraNormals = data.RequiresCameraNormals
                        ? data.CameraNormals
                        : null;
                    context.cmd.SetGlobalTexture(
                        LoogaShadowShaderIds.ShadowDepthTexture,
                        cameraDepth);
                    context.cmd.SetGlobalVector(LoogaShadowShaderIds.ShadowDepthTexelSize,
                        new Vector4(1f / cameraDepth.rt.width, 1f / cameraDepth.rt.height,
                            cameraDepth.rt.width, cameraDepth.rt.height));
                    if (cameraNormals != null)
                    {
                        context.cmd.SetGlobalTexture(
                            LoogaShadowShaderIds.CameraNormalsTexture,
                            cameraNormals);
                    }
                    context.cmd.SetRenderTarget(
                        rawTarget,
                        RenderBufferLoadAction.DontCare,
                        RenderBufferStoreAction.Store);
                    context.cmd.SetGlobalTexture(LoogaShadowShaderIds.VirtualShadowClipmaps[0], clipmap0);
                    context.cmd.SetGlobalTexture(LoogaShadowShaderIds.VirtualShadowClipmaps[1], clipmap1);
                    context.cmd.SetGlobalTexture(LoogaShadowShaderIds.VirtualShadowClipmaps[2], clipmap2);
                    context.cmd.SetGlobalTexture(LoogaShadowShaderIds.VirtualShadowClipmaps[3], clipmap3);
                    context.cmd.SetGlobalTexture(LoogaShadowShaderIds.VirtualShadowDepthClipmaps[0], depthClipmap0);
                    context.cmd.SetGlobalTexture(LoogaShadowShaderIds.VirtualShadowDepthClipmaps[1], depthClipmap1);
                    context.cmd.SetGlobalTexture(LoogaShadowShaderIds.VirtualShadowDepthClipmaps[2], depthClipmap2);
                    context.cmd.SetGlobalTexture(LoogaShadowShaderIds.VirtualShadowDepthClipmaps[3], depthClipmap3);
                    context.cmd.SetGlobalTexture(LoogaShadowShaderIds.VirtualShadowAtlas, clipmap0);
                    context.cmd.SetGlobalTexture(LoogaShadowShaderIds.VirtualShadowDepthAtlas, depthClipmap0);
                    ApplySettings(context.cmd, data);
                    // Half resolution: blocker search, then its denoise. The passes end in blockerTarget.
                    context.cmd.SetRenderTarget(
                        blockerTarget,
                        RenderBufferLoadAction.DontCare,
                        RenderBufferStoreAction.Store);
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.ResolveTargetSize,
                        GetTargetSize(blockerTarget));
                    Blitter.BlitTexture(context.cmd, depthClipmap0, Vector2.one, data.Material, ResolveShaderPass);
                    DenoiseInto(context.cmd, data.Material, blockerTarget, blockerDenoiseTarget, new Vector4(1f, 0f, 0f, 0f));
                    DenoiseInto(context.cmd, data.Material, blockerDenoiseTarget, blockerTarget, new Vector4(0f, 1f, 0f, 0f));
                    DenoiseInto(context.cmd, data.Material, blockerTarget, blockerDenoiseTarget, new Vector4(2f, 0f, 0f, 0f));
                    DenoiseInto(context.cmd, data.Material, blockerDenoiseTarget, blockerTarget, new Vector4(0f, 2f, 0f, 0f));

                    // Full resolution: filter with the upsampled penumbra, then denoise it.
                    context.cmd.SetRenderTarget(
                        rawTarget,
                        RenderBufferLoadAction.DontCare,
                        RenderBufferStoreAction.Store);
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.ResolveTargetSize,
                        GetTargetSize(rawTarget));
                    Blitter.BlitTexture(
                        context.cmd,
                        blockerTarget,
                        Vector2.one,
                        data.Material,
                        RefilterShaderPass);
                    context.cmd.SetRenderTarget(
                        denoiseTarget,
                        RenderBufferLoadAction.DontCare,
                        RenderBufferStoreAction.Store);
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.DenoiseDirection,
                        new Vector4(1f, 0f, 0f, 0f));
                    Blitter.BlitTexture(
                        context.cmd,
                        rawTarget,
                        Vector2.one,
                        data.Material,
                        DenoiseShaderPass);
                    context.cmd.SetRenderTarget(
                        target,
                        RenderBufferLoadAction.DontCare,
                        RenderBufferStoreAction.Store);
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.DenoiseDirection,
                        new Vector4(0f, 1f, 0f, 0f));
                    Blitter.BlitTexture(
                        context.cmd,
                        denoiseTarget,
                        Vector2.one,
                        data.Material,
                        DenoiseShaderPass);
                    context.cmd.SetGlobalInteger(LoogaShadowShaderIds.ShadowsEnabled, 1);
                    // URP's lit shaders fade the screen-space shadow by these parameters. URP fills them
                    // from its own shadow distance, so replace them with the Looga shadow distance.
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.UrpMainLightShadowParams,
                        data.MainLightShadowParams);
                    context.cmd.SetKeyword(LoogaShadowShaderIds.MainLightShadows, false);
                    context.cmd.SetKeyword(LoogaShadowShaderIds.MainLightShadowCascades, false);
                    context.cmd.SetKeyword(LoogaShadowShaderIds.MainLightShadowScreen, true);
                });
            }

            private static void RecordFullyLitShadow(
                RenderGraph renderGraph,
                UniversalCameraData cameraData,
                LoogaShadowFrameData shadowFrameData)
            {
                RenderTextureDescriptor descriptor =
                    cameraData.cameraTargetDescriptor;
                descriptor.depthStencilFormat = GraphicsFormat.None;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.graphicsFormat = GraphicsFormat.R16G16_SFloat;
                descriptor.useMipMap = false;
                descriptor.autoGenerateMips = false;
                TextureHandle target = renderGraph.CreateTexture(
                    new TextureDesc(descriptor)
                    {
                        name = "Looga Main Light Shadow Fully Lit",
                        clearBuffer = true,
                        clearColor = new Color(1f, 0f, 0f, 1f),
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp
                    });

                shadowFrameData.RawVisibility = target;
                shadowFrameData.ResolvedVisibility = target;

                using IRasterRenderGraphBuilder builder =
                    renderGraph.AddRasterRenderPass(
                        "Looga Shadows No Visible Casters",
                        out FullyLitPassData _);
                builder.SetRenderAttachment(target, 0, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetGlobalTextureAfterPass(
                    target,
                    LoogaShadowShaderIds.MainLightShadowTexture);
                builder.SetGlobalTextureAfterPass(
                    target,
                    LoogaShadowShaderIds.UrpScreenSpaceShadowTexture);
                builder.SetRenderFunc(static (
                    FullyLitPassData _,
                    RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalInteger(
                        LoogaShadowShaderIds.ShadowsEnabled,
                        1);
                    context.cmd.SetKeyword(
                        LoogaShadowShaderIds.MainLightShadows,
                        false);
                    context.cmd.SetKeyword(
                        LoogaShadowShaderIds.MainLightShadowCascades,
                        false);
                    context.cmd.SetKeyword(
                        LoogaShadowShaderIds.MainLightShadowScreen,
                        true);
                });
            }

            private static void ApplySettings(IBaseCommandBuffer command, PassData data)
            {
                LoogaShadowResolvedSettings settings = data.Settings;
                command.SetGlobalMatrixArray(LoogaShadowShaderIds.WorldToShadow, data.WorldToShadow);
                command.SetGlobalVectorArray(LoogaShadowShaderIds.ClipmapCenters, data.ClipmapCenters);
                command.SetGlobalVectorArray(LoogaShadowShaderIds.ClipmapRadii, data.ClipmapRadii);
                command.SetGlobalVectorArray(LoogaShadowShaderIds.ClipmapRects, data.ClipmapRects);
                command.SetGlobalInteger(LoogaShadowShaderIds.ClipmapCount, settings.ClipmapCount);
                command.SetGlobalVector(
                    LoogaShadowShaderIds.AtlasSize,
                    new Vector4(
                        settings.AtlasResolution,
                        1f / settings.AtlasResolution,
                        settings.TileResolution,
                        1f / settings.TileResolution));
                command.SetGlobalVector(
                    LoogaShadowShaderIds.LightDirection,
                    new Vector4(data.LightDirection.x, data.LightDirection.y, data.LightDirection.z, 0f));
                command.SetGlobalVector(
                    LoogaShadowShaderIds.SampleCounts,
                    new Vector4(settings.BlockerSampleCount, settings.FilterSampleCount, 0f, 0f));
                command.SetGlobalVector(
                    LoogaShadowShaderIds.SoftShadowData,
                    new Vector4(
                        settings.SourceAngularDiameter,
                        settings.Softness,
                        settings.MaximumPenumbra,
                        settings.ClipmapBlend));
                command.SetGlobalVector(
                    LoogaShadowShaderIds.BiasData,
                    new Vector4(settings.DepthBias, settings.NormalBias, settings.DepthRange, 0f));
                command.SetGlobalVector(
                    LoogaShadowShaderIds.DistanceData,
                    new Vector4(settings.ShadowDistance, settings.ShadowDistance * 0.9f, 0f, 0f));
                command.SetGlobalInteger(
                    LoogaShadowShaderIds.NormalsSource,
                    (int)data.NormalsSource);
                command.SetGlobalInteger(
                    LoogaShadowShaderIds.NormalsOctEncoded,
                    data.NormalsOctEncoded ? 1 : 0);
            }
        }

        /// <summary>
        /// Restores URP's world-space transparent shadow path using Looga's packed clipmap atlas.
        /// Transparent shaders cannot use the opaque screen mask because it represents the surface
        /// behind them, but URP's standard forward passes can sample this atlas at their own world
        /// position without requiring material changes.
        /// </summary>
        private sealed class TransparentShadowReceiverPass : ScriptableRenderPass
        {
            private const int UrpShadowMatrixCount = MaximumClipmapCount + 1;
            private readonly ProfilingSampler _profilingSampler =
                new("Looga Shadows Bind Transparent Clipmaps");
            private readonly Matrix4x4[] _worldToShadow =
                new Matrix4x4[UrpShadowMatrixCount];
            private readonly Vector4[] _splitSpheres =
                new Vector4[MaximumClipmapCount];
            private Vector4 _splitSphereRadii;
            private LoogaShadowResolvedSettings _settings;
            private Vector4 _shadowParams;

            private sealed class PassData
            {
                public TextureHandle Atlas;
                public bool HasAtlas;
                public int ClipmapCount;
                public int AtlasResolution;
                public Matrix4x4[] WorldToShadow;
                public Vector4[] SplitSpheres;
                public Vector4 SplitSphereRadii;
                public Vector4 ShadowParams;
            }

            public void Setup(
                LoogaShadowResolvedSettings settings,
                Matrix4x4[] worldToShadow,
                Vector4[] clipmapCenters,
                Vector4[] clipmapRadii,
                Vector4 shadowParams)
            {
                _settings = settings;
                _shadowParams = shadowParams;

                Matrix4x4 noOpShadowMatrix = Matrix4x4.zero;
                noOpShadowMatrix.m22 = SystemInfo.usesReversedZBuffer
                    ? 1f
                    : 0f;

                _splitSphereRadii = Vector4.zero;
                for (int level = 0; level < MaximumClipmapCount; level++)
                {
                    bool active = level < settings.ClipmapCount;
                    _worldToShadow[level] = active
                        ? worldToShadow[level]
                        : noOpShadowMatrix;
                    _splitSpheres[level] = active
                        ? clipmapCenters[level]
                        : Vector4.zero;
                    _splitSphereRadii[level] = active
                        ? clipmapRadii[level].x * clipmapRadii[level].x
                        : 0f;
                }

                _worldToShadow[MaximumClipmapCount] = noOpShadowMatrix;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                bool hasAtlas = false;
                TextureHandle atlas = TextureHandle.nullHandle;
                if (frameData.Contains<LoogaShadowFrameData>())
                {
                    LoogaShadowFrameData shadowFrameData =
                        frameData.Get<LoogaShadowFrameData>();
                    hasAtlas =
                        shadowFrameData.HasShadowCasters &&
                        shadowFrameData.Clipmaps[0].IsValid();
                    if (hasAtlas)
                        atlas = shadowFrameData.Clipmaps[0];
                }

                using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(
                    "Looga Shadows Bind Transparent Clipmaps",
                    out PassData passData,
                    _profilingSampler);

                passData.Atlas = atlas;
                passData.HasAtlas = hasAtlas;
                passData.ClipmapCount = _settings.ClipmapCount;
                passData.AtlasResolution = _settings.AtlasResolution;
                passData.WorldToShadow = _worldToShadow;
                passData.SplitSpheres = _splitSpheres;
                passData.SplitSphereRadii = _splitSphereRadii;
                passData.ShadowParams = _shadowParams;

                builder.SetRenderAttachment(
                    resourceData.activeColorTexture,
                    0,
                    AccessFlags.ReadWrite);
                if (hasAtlas)
                    builder.UseTexture(atlas, AccessFlags.Read);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    if (!data.HasAtlas)
                    {
                        context.cmd.SetKeyword(
                            LoogaShadowShaderIds.MainLightShadowScreen,
                            false);
                        context.cmd.SetKeyword(
                            LoogaShadowShaderIds.MainLightShadows,
                            false);
                        context.cmd.SetKeyword(
                            LoogaShadowShaderIds.MainLightShadowCascades,
                            false);
                        return;
                    }

                    float inverseAtlasResolution =
                        1f / data.AtlasResolution;
                    float halfTexel = inverseAtlasResolution * 0.5f;

                    context.cmd.SetGlobalTexture(
                        "_MainLightShadowmapTexture",
                        data.Atlas);
                    context.cmd.SetGlobalMatrixArray(
                        LoogaShadowShaderIds.UrpMainLightWorldToShadow,
                        data.WorldToShadow);
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.UrpCascadeShadowSplitSpheres0,
                        data.SplitSpheres[0]);
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.UrpCascadeShadowSplitSpheres1,
                        data.SplitSpheres[1]);
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.UrpCascadeShadowSplitSpheres2,
                        data.SplitSpheres[2]);
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.UrpCascadeShadowSplitSpheres3,
                        data.SplitSpheres[3]);
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.UrpCascadeShadowSplitSphereRadii,
                        data.SplitSphereRadii);
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.UrpMainLightShadowOffset0,
                        new Vector4(
                            -halfTexel,
                            -halfTexel,
                            halfTexel,
                            -halfTexel));
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.UrpMainLightShadowOffset1,
                        new Vector4(
                            -halfTexel,
                            halfTexel,
                            halfTexel,
                            halfTexel));
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.UrpMainLightShadowmapSize,
                        new Vector4(
                            inverseAtlasResolution,
                            inverseAtlasResolution,
                            data.AtlasResolution,
                            data.AtlasResolution));
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.UrpMainLightShadowParams,
                        data.ShadowParams);

                    context.cmd.SetKeyword(LoogaShadowShaderIds.MainLightShadowScreen, false);
                    context.cmd.SetKeyword(
                        LoogaShadowShaderIds.MainLightShadows,
                        data.ClipmapCount == 1);
                    context.cmd.SetKeyword(
                        LoogaShadowShaderIds.MainLightShadowCascades,
                        data.ClipmapCount > 1);
                    // URP's main-light pass no longer runs, so it no longer enables soft filtering for
                    // a soft main light. Only enable it: soft additional lights may have set it already.
                    if (data.ShadowParams.y > 0f)
                        context.cmd.SetKeyword(LoogaShadowShaderIds.SoftShadows, true);
                });
            }
        }

        private sealed class DebugOverlayPass : ScriptableRenderPass
        {
            private readonly ProfilingSampler _profilingSampler = new("Looga Shadows Debug Overlay");
            private Material _material;
            private LoogaShadowResolvedSettings _settings;
            private Matrix4x4[] _worldToShadow;
            private Vector4[] _clipmapCenters;
            private Vector4[] _clipmapRadii;
            private Vector4[] _clipmapRects;
            private Vector3 _lightDirection;
            private LoogaShadowDebugView _debugView;
            private LoogaShadowNormalsSource _normalsSource;
            private bool _requiresCameraNormals;
            private bool _normalsOctEncoded;

            private sealed class PassData
            {
                public TextureHandle Clipmap0;
                public TextureHandle Clipmap1;
                public TextureHandle Clipmap2;
                public TextureHandle Clipmap3;
                public TextureHandle DepthClipmap0;
                public TextureHandle DepthClipmap1;
                public TextureHandle DepthClipmap2;
                public TextureHandle DepthClipmap3;
                public TextureHandle RawVisibility;
                public TextureHandle ResolvedVisibility;
                public TextureHandle CameraDepth;
                public TextureHandle CameraNormals;
                public TextureHandle DebugSource;
                public Material Material;
                public int ShaderPass;
                public LoogaShadowResolvedSettings Settings;
                public Matrix4x4[] WorldToShadow;
                public Vector4[] ClipmapCenters;
                public Vector4[] ClipmapRadii;
                public Vector4[] ClipmapRects;
                public Vector3 LightDirection;
                public LoogaShadowNormalsSource NormalsSource;
                public bool RequiresCameraNormals;
                public bool NormalsOctEncoded;
            }

            public void Setup(
                Material material,
                LoogaShadowResolvedSettings settings,
                Matrix4x4[] worldToShadow,
                Vector4[] clipmapCenters,
                Vector4[] clipmapRadii,
                Vector4[] clipmapRects,
                Vector3 lightDirection,
                bool usesDeferredLighting,
                bool usesAccurateGBufferNormals)
            {
                _material = material;
                _settings = settings;
                _worldToShadow = worldToShadow;
                _clipmapCenters = clipmapCenters;
                _clipmapRadii = clipmapRadii;
                _clipmapRects = clipmapRects;
                _lightDirection = lightDirection;
                _debugView = settings.DebugView;
                _normalsSource = GetEffectiveNormalsSource(
                    settings.NormalsSource,
                    usesDeferredLighting);
                _requiresCameraNormals =
                    _normalsSource != LoogaShadowNormalsSource.ReconstructFromDepth;
                _normalsOctEncoded =
                    _requiresCameraNormals && usesAccurateGBufferNormals;
                ConfigureInput(GetRequiredInputs(_normalsSource));
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null || _debugView == LoogaShadowDebugView.Off)
                    return;

                if (!frameData.Contains<LoogaShadowFrameData>())
                    return;

                LoogaShadowFrameData shadowFrameData = frameData.Get<LoogaShadowFrameData>();
                TextureHandle clipmap0 = shadowFrameData.Clipmaps[0];
                TextureHandle clipmap1 = shadowFrameData.Clipmaps[1];
                TextureHandle clipmap2 = shadowFrameData.Clipmaps[2];
                TextureHandle clipmap3 = shadowFrameData.Clipmaps[3];
                TextureHandle depthClipmap0 = shadowFrameData.DepthClipmaps[0];
                TextureHandle depthClipmap1 = shadowFrameData.DepthClipmaps[1];
                TextureHandle depthClipmap2 = shadowFrameData.DepthClipmaps[2];
                TextureHandle depthClipmap3 = shadowFrameData.DepthClipmaps[3];
                TextureHandle rawVisibility = shadowFrameData.RawVisibility;
                TextureHandle resolvedVisibility = shadowFrameData.ResolvedVisibility;
                if (!clipmap0.IsValid() || !clipmap1.IsValid() ||
                    !clipmap2.IsValid() || !clipmap3.IsValid() ||
                    !depthClipmap0.IsValid() || !depthClipmap1.IsValid() ||
                    !depthClipmap2.IsValid() || !depthClipmap3.IsValid() ||
                    !rawVisibility.IsValid() || !resolvedVisibility.IsValid())
                    return;

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                TextureHandle cameraDepth = resourceData.cameraDepthTexture.IsValid()
                    ? resourceData.cameraDepthTexture
                    : resourceData.activeDepthTexture;
                TextureHandle cameraNormals = GetNormalsTexture(
                    resourceData,
                    _normalsSource);
                if (!cameraDepth.IsValid() ||
                    (_requiresCameraNormals && !cameraNormals.IsValid()))
                    return;

                using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(
                    "Looga Shadows Debug Overlay",
                    out PassData passData,
                    _profilingSampler);

                passData.Clipmap0 = clipmap0;
                passData.Clipmap1 = clipmap1;
                passData.Clipmap2 = clipmap2;
                passData.Clipmap3 = clipmap3;
                passData.DepthClipmap0 = depthClipmap0;
                passData.DepthClipmap1 = depthClipmap1;
                passData.DepthClipmap2 = depthClipmap2;
                passData.DepthClipmap3 = depthClipmap3;
                passData.RawVisibility = rawVisibility;
                passData.ResolvedVisibility = resolvedVisibility;
                passData.CameraDepth = cameraDepth;
                passData.CameraNormals = cameraNormals;
                passData.DebugSource = _debugView switch
                {
                    LoogaShadowDebugView.RawVisibility => rawVisibility,
                    LoogaShadowDebugView.FinalVisibility or LoogaShadowDebugView.Penumbra
                        => resolvedVisibility,
                    _ => cameraDepth
                };
                passData.Material = _material;
                passData.ShaderPass = GetShaderPass(_debugView);
                passData.Settings = _settings;
                passData.WorldToShadow = _worldToShadow;
                passData.ClipmapCenters = _clipmapCenters;
                passData.ClipmapRadii = _clipmapRadii;
                passData.ClipmapRects = _clipmapRects;
                passData.LightDirection = _lightDirection;
                passData.NormalsSource = _normalsSource;
                passData.RequiresCameraNormals = _requiresCameraNormals;
                passData.NormalsOctEncoded = _normalsOctEncoded;
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                builder.UseTexture(clipmap0, AccessFlags.Read);
                builder.UseTexture(clipmap1, AccessFlags.Read);
                builder.UseTexture(clipmap2, AccessFlags.Read);
                builder.UseTexture(clipmap3, AccessFlags.Read);
                builder.UseTexture(depthClipmap0, AccessFlags.Read);
                builder.UseTexture(depthClipmap1, AccessFlags.Read);
                builder.UseTexture(depthClipmap2, AccessFlags.Read);
                builder.UseTexture(depthClipmap3, AccessFlags.Read);
                builder.UseTexture(rawVisibility, AccessFlags.Read);
                builder.UseTexture(resolvedVisibility, AccessFlags.Read);
                builder.UseTexture(cameraDepth, AccessFlags.Read);
                if (_requiresCameraNormals)
                    builder.UseTexture(cameraNormals, AccessFlags.Read);
                builder.UseAllGlobalTextures(true);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    RTHandle debugSource = data.DebugSource;
                    LoogaShadowResolvedSettings settings = data.Settings;
                    context.cmd.SetGlobalTexture(
                        "_LoogaVirtualShadowClipmap0",
                        data.Clipmap0);
                    context.cmd.SetGlobalTexture(
                        "_LoogaVirtualShadowClipmap1",
                        data.Clipmap1);
                    context.cmd.SetGlobalTexture(
                        "_LoogaVirtualShadowClipmap2",
                        data.Clipmap2);
                    context.cmd.SetGlobalTexture(
                        "_LoogaVirtualShadowClipmap3",
                        data.Clipmap3);
                    context.cmd.SetGlobalTexture(
                        "_LoogaVirtualShadowDepthClipmap0",
                        data.DepthClipmap0);
                    context.cmd.SetGlobalTexture(
                        "_LoogaVirtualShadowDepthClipmap1",
                        data.DepthClipmap1);
                    context.cmd.SetGlobalTexture(
                        "_LoogaVirtualShadowDepthClipmap2",
                        data.DepthClipmap2);
                    context.cmd.SetGlobalTexture(
                        "_LoogaVirtualShadowDepthClipmap3",
                        data.DepthClipmap3);
                    context.cmd.SetGlobalTexture(
                        "_LoogaVirtualShadowAtlas",
                        data.Clipmap0);
                    context.cmd.SetGlobalTexture(
                        "_LoogaVirtualShadowDepthAtlas",
                        data.DepthClipmap0);
                    context.cmd.SetGlobalMatrixArray(
                        LoogaShadowShaderIds.WorldToShadow,
                        data.WorldToShadow);
                    context.cmd.SetGlobalVectorArray(
                        LoogaShadowShaderIds.ClipmapCenters,
                        data.ClipmapCenters);
                    context.cmd.SetGlobalVectorArray(
                        LoogaShadowShaderIds.ClipmapRadii,
                        data.ClipmapRadii);
                    context.cmd.SetGlobalVectorArray(
                        LoogaShadowShaderIds.ClipmapRects,
                        data.ClipmapRects);
                    context.cmd.SetGlobalInteger(
                        LoogaShadowShaderIds.ClipmapCount,
                        settings.ClipmapCount);
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.AtlasSize,
                        new Vector4(
                            settings.AtlasResolution,
                            1f / settings.AtlasResolution,
                            settings.TileResolution,
                            1f / settings.TileResolution));
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.LightDirection,
                        new Vector4(
                            data.LightDirection.x,
                            data.LightDirection.y,
                            data.LightDirection.z,
                            0f));
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.SampleCounts,
                        new Vector4(
                            settings.BlockerSampleCount,
                            settings.FilterSampleCount,
                            0f,
                            0f));
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.SoftShadowData,
                        new Vector4(
                            settings.SourceAngularDiameter,
                            settings.Softness,
                            settings.MaximumPenumbra,
                            settings.ClipmapBlend));
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.BiasData,
                        new Vector4(
                            settings.DepthBias,
                            settings.NormalBias,
                            settings.DepthRange,
                            0f));
                    context.cmd.SetGlobalVector(
                        LoogaShadowShaderIds.DistanceData,
                        new Vector4(
                            settings.ShadowDistance,
                            settings.ShadowDistance * 0.9f,
                            0f,
                            0f));
                    context.cmd.SetGlobalInteger(
                        LoogaShadowShaderIds.NormalsSource,
                        (int)data.NormalsSource);
                    context.cmd.SetGlobalInteger(
                        LoogaShadowShaderIds.NormalsOctEncoded,
                        data.NormalsOctEncoded ? 1 : 0);
                    if (data.RequiresCameraNormals)
                    {
                        context.cmd.SetGlobalTexture(
                            LoogaShadowShaderIds.CameraNormalsTexture,
                            data.CameraNormals);
                    }
                    Blitter.BlitTexture(
                        context.cmd,
                        debugSource,
                        Vector2.one,
                        data.Material,
                        data.ShaderPass);
                });
            }

            private static int GetShaderPass(LoogaShadowDebugView debugView)
            {
                return debugView switch
                {
                    LoogaShadowDebugView.FinalVisibility => 1,
                    LoogaShadowDebugView.RawVisibility => 2,
                    LoogaShadowDebugView.Penumbra => 3,
                    LoogaShadowDebugView.ClipmapLevels => 4,
                    LoogaShadowDebugView.VirtualTexels => 5,
                    LoogaShadowDebugView.LinearDepth => 6,
                    LoogaShadowDebugView.WorldNormals => 7,
                    _ => 1
                };
            }
        }
    }
}
