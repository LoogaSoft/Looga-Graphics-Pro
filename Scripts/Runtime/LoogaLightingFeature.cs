using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Scripting.APIUpdating;
using System.Reflection;
using LoogaSoft.Tonemapper.Runtime;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LoogaSoft.Lighting
{
    [DisallowMultipleRendererFeature("Looga Lighting")]
    [MovedFrom(true, "LoogaSoft.Lighting", "LoogaSoft.LightingPrime", "LoogaLightingFeature")]
    public class LoogaLightingFeature : ScriptableRendererFeature
    {
        private const string FeatureDisplayName = "Looga Lighting";
        private const string MasterDeferredShaderPath = "Hidden/LoogaSoft/Lighting/MasterDeferred";
        private const string ProjectMasterDeferredShaderPrefix =
            "Hidden/LoogaSoft/Lighting/MasterDeferredProject/";
        private const string ProjectMasterDeferredResourcePrefix =
            "Looga Master Deferred ";
        private const string LegacyProjectMasterDeferredResourcePrefix =
            "LoogaSoft/Lighting/Looga Master Deferred ";

        public enum LightingModel
        {
            [InspectorName("Disney/Burley")]
            DisneyBurley = 0,
            [InspectorName("Source 2")]
            Source2 = 1,
            Minnaert = 3,
            [InspectorName("Overwatch")]
            Overwatch = 4,
            OrenNayar = 5,
            [InspectorName("Arkane")]
            Arkane = 6,
            Custom = 100
        }

        public LightingModel activeLightingModel = LightingModel.DisneyBurley;
        public LoogaLightingModelProfile customLightingModelProfile;

        [InspectorName("Enable Tonemapper")]
        public bool enableTonemapper = true;

        [InspectorName("Enable Advanced Material Data")]
        public bool enableAdvancedMaterialData = true;
        [InspectorName("Enable Subsurface Scattering")]
        public bool enableSubsurfaceScattering = true;
        [InspectorName("Enable Backlighting")]
        public bool enableBacklighting = true;
        [InspectorName("Backlighting Intensity"), Range(0.0f, 2.0f)]
        public float backlightingIntensity = 1.0f;
        [HideInInspector] public Shader tonemapperShader;
        [HideInInspector] public Shader masterDeferredShader;

        private Material _activeLightingMaterial;
        private int _activeLightingMaterialModel = int.MinValue;
        private Material _ssssMaterial;
        private Material _tonemapperMaterial;

        private CustomLightingPass _customLightingPass;
        private LoogaTonemapperPass _tonemapperPass;

        private static readonly int GlobalLightingModelID = Shader.PropertyToID("_LoogaLightingModel");
        private static readonly int GBufferNormalsAreOctID = Shader.PropertyToID("_LoogaGBufferNormalsAreOct");
        private static readonly int AdvancedMaterialDataEnabledID = Shader.PropertyToID("_LoogaAdvancedMaterialDataEnabled");
        private static readonly int SubsurfaceScatteringEnabledID = Shader.PropertyToID("_LoogaSubsurfaceScatteringEnabled");
        private static readonly int BacklightingEnabledID = Shader.PropertyToID("_LoogaBacklightingEnabled");
        private static readonly int BacklightingIntensityID = Shader.PropertyToID("_LoogaBacklightingIntensity");
        private static readonly int ProfileDiffuseModelID = Shader.PropertyToID("_LoogaProfileDiffuseModel");
        private static readonly int ProfileDirectSpecularModelID = Shader.PropertyToID("_LoogaProfileDirectSpecularModel");
        private static readonly int ProfileIndirectSpecularModelID = Shader.PropertyToID("_LoogaProfileIndirectSpecularModel");
        private static readonly int ProfileSpecularOcclusionModelID = Shader.PropertyToID("_LoogaProfileSpecularOcclusionModel");
        private static readonly int ProfileDiffuseStrengthID = Shader.PropertyToID("_LoogaProfileDiffuseStrength");
        private static readonly int ProfileDirectSpecularStrengthID = Shader.PropertyToID("_LoogaProfileDirectSpecularStrength");
        private static readonly int ProfileIndirectSpecularStrengthID = Shader.PropertyToID("_LoogaProfileIndirectSpecularStrength");
        private static readonly int ProfileDirectRoughnessScaleID = Shader.PropertyToID("_LoogaProfileDirectRoughnessScale");
        private static readonly int ProfileDirectRoughnessBiasID = Shader.PropertyToID("_LoogaProfileDirectRoughnessBias");
        private static readonly int ProfileIndirectRoughnessScaleID = Shader.PropertyToID("_LoogaProfileIndirectRoughnessScale");
        private static readonly int ProfileIndirectRoughnessBiasID = Shader.PropertyToID("_LoogaProfileIndirectRoughnessBias");
        private static readonly int ProfileIndirectFresnelPowerID = Shader.PropertyToID("_LoogaProfileIndirectFresnelPower");
        private static readonly int ProfileMinnaertKID = Shader.PropertyToID("_LoogaProfileMinnaertK");
        private static readonly int ProfileOrenNayarSigmaID = Shader.PropertyToID("_LoogaProfileOrenNayarSigma");
        private static readonly int ProfileDiffuseWrapID = Shader.PropertyToID("_LoogaProfileDiffuseWrap");
        private static readonly int ProfileBandCountID = Shader.PropertyToID("_LoogaProfileBandCount");
        private static readonly int ProfileBandFeatherID = Shader.PropertyToID("_LoogaProfileBandFeather");
        private static readonly int ProfileBandBlendID = Shader.PropertyToID("_LoogaProfileBandBlend");
        private static readonly int ProfileSecondarySpecularWeightID = Shader.PropertyToID("_LoogaProfileSecondarySpecularWeight");
        private static readonly int ProfileSecondaryRoughnessSpreadID = Shader.PropertyToID("_LoogaProfileSecondaryRoughnessSpread");
        private static readonly int ProfileHighlightShapeStrengthID = Shader.PropertyToID("_LoogaProfileHighlightShapeStrength");
        private static readonly int ProfileHighlightShapeFloorID = Shader.PropertyToID("_LoogaProfileHighlightShapeFloor");
        private static readonly int ProfileHighlightShapeStartID = Shader.PropertyToID("_LoogaProfileHighlightShapeStart");
        private static readonly int ProfileHighlightShapeEndID = Shader.PropertyToID("_LoogaProfileHighlightShapeEnd");
        private static readonly int ProfileGrazingOcclusionStrengthID = Shader.PropertyToID("_LoogaProfileGrazingOcclusionStrength");
        private static readonly int ProfileEdgeOcclusionStrengthID = Shader.PropertyToID("_LoogaProfileEdgeOcclusionStrength");
        private static readonly int ProfileEdgeOcclusionStartID = Shader.PropertyToID("_LoogaProfileEdgeOcclusionStart");
        private static readonly int ProfileEdgeOcclusionEndID = Shader.PropertyToID("_LoogaProfileEdgeOcclusionEnd");
        private static readonly LoogaLightingModelSettings DefaultProfileSettings =
            new LoogaLightingModelSettings();
        private const string TonemapperShaderPath = "Hidden/LoogaSoft/Tonemapper";

        #if UNITY_EDITOR
        private void OnValidate()
        {
            bool needsSave = false;

            if ((int)activeLightingModel == 2)
            {
                activeLightingModel = LightingModel.DisneyBurley;
                needsSave = true;
            }

            if (name != FeatureDisplayName)
            {
                name = FeatureDisplayName;
                needsSave = true;
            }

            if (tonemapperShader == null) AssignShader(ref tonemapperShader, "Looga Tonemapper", ref needsSave);
            Shader specializedShader = FindProjectMasterDeferredShader(activeLightingModel);
            if (specializedShader != null && masterDeferredShader != specializedShader)
            {
                masterDeferredShader = specializedShader;
                needsSave = true;
            }
            else if (specializedShader == null &&
                (masterDeferredShader == null ||
                 masterDeferredShader.name != MasterDeferredShaderPath))
            {
                masterDeferredShader = Shader.Find(MasterDeferredShaderPath);
                needsSave |= masterDeferredShader != null;
            }

            if (needsSave) EditorUtility.SetDirty(this);
        }

        private void AssignShader(ref Shader shader, string shaderName, ref bool needsSave)
        {
            string[] guids = AssetDatabase.FindAssets($"{shaderName} t:Shader");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                needsSave = true;
            }
        }
        #endif

        public override void Create()
        {
            name = FeatureDisplayName;
            LoogaIndirectLightingController.EnsureGlobalsAreValid();
            UpdateLightingState();
        }

        private void UpdateLightingState()
        {
            // 1. Core Lighting Initialization
            int lightingModel = (int)activeLightingModel;
            Shader.SetGlobalInteger(GlobalLightingModelID, lightingModel == 2 ? 0 : lightingModel);
            Shader.SetGlobalInteger(AdvancedMaterialDataEnabledID, enableAdvancedMaterialData ? 1 : 0);
            Shader.SetGlobalInteger(SubsurfaceScatteringEnabledID, enableSubsurfaceScattering ? 1 : 0);
            Shader.SetGlobalInteger(BacklightingEnabledID, enableBacklighting ? 1 : 0);
            Shader.SetGlobalFloat(BacklightingIntensityID, backlightingIntensity);
            if (activeLightingModel == LightingModel.Custom)
                UploadLightingModelProfile();

            Shader lightingShader = FindProjectMasterDeferredShader(activeLightingModel);
            string expectedGeneratedName =
                ProjectMasterDeferredShaderPrefix +
                GetLightingModelToken(activeLightingModel);
            if (lightingShader == null && masterDeferredShader != null &&
                (masterDeferredShader.name == MasterDeferredShaderPath ||
                 masterDeferredShader.name == expectedGeneratedName))
            {
                lightingShader = masterDeferredShader;
            }
            if (lightingShader == null)
                lightingShader = Shader.Find(MasterDeferredShaderPath);

            if (lightingShader != null &&
                (_activeLightingMaterial == null || _activeLightingMaterial.shader != lightingShader))
            {
                if (_activeLightingMaterial != null) CoreUtils.Destroy(_activeLightingMaterial);
                _activeLightingMaterial = CoreUtils.CreateEngineMaterial(lightingShader);
                _activeLightingMaterialModel = int.MinValue;
            }

            if (lightingShader != null && lightingShader.name == MasterDeferredShaderPath)
                UpdateLightingModelKeyword(lightingModel);

            if (_ssssMaterial == null || _ssssMaterial.shader.name != "Hidden/LoogaSoft/SSSS")
            {
                Shader ssssShader = Shader.Find("Hidden/LoogaSoft/SSSS");
                if (ssssShader != null) _ssssMaterial = CoreUtils.CreateEngineMaterial(ssssShader);
            }

            if (_activeLightingMaterial != null)
            {
                if (_customLightingPass == null) _customLightingPass = new CustomLightingPass(this);
                else _customLightingPass.UpdateMaterials(this);
            }

            if (enableTonemapper)
                UpdateTonemapperState();
        }

        private void UpdateLightingModelKeyword(int lightingModel)
        {
            if (_activeLightingMaterial == null || _activeLightingMaterialModel == lightingModel)
                return;

            _activeLightingMaterial.DisableKeyword("_LOOGA_MODEL_SOURCE2");
            _activeLightingMaterial.DisableKeyword("_LOOGA_MODEL_MINNAERT");
            _activeLightingMaterial.DisableKeyword("_LOOGA_MODEL_OVERWATCH");
            _activeLightingMaterial.DisableKeyword("_LOOGA_MODEL_OREN_NAYAR");
            _activeLightingMaterial.DisableKeyword("_LOOGA_MODEL_ARKANE");
            _activeLightingMaterial.DisableKeyword("_LOOGA_MODEL_CUSTOM");

            switch (lightingModel)
            {
                case (int)LightingModel.Source2:
                    _activeLightingMaterial.EnableKeyword("_LOOGA_MODEL_SOURCE2");
                    break;
                case (int)LightingModel.Minnaert:
                    _activeLightingMaterial.EnableKeyword("_LOOGA_MODEL_MINNAERT");
                    break;
                case (int)LightingModel.Overwatch:
                    _activeLightingMaterial.EnableKeyword("_LOOGA_MODEL_OVERWATCH");
                    break;
                case (int)LightingModel.OrenNayar:
                    _activeLightingMaterial.EnableKeyword("_LOOGA_MODEL_OREN_NAYAR");
                    break;
                case (int)LightingModel.Arkane:
                    _activeLightingMaterial.EnableKeyword("_LOOGA_MODEL_ARKANE");
                    break;
                case (int)LightingModel.Custom:
                    _activeLightingMaterial.EnableKeyword("_LOOGA_MODEL_CUSTOM");
                    break;
            }

            _activeLightingMaterialModel = lightingModel;
        }

        private static Shader FindProjectMasterDeferredShader(LightingModel model)
        {
            string token = GetLightingModelToken(model);
            Shader shader = Shader.Find(ProjectMasterDeferredShaderPrefix + token);
            if (shader == null)
                shader = Resources.Load<Shader>(ProjectMasterDeferredResourcePrefix + token);
            if (shader == null)
                shader = Resources.Load<Shader>(LegacyProjectMasterDeferredResourcePrefix + token);
            return shader;
        }

        private static string GetLightingModelToken(LightingModel model)
        {
            return model switch
            {
                LightingModel.Source2 => "Source2",
                LightingModel.Minnaert => "Minnaert",
                LightingModel.Overwatch => "Overwatch",
                LightingModel.OrenNayar => "OrenNayar",
                LightingModel.Arkane => "Arkane",
                LightingModel.Custom => "Custom",
                _ => "DisneyBurley"
            };
        }

        private void UploadLightingModelProfile()
        {
            LoogaLightingModelSettings settings =
                activeLightingModel == LightingModel.Custom && customLightingModelProfile != null
                    ? customLightingModelProfile.settings
                    : DefaultProfileSettings;

            if (settings == null)
                settings = new LoogaLightingModelSettings();

            Shader.SetGlobalInteger(ProfileDiffuseModelID, (int)settings.diffuseModel);
            Shader.SetGlobalInteger(ProfileDirectSpecularModelID, (int)settings.directSpecularModel);
            Shader.SetGlobalInteger(ProfileIndirectSpecularModelID, (int)settings.indirectSpecularModel);
            Shader.SetGlobalInteger(ProfileSpecularOcclusionModelID, (int)settings.specularOcclusionModel);
            Shader.SetGlobalFloat(ProfileDiffuseStrengthID, settings.diffuseStrength);
            Shader.SetGlobalFloat(ProfileDirectSpecularStrengthID, settings.directSpecularStrength);
            Shader.SetGlobalFloat(ProfileIndirectSpecularStrengthID, settings.indirectSpecularStrength);
            Shader.SetGlobalFloat(ProfileDirectRoughnessScaleID, settings.directRoughnessScale);
            Shader.SetGlobalFloat(ProfileDirectRoughnessBiasID, settings.directRoughnessBias);
            Shader.SetGlobalFloat(ProfileIndirectRoughnessScaleID, settings.indirectRoughnessScale);
            Shader.SetGlobalFloat(ProfileIndirectRoughnessBiasID, settings.indirectRoughnessBias);
            Shader.SetGlobalFloat(ProfileIndirectFresnelPowerID, settings.indirectFresnelPower);
            Shader.SetGlobalFloat(ProfileMinnaertKID, settings.minnaertK);
            Shader.SetGlobalFloat(ProfileOrenNayarSigmaID, settings.orenNayarSigma);
            Shader.SetGlobalFloat(ProfileDiffuseWrapID, settings.diffuseWrap);
            Shader.SetGlobalFloat(ProfileBandCountID, settings.bandCount);
            Shader.SetGlobalFloat(ProfileBandFeatherID, settings.bandFeather);
            Shader.SetGlobalFloat(ProfileBandBlendID, settings.bandBlend);
            Shader.SetGlobalFloat(ProfileSecondarySpecularWeightID, settings.secondarySpecularWeight);
            Shader.SetGlobalFloat(ProfileSecondaryRoughnessSpreadID, settings.secondaryRoughnessSpread);
            Shader.SetGlobalFloat(ProfileHighlightShapeStrengthID, settings.highlightShapeStrength);
            Shader.SetGlobalFloat(ProfileHighlightShapeFloorID, settings.highlightShapeFloor);
            Shader.SetGlobalFloat(ProfileHighlightShapeStartID, settings.highlightShapeStart);
            Shader.SetGlobalFloat(ProfileHighlightShapeEndID, settings.highlightShapeEnd);
            Shader.SetGlobalFloat(ProfileGrazingOcclusionStrengthID, settings.grazingOcclusionStrength);
            Shader.SetGlobalFloat(ProfileEdgeOcclusionStrengthID, settings.edgeOcclusionStrength);
            Shader.SetGlobalFloat(ProfileEdgeOcclusionStartID, settings.edgeOcclusionStart);
            Shader.SetGlobalFloat(ProfileEdgeOcclusionEndID, settings.edgeOcclusionEnd);
        }

        private void UpdateTonemapperState()
        {
            if (tonemapperShader == null)
                tonemapperShader = Shader.Find(TonemapperShaderPath);

            if (tonemapperShader == null)
                return;

            if (_tonemapperMaterial == null || _tonemapperMaterial.shader != tonemapperShader)
            {
                if (_tonemapperMaterial != null) CoreUtils.Destroy(_tonemapperMaterial);
                _tonemapperMaterial = CoreUtils.CreateEngineMaterial(tonemapperShader);
            }

            if (_tonemapperPass == null)
            {
                _tonemapperPass = new LoogaTonemapperPass(_tonemapperMaterial)
                {
                    renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
                };
            }
            else
            {
                _tonemapperPass.UpdateMaterial(_tonemapperMaterial);
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            LoogaIndirectLightingController.EnsureGlobalsAreValid();

            if (!isActive || (renderingData.cameraData.cameraType != CameraType.Game && renderingData.cameraData.cameraType != CameraType.SceneView))
                return;

            UpdateLightingState();

            if (!IsDeferredPlusRenderer(renderer))
            {
                EnqueueTonemapper(renderer);
                return;
            }

            if (_activeLightingMaterial != null && _customLightingPass != null)
            {
                _customLightingPass.SetRenderer(renderer);
                renderer.EnqueuePass(_customLightingPass);
            }

            EnqueueTonemapper(renderer);
        }

        private void EnqueueTonemapper(ScriptableRenderer renderer)
        {
            if (!enableTonemapper)
                return;

            UpdateTonemapperState();

            if (_tonemapperPass != null && _tonemapperMaterial != null)
                renderer.EnqueuePass(_tonemapperPass);
        }

        protected override void Dispose(bool disposing)
        {
            Shader.SetGlobalInteger(AdvancedMaterialDataEnabledID, 0);
            Shader.SetGlobalInteger(SubsurfaceScatteringEnabledID, 0);
            Shader.SetGlobalInteger(BacklightingEnabledID, 0);
            if (_activeLightingMaterial != null) CoreUtils.Destroy(_activeLightingMaterial);
            if (_ssssMaterial != null) CoreUtils.Destroy(_ssssMaterial);
            if (_tonemapperMaterial != null) CoreUtils.Destroy(_tonemapperMaterial);

            _customLightingPass = null;
            _tonemapperPass = null;
            base.Dispose(disposing);
        }

        private static bool UsesAccurateGBufferNormals(ScriptableRenderer renderer)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = renderer?.GetType().GetProperty("accurateGbufferNormals", flags);

            if (property != null && property.PropertyType == typeof(bool))
                return (bool)property.GetValue(renderer);

            return false;
        }

        private static bool IsDeferredPlusRenderer(ScriptableRenderer renderer)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = renderer?.GetType().GetProperty("renderingModeActual", flags);

            if (property == null)
                return false;

            object value = property.GetValue(renderer);
            return value != null && value.ToString() == "DeferredPlus";
        }

        private static int GetDeferredGBufferIndex(ScriptableRenderer renderer, string propertyName)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo deferredLightsProperty = renderer?.GetType().GetProperty("deferredLights", flags);
            object deferredLights = deferredLightsProperty?.GetValue(renderer);
            PropertyInfo indexProperty = deferredLights?.GetType().GetProperty(propertyName, flags);

            return indexProperty?.PropertyType == typeof(int)
                ? (int)indexProperty.GetValue(deferredLights)
                : -1;
        }

        // =======================================================================
        // DEFERRED LIGHTING PASS
        // =======================================================================
        private class CustomLightingPass : ScriptableRenderPass
        {
            private LoogaLightingFeature _feature;
            private ScriptableRenderer _renderer;

            private static readonly int[] ShaderGBufferIDs = {
                Shader.PropertyToID("_GBuffer0"), Shader.PropertyToID("_GBuffer1"),
                Shader.PropertyToID("_GBuffer2")
            };

            private static readonly int CameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture");
            private static readonly int MainLightPositionID = Shader.PropertyToID("_MainLightPosition");
            private static readonly int MainLightColorID = Shader.PropertyToID("_MainLightColor");
            private static readonly int SSSSProfileTextureID = Shader.PropertyToID("_SSSSProfileTexture");
            private static readonly int SSSSProfileExtraTextureID = Shader.PropertyToID("_SSSSProfileExtraTexture");
            private static readonly int LoogaMaterialExtrasTextureID = Shader.PropertyToID("_LoogaMaterialExtrasTexture");
            private static readonly int LoogaModelParametersTextureID = Shader.PropertyToID("_LoogaModelParametersTexture");
            private static readonly int LoogaRenderingLayersTextureID = Shader.PropertyToID("_LoogaRenderingLayersTexture");
            private static readonly int LoogaHasRenderingLayersTextureID = Shader.PropertyToID("_LoogaHasRenderingLayersTexture");
            private static readonly int LoogaShadowMaskTextureID = Shader.PropertyToID("_LoogaShadowMaskTexture");
            private static readonly int LoogaHasShadowMaskTextureID = Shader.PropertyToID("_LoogaHasShadowMaskTexture");
            private static readonly int LoogaSourceColorTextureID = Shader.PropertyToID("_LoogaSourceColorTexture");
            private static readonly int LoogaHasSSSSProfileTextureID = Shader.PropertyToID("_LoogaHasSSSSProfileTexture");
            private static readonly ShaderTagId SSSSProfileTagId = new ShaderTagId("SSSSProfile");
            private static readonly ShaderTagId LoogaMaterialExtrasTagId = new ShaderTagId("LoogaMaterialExtras");

            public CustomLightingPass(LoogaLightingFeature feature)
            {
                _feature = feature;
                renderPassEvent = RenderPassEvent.BeforeRenderingDeferredLights;
            }

            public void UpdateMaterials(LoogaLightingFeature feature) => _feature = feature;

            public void SetRenderer(ScriptableRenderer renderer) => _renderer = renderer;

            private class LightingPassData
            {
                public Material material;
                public TextureHandle[] gBuffers;
                public TextureHandle sourceColorTexture, depthTexture, ssssProfileTexture, ssssProfileExtraTexture, materialExtrasTexture, modelParametersTexture, renderingLayersTexture, shadowMaskTexture;
                public Vector4 mainLightPosition;
                public Vector4 mainLightColor;
                public bool hasSSSSProfileTexture;
                public bool useAccurateGBufferNormals;
            }

            private class SSSSPassData { public TextureHandle source; public Material material; public int passIndex; }
            private class DrawProfileData { public RendererListHandle rendererList; }
            private class BlitPassData
            {
                public TextureHandle source;
            }

            private class StencilClearPassData
            {
                public Material material;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

                if (_feature._activeLightingMaterial == null) return;

                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                GetMainLightConstants(lightData, out Vector4 mainLightPosition, out Vector4 mainLightColor);

                TextureHandle activeColor = resourceData.activeColorTexture;
                TextureHandle hardwareDepth = resourceData.activeDepthTexture;
                TextureHandle stencilTexture = resourceData.activeDepthTexture;

                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;

                TextureHandle tempLightingTarget = renderGraph.CreateTexture(new TextureDesc(desc)
                {
                    name = "Looga Lighting Target", enableRandomWrite = true, clearBuffer = true, clearColor = Color.clear
                });

                TextureHandle ssssProfileTarget = TextureHandle.nullHandle;
                TextureHandle materialExtrasTarget = TextureHandle.nullHandle;
                TextureHandle modelParametersTarget = TextureHandle.nullHandle;

                if (_feature.enableAdvancedMaterialData && hardwareDepth.IsValid())
                {
                    materialExtrasTarget = renderGraph.CreateTexture(new TextureDesc(desc)
                    {
                        name = "Looga Material Extras Target", colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, clearBuffer = true, clearColor = Color.clear
                    });

                    modelParametersTarget = renderGraph.CreateTexture(new TextureDesc(desc)
                    {
                        name = "Looga Model Parameters Target", colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, clearBuffer = true, clearColor = Color.clear
                    });

                    using (var builder = renderGraph.AddRasterRenderPass<DrawProfileData>("Looga Material Extras Draw", out var passData))
                    {
                        builder.SetRenderAttachment(materialExtrasTarget, 0, AccessFlags.Write);
                        builder.SetRenderAttachment(modelParametersTarget, 1, AccessFlags.Write);
                        builder.SetRenderAttachmentDepth(hardwareDepth, AccessFlags.Read);

                        UniversalRenderingData urpRenderingData = frameData.Get<UniversalRenderingData>();
                        DrawingSettings drawingSettings = new DrawingSettings(LoogaMaterialExtrasTagId, new SortingSettings(cameraData.camera));
                        FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.opaque);

                        passData.rendererList = renderGraph.CreateRendererList(new RendererListParams(urpRenderingData.cullResults, drawingSettings, filteringSettings));
                        builder.UseRendererList(passData.rendererList);

                        builder.SetRenderFunc((DrawProfileData data, RasterGraphContext context) => context.cmd.DrawRendererList(data.rendererList));
                    }
                }

                TextureHandle ssssProfileExtraTarget = TextureHandle.nullHandle;

                bool needsScatteringProfiles = _feature.enableSubsurfaceScattering || _feature.enableBacklighting;
                if (needsScatteringProfiles && _feature._ssssMaterial != null && hardwareDepth.IsValid())
                {
                    ssssProfileTarget = renderGraph.CreateTexture(new TextureDesc(desc)
                    {
                        name = "SSSS Profile Target", colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, clearBuffer = true, clearColor = Color.clear
                    });

                    ssssProfileExtraTarget = renderGraph.CreateTexture(new TextureDesc(desc)
                    {
                        name = "SSSS Profile Extra Target", colorFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, clearBuffer = true, clearColor = Color.clear
                    });

                    using (var builder = renderGraph.AddRasterRenderPass<DrawProfileData>("Looga SSSS Profile Draw", out var passData))
                    {
                        builder.SetRenderAttachment(ssssProfileTarget, 0, AccessFlags.Write);
                        builder.SetRenderAttachment(ssssProfileExtraTarget, 1, AccessFlags.Write);
                        builder.SetRenderAttachmentDepth(hardwareDepth, AccessFlags.Read);

                        UniversalRenderingData urpRenderingData = frameData.Get<UniversalRenderingData>();
                        DrawingSettings drawingSettings = new DrawingSettings(SSSSProfileTagId, new SortingSettings(cameraData.camera));
                        FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.opaque);

                        passData.rendererList = renderGraph.CreateRendererList(new RendererListParams(urpRenderingData.cullResults, drawingSettings, filteringSettings));
                        builder.UseRendererList(passData.rendererList);

                        builder.SetRenderFunc((DrawProfileData data, RasterGraphContext context) => context.cmd.DrawRendererList(data.rendererList));
                    }
                }

                using (var builder = renderGraph.AddRasterRenderPass<LightingPassData>("Looga Lighting Evaluation", out var passData))
                {
                    passData.material = _feature._activeLightingMaterial;
                    passData.sourceColorTexture = activeColor;
                    passData.depthTexture = hardwareDepth;
                    passData.ssssProfileTexture = ssssProfileTarget;
                    passData.ssssProfileExtraTexture = ssssProfileExtraTarget;
                    passData.materialExtrasTexture = materialExtrasTarget;
                    passData.modelParametersTexture = modelParametersTarget;
                    passData.mainLightPosition = mainLightPosition;
                    passData.mainLightColor = mainLightColor;
                    passData.hasSSSSProfileTexture = ssssProfileTarget.IsValid();
                    passData.useAccurateGBufferNormals = UsesAccurateGBufferNormals(_renderer);

                    TextureHandle[] currentGBuffers = resourceData.gBuffer;
                    if (currentGBuffers != null)
                    {
                        int renderingLayersIndex = GetDeferredGBufferIndex(_renderer, "GBufferRenderingLayers");
                        int shadowMaskIndex = GetDeferredGBufferIndex(_renderer, "GBufferShadowMask");
                        passData.gBuffers = new TextureHandle[Mathf.Min(3, currentGBuffers.Length)];

                        for (int i = 0; i < passData.gBuffers.Length; i++)
                            passData.gBuffers[i] = currentGBuffers[i];

                        if (renderingLayersIndex >= 0 && renderingLayersIndex < currentGBuffers.Length)
                            passData.renderingLayersTexture = currentGBuffers[renderingLayersIndex];
                        if (shadowMaskIndex >= 0 && shadowMaskIndex < currentGBuffers.Length)
                            passData.shadowMaskTexture = currentGBuffers[shadowMaskIndex];

                        for (int i = 0; i < passData.gBuffers.Length; i++)
                        {
                            if (passData.gBuffers[i].IsValid())
                                builder.UseTexture(passData.gBuffers[i], AccessFlags.Read);
                        }
                    }

                    if (passData.depthTexture.IsValid()) builder.UseTexture(passData.depthTexture, AccessFlags.Read);
                    if (passData.sourceColorTexture.IsValid()) builder.UseTexture(passData.sourceColorTexture, AccessFlags.Read);
                    if (passData.ssssProfileTexture.IsValid()) builder.UseTexture(passData.ssssProfileTexture, AccessFlags.Read);
                    if (passData.ssssProfileExtraTexture.IsValid()) builder.UseTexture(passData.ssssProfileExtraTexture, AccessFlags.Read);
                    if (passData.materialExtrasTexture.IsValid()) builder.UseTexture(passData.materialExtrasTexture, AccessFlags.Read);
                    if (passData.modelParametersTexture.IsValid()) builder.UseTexture(passData.modelParametersTexture, AccessFlags.Read);
                    if (passData.renderingLayersTexture.IsValid()) builder.UseTexture(passData.renderingLayersTexture, AccessFlags.Read);
                    if (passData.shadowMaskTexture.IsValid()) builder.UseTexture(passData.shadowMaskTexture, AccessFlags.Read);

                    // The deferred lighting shader reads URP-managed globals such as
                    // screen-space shadows, SSAO, light cookies, and shadow atlases.
                    builder.UseAllGlobalTextures(true);
                    builder.SetRenderAttachment(tempLightingTarget, 0, AccessFlags.Write);
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc((LightingPassData data, RasterGraphContext context) =>
                    {
                        RasterCommandBuffer cmd = context.cmd;
                        if (data.gBuffers != null)
                        {
                            for (int i = 0; i < data.gBuffers.Length; i++)
                                if (data.gBuffers[i].IsValid()) cmd.SetGlobalTexture(ShaderGBufferIDs[i], data.gBuffers[i]);
                        }

                        if (data.depthTexture.IsValid()) cmd.SetGlobalTexture(CameraDepthTextureID, data.depthTexture);
                        if (data.sourceColorTexture.IsValid()) cmd.SetGlobalTexture(LoogaSourceColorTextureID, data.sourceColorTexture);
                        if (data.ssssProfileTexture.IsValid()) cmd.SetGlobalTexture(SSSSProfileTextureID, data.ssssProfileTexture);
                        if (data.ssssProfileExtraTexture.IsValid()) cmd.SetGlobalTexture(SSSSProfileExtraTextureID, data.ssssProfileExtraTexture);
                        if (data.materialExtrasTexture.IsValid()) cmd.SetGlobalTexture(LoogaMaterialExtrasTextureID, data.materialExtrasTexture);
                        if (data.modelParametersTexture.IsValid()) cmd.SetGlobalTexture(LoogaModelParametersTextureID, data.modelParametersTexture);
                        cmd.SetGlobalInteger(LoogaHasSSSSProfileTextureID, data.hasSSSSProfileTexture ? 1 : 0);
                        cmd.SetGlobalInteger(LoogaHasRenderingLayersTextureID, data.renderingLayersTexture.IsValid() ? 1 : 0);
                        if (data.renderingLayersTexture.IsValid()) cmd.SetGlobalTexture(LoogaRenderingLayersTextureID, data.renderingLayersTexture);
                        cmd.SetGlobalInteger(LoogaHasShadowMaskTextureID, data.shadowMaskTexture.IsValid() ? 1 : 0);
                        if (data.shadowMaskTexture.IsValid()) cmd.SetGlobalTexture(LoogaShadowMaskTextureID, data.shadowMaskTexture);
                        cmd.SetGlobalInteger(GBufferNormalsAreOctID, data.useAccurateGBufferNormals ? 1 : 0);
                        cmd.SetGlobalVector(MainLightPositionID, data.mainLightPosition);
                        cmd.SetGlobalVector(MainLightColorID, data.mainLightColor);

                        Blitter.BlitTexture(cmd, new Vector4(1,1,0,0), data.material, 0);
                    });
                }

                if (_feature.enableSubsurfaceScattering && ssssProfileTarget.IsValid())
                {
                    TextureHandle ssssPingPong = renderGraph.CreateTexture(new TextureDesc(desc) { name = "SSSS PingPong Target" });

                    using (var builder = renderGraph.AddRasterRenderPass<SSSSPassData>("Looga SSSS Horizontal", out var passData))
                    {
                        passData.source = tempLightingTarget;
                        passData.material = _feature._ssssMaterial;
                        passData.passIndex = 0;

                        builder.UseTexture(passData.source, AccessFlags.Read);
                        builder.SetRenderAttachment(ssssPingPong, 0, AccessFlags.Write);
                        builder.SetRenderAttachmentDepth(hardwareDepth, AccessFlags.Read);
                        builder.UseTexture(ssssProfileTarget, AccessFlags.Read);
                        builder.UseTexture(ssssProfileExtraTarget, AccessFlags.Read);
                        builder.AllowGlobalStateModification(true);

                        builder.SetRenderFunc((SSSSPassData data, RasterGraphContext context) =>
                        {
                            context.cmd.SetGlobalTexture(SSSSProfileTextureID, ssssProfileTarget);
                            context.cmd.SetGlobalTexture(SSSSProfileExtraTextureID, ssssProfileExtraTarget);
                            Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, data.passIndex);
                        });
                    }

                    using (var builder = renderGraph.AddRasterRenderPass<SSSSPassData>("Looga SSSS Vertical", out var passData))
                    {
                        passData.source = ssssPingPong;
                        passData.material = _feature._ssssMaterial;
                        passData.passIndex = 1;

                        builder.UseTexture(passData.source, AccessFlags.Read);
                        builder.SetRenderAttachment(tempLightingTarget, 0, AccessFlags.Write);
                        builder.SetRenderAttachmentDepth(hardwareDepth, AccessFlags.Read);
                        builder.UseTexture(ssssProfileTarget, AccessFlags.Read);
                        builder.UseTexture(ssssProfileExtraTarget, AccessFlags.Read);
                        builder.AllowGlobalStateModification(true);

                        builder.SetRenderFunc((SSSSPassData data, RasterGraphContext context) =>
                        {
                            context.cmd.SetGlobalTexture(SSSSProfileTextureID, ssssProfileTarget);
                            context.cmd.SetGlobalTexture(SSSSProfileExtraTextureID, ssssProfileExtraTarget);
                            Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, data.passIndex);
                        });
                    }
                }

                using (var builder = renderGraph.AddRasterRenderPass<BlitPassData>("Looga Lighting Blit", out var passData))
                {
                    passData.source = tempLightingTarget;

                    builder.UseTexture(passData.source, AccessFlags.Read);

                    builder.SetRenderAttachment(activeColor, 0, AccessFlags.Write);

                    builder.SetRenderFunc((BlitPassData data, RasterGraphContext context) =>
                    {
                        RasterCommandBuffer cmd = context.cmd;
                        Blitter.BlitTexture(cmd, data.source, new Vector4(1,1,0,0), 0.0f, false);
                    });
                }

                if (stencilTexture.IsValid())
                {
                    using (var builder = renderGraph.AddRasterRenderPass<StencilClearPassData>("Looga Clear Deferred Stencil", out var passData))
                    {
                        passData.material = _feature._activeLightingMaterial;

                        // The fullscreen pass only clears deferred stencil bits. Preserve the
                        // existing hardware depth so later forward/transparent draws still test
                        // against opaque geometry and custom overlay depth.
                        builder.SetRenderAttachmentDepth(stencilTexture, AccessFlags.ReadWrite);
                        builder.AllowGlobalStateModification(true);

                        builder.SetRenderFunc((StencilClearPassData data, RasterGraphContext context) =>
                        {
                            RasterCommandBuffer cmd = context.cmd;

                            if (data.material != null)
                                Blitter.BlitTexture(cmd, new Vector4(1,1,0,0), data.material, 1);
                        });
                    }
                }
            }

            private static void GetMainLightConstants(UniversalLightData lightData, out Vector4 lightPosition, out Vector4 lightColor)
            {
                lightPosition = new Vector4(0f, 0f, 1f, 0f);
                lightColor = Vector4.zero;

                int mainLightIndex = lightData.mainLightIndex;
                if (mainLightIndex < 0 || mainLightIndex >= lightData.visibleLights.Length)
                    return;

                VisibleLight visibleLight = lightData.visibleLights[mainLightIndex];
                Matrix4x4 lightLocalToWorld = visibleLight.localToWorldMatrix;

                if (visibleLight.lightType == LightType.Directional)
                {
                    Vector4 direction = -lightLocalToWorld.GetColumn(2);
                    lightPosition = new Vector4(direction.x, direction.y, direction.z, 0f);
                }
                else
                {
                    Vector4 position = lightLocalToWorld.GetColumn(3);
                    lightPosition = new Vector4(position.x, position.y, position.z, 1f);
                }

                lightColor = visibleLight.finalColor;
            }
        }
    }

}
