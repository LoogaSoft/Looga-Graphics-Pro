using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.ShaderGraph;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using static Unity.Rendering.Universal.ShaderUtils;

namespace UnityEditor.Rendering.Universal.ShaderGraph
{
    [GenerateBlocks("Looga Lighting")]
    internal struct LoogaSurfaceDescription
    {
        public static string name = "SurfaceDescription";

        public static readonly BlockFieldDescriptor MinnaertK = FloatBlock("LoogaMinnaertK", "Minnaert k", 0.7f);
        public static readonly BlockFieldDescriptor MinnaertIndirectModel = FloatBlock("LoogaMinnaertIndirectModel", "Minnaert Indirect Model", 1.0f);
        public static readonly BlockFieldDescriptor OverwatchWrap = FloatBlock("LoogaOverwatchWrap", "Overwatch Wrap", 0.08f);
        public static readonly BlockFieldDescriptor OrenNayarSigma = FloatBlock("LoogaOrenNayarSigma", "Oren-Nayar Sigma", 30.0f);
        public static readonly BlockFieldDescriptor OrenNayarIndirectModel = FloatBlock("LoogaOrenNayarIndirectModel", "Oren-Nayar Indirect Model", 0.0f);
        public static readonly BlockFieldDescriptor ArkaneBandCount = FloatBlock("LoogaArkaneBandCount", "Arkane Band Count", 3.0f);
        public static readonly BlockFieldDescriptor ArkaneBandFeather = FloatBlock("LoogaArkaneBandFeather", "Arkane Band Feather", 0.15f);
        public static readonly BlockFieldDescriptor SecondarySmoothness = FloatBlock("LoogaSecondarySmoothness", "Secondary Lobe Smoothness", 0.5f);
        public static readonly BlockFieldDescriptor SecondaryLobeMix = FloatBlock("LoogaSecondaryLobeMix", "Secondary Lobe Mix", 0.0f);
        public static readonly BlockFieldDescriptor SubsurfaceColor = ColorBlock("LoogaSubsurfaceColor", "Subsurface Color", new Color(1.0f, 0.5f, 0.4f));
        public static readonly BlockFieldDescriptor ScatterWidth = FloatBlock("LoogaScatterWidth", "Subsurface Scatter Width", 0.0f);
        public static readonly BlockFieldDescriptor AmbientScatter = FloatBlock("LoogaAmbientScatter", "Subsurface Ambient Scatter", 0.2f);
        public static readonly BlockFieldDescriptor Transmission = FloatBlock("LoogaTransmission", "Transmission Mask", 0.0f);
        public static readonly BlockFieldDescriptor TransmissionShadowSoftness = FloatBlock("LoogaTransmissionShadowSoftness", "Transmission Shadow Softness", 0.5f);
        public static readonly BlockFieldDescriptor BacklightRimPower = FloatBlock("LoogaBacklightRimPower", "Backlight Rim Tightness", 4.0f);
        public static readonly BlockFieldDescriptor BacklightDistortion = FloatBlock("LoogaBacklightDistortion", "Backlight Distortion", 0.2f);

        private static BlockFieldDescriptor FloatBlock(string referenceName, string displayName, float value)
        {
            return new BlockFieldDescriptor(name, referenceName, displayName,
                $"SURFACEDESCRIPTION_{referenceName.ToUpperInvariant()}", new FloatControl(value), ShaderStage.Fragment);
        }

        private static BlockFieldDescriptor ColorBlock(string referenceName, string displayName, Color value)
        {
            return new BlockFieldDescriptor(name, referenceName, displayName,
                $"SURFACEDESCRIPTION_{referenceName.ToUpperInvariant()}", new ColorControl(value, false), ShaderStage.Fragment);
        }
    }

    sealed class LoogaLitSubTarget : UniversalSubTarget
    {
        private static readonly GUID SourceCodeGuid = new GUID("ecd1878ecd3722b46ac75e0b79b0e9c8");
        private const string GBufferPassPath = "Packages/com.loogasoft.loogagraphicspro/Includes/ShaderGraph/LoogaShaderGraphGBufferPass.hlsl";
        private const string ForwardPassPath = "Packages/com.loogasoft.loogagraphicspro/Includes/ShaderGraph/LoogaShaderGraphForwardPass.hlsl";
        private const string MaterialExtrasPassPath = "Packages/com.loogasoft.loogagraphicspro/Includes/ShaderGraph/LoogaShaderGraphMaterialExtrasPass.hlsl";
        private const string SsssPassPath = "Packages/com.loogasoft.loogagraphicspro/Includes/ShaderGraph/LoogaShaderGraphSSSSPass.hlsl";

        [SerializeField] private WorkflowMode m_WorkflowMode = WorkflowMode.Metallic;
        [SerializeField] private NormalDropOffSpace m_NormalDropOffSpace = NormalDropOffSpace.Tangent;
        [SerializeField] private bool m_BlendModePreserveSpecular = true;

        public LoogaLitSubTarget()
        {
            displayName = "Looga Lit";
        }

        protected override ShaderID shaderID => ShaderID.SG_Lit;
        public override bool IsActive() => true;

        private UniversalLitSubTarget CreateLitDelegate()
        {
            return new UniversalLitSubTarget
            {
                target = target,
                workflowMode = m_WorkflowMode,
                normalDropOffSpace = m_NormalDropOffSpace,
                clearCoat = false,
                blendModePreserveSpecular = m_BlendModePreserveSpecular
            };
        }

        public override void Setup(ref TargetSetupContext context)
        {
            context.AddAssetDependency(SourceCodeGuid, AssetCollection.Flags.SourceDependency);
            base.Setup(ref context);

            Type universalRpType = typeof(UniversalRenderPipelineAsset);
            if (!context.HasCustomEditorForRenderPipeline(universalRpType))
                context.AddCustomEditorForRenderPipeline(typeof(ShaderGraphLitGUI).FullName, universalRpType);

            UniversalLitSubTarget lit = CreateLitDelegate();
            TargetSetupContext litContext = new TargetSetupContext(context.assetCollection);
            lit.Setup(ref litContext);

            SubShaderDescriptor subShader = litContext.subShaders.First();
            subShader.passes = BuildLoogaPasses(subShader.passes);
            context.AddSubShader(PostProcessSubShader(subShader));
        }

        private PassCollection BuildLoogaPasses(PassCollection source)
        {
            PassCollection result = new PassCollection();
            bool addedSidecars = false;

            foreach (PassCollection.Item item in source)
            {
                PassDescriptor pass = item.descriptor;
                if (pass.referenceName == "SHADERPASS_GBUFFER")
                {
                    pass.validPixelBlocks = LoogaBlockMasks.FragmentLit;
                    pass.includes = ReplacePostGraphInclude(pass.includes, "PBRGBufferPass.hlsl", GBufferPassPath);
                    pass.renderStates = CloneRenderStates(pass.renderStates);
                    pass.renderStates.Add(RenderState.Stencil(new StencilDescriptor
                    {
                        Ref = "96",
                        Comp = "Always",
                        Pass = "Replace",
                        WriteMask = "96"
                    }));
                }
                else if (pass.referenceName == "SHADERPASS_FORWARD" || pass.referenceName == "SHADERPASS_FORWARDONLY")
                {
                    pass.validPixelBlocks = LoogaBlockMasks.FragmentLit;
                    pass.includes = ReplacePostGraphInclude(pass.includes, "PBRForwardPass.hlsl", ForwardPassPath);
                    pass.pragmas = LoogaPragmas.Forward;
                }

                result.Add(pass, item.fieldConditions);

                if (!addedSidecars && pass.referenceName == "SHADERPASS_GBUFFER")
                {
                    result.Add(CreateMaterialExtrasPass());
                    result.Add(CreateSsssPass());
                    addedSidecars = true;
                }
            }

            return result;
        }

        private static IncludeCollection ReplacePostGraphInclude(IncludeCollection source, string fileName, string replacementPath)
        {
            IncludeCollection result = new IncludeCollection();
            foreach (IncludeDescriptor include in source)
            {
                if (include.path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                result.AddInternal(include.guid, include.path, include.location, include.fieldConditions, include.shouldIncludeWithPragmas);
            }

            result.Add(replacementPath, IncludeLocation.Postgraph);
            return result;
        }

        private static RenderStateCollection CloneRenderStates(RenderStateCollection source)
        {
            RenderStateCollection result = new RenderStateCollection();
            foreach (RenderStateCollection.Item item in source)
                result.Add(item.descriptor, item.fieldConditions);
            return result;
        }

        private PassDescriptor CreateMaterialExtrasPass()
        {
            PassDescriptor pass = CreateSidecarPass(
                "Looga Material Extras",
                "SHADERPASS_LOOGA_MATERIAL_EXTRAS",
                "LoogaMaterialExtras",
                MaterialExtrasPassPath);
            CorePasses.AddAlphaClipControlToPass(ref pass, target);
            CorePasses.AddLODCrossFadeControlToPass(ref pass, target);
            return pass;
        }

        private PassDescriptor CreateSsssPass()
        {
            PassDescriptor pass = CreateSidecarPass(
                "Looga SSSS Profile",
                "SHADERPASS_LOOGA_SSSS_PROFILE",
                "SSSSProfile",
                SsssPassPath);
            CorePasses.AddAlphaClipControlToPass(ref pass, target);
            CorePasses.AddLODCrossFadeControlToPass(ref pass, target);
            return pass;
        }

        private PassDescriptor CreateSidecarPass(string displayName, string referenceName, string lightMode, string includePath)
        {
            return new PassDescriptor
            {
                displayName = displayName,
                referenceName = referenceName,
                lightMode = lightMode,
                useInPreview = false,
                passTemplatePath = UniversalTarget.kUberTemplatePath,
                sharedTemplateDirectories = UniversalTarget.kSharedTemplateDirectories,
                validVertexBlocks = CoreBlockMasks.Vertex,
                validPixelBlocks = LoogaBlockMasks.FragmentLit,
                structs = CoreStructCollections.Default,
                requiredFields = LoogaRequiredFields.Sidecar,
                fieldDependencies = CoreFieldDependencies.Default,
                renderStates = new RenderStateCollection
                {
                    { RenderState.ZWrite(ZWrite.Off) },
                    { RenderState.ZTest(ZTest.Equal) },
                    { CoreRenderStates.UberSwitchedCullRenderState(target) }
                },
                pragmas = LoogaPragmas.Sidecar,
                defines = new DefineCollection(),
                keywords = new KeywordCollection(),
                includes = new IncludeCollection
                {
                    { CoreIncludes.DOTSPregraph },
                    { CoreIncludes.CorePregraph },
                    { CoreIncludes.ShaderGraphPregraph },
                    { CoreIncludes.CorePostgraph },
                    { includePath, IncludeLocation.Postgraph }
                },
                customInterpolators = CoreCustomInterpDescriptors.Common
            };
        }

        public override void ProcessPreviewMaterial(Material material)
        {
            CreateLitDelegate().ProcessPreviewMaterial(material);
        }

        public override void GetFields(ref TargetFieldContext context)
        {
            CreateLitDelegate().GetFields(ref context);
        }

        public override void GetActiveBlocks(ref TargetActiveBlockContext context)
        {
            CreateLitDelegate().GetActiveBlocks(ref context);
            foreach (BlockFieldDescriptor block in LoogaBlockMasks.ModelInputs)
                context.AddBlock(block);
        }

        public override void CollectShaderProperties(PropertyCollector collector, GenerationMode generationMode)
        {
            CreateLitDelegate().CollectShaderProperties(collector, generationMode);
        }

        public override void GetPropertiesGUI(ref TargetPropertyGUIContext context, Action onChange, Action<string> registerUndo)
        {
            UniversalTarget universalTarget = target;
            universalTarget.AddDefaultMaterialOverrideGUI(ref context, onChange, registerUndo);

            context.AddProperty("Workflow Mode", new EnumField(m_WorkflowMode), evt =>
            {
                if (Equals(m_WorkflowMode, evt.newValue)) return;
                registerUndo("Change Workflow");
                m_WorkflowMode = (WorkflowMode)evt.newValue;
                onChange();
            });

            universalTarget.AddDefaultSurfacePropertiesGUI(ref context, onChange, registerUndo, showReceiveShadows: true);

            context.AddProperty("Fragment Normal Space", new EnumField(m_NormalDropOffSpace), evt =>
            {
                if (Equals(m_NormalDropOffSpace, evt.newValue)) return;
                registerUndo("Change Fragment Normal Space");
                m_NormalDropOffSpace = (NormalDropOffSpace)evt.newValue;
                onChange();
            });

            if (target.surfaceType == SurfaceType.Transparent &&
                (target.alphaMode == AlphaMode.Alpha || target.alphaMode == AlphaMode.Additive))
            {
                context.AddProperty("Preserve Specular Lighting", new Toggle { value = m_BlendModePreserveSpecular }, evt =>
                {
                    if (Equals(m_BlendModePreserveSpecular, evt.newValue)) return;
                    registerUndo("Change Preserve Specular");
                    m_BlendModePreserveSpecular = evt.newValue;
                    onChange();
                });
            }
        }

        protected override int ComputeMaterialNeedsUpdateHash()
        {
            int hash = base.ComputeMaterialNeedsUpdateHash();
            return hash * 23 + target.allowMaterialOverride.GetHashCode();
        }

        private static class LoogaBlockMasks
        {
            public static readonly BlockFieldDescriptor[] ModelInputs =
            {
                LoogaSurfaceDescription.MinnaertK,
                LoogaSurfaceDescription.MinnaertIndirectModel,
                LoogaSurfaceDescription.OverwatchWrap,
                LoogaSurfaceDescription.OrenNayarSigma,
                LoogaSurfaceDescription.OrenNayarIndirectModel,
                LoogaSurfaceDescription.ArkaneBandCount,
                LoogaSurfaceDescription.ArkaneBandFeather,
                LoogaSurfaceDescription.SecondarySmoothness,
                LoogaSurfaceDescription.SecondaryLobeMix,
                LoogaSurfaceDescription.SubsurfaceColor,
                LoogaSurfaceDescription.ScatterWidth,
                LoogaSurfaceDescription.AmbientScatter,
                LoogaSurfaceDescription.Transmission,
                LoogaSurfaceDescription.TransmissionShadowSoftness,
                LoogaSurfaceDescription.BacklightRimPower,
                LoogaSurfaceDescription.BacklightDistortion
            };

            public static readonly BlockFieldDescriptor[] FragmentLit =
            {
                BlockFields.SurfaceDescription.BaseColor,
                BlockFields.SurfaceDescription.NormalOS,
                BlockFields.SurfaceDescription.NormalTS,
                BlockFields.SurfaceDescription.NormalWS,
                BlockFields.SurfaceDescription.Emission,
                BlockFields.SurfaceDescription.Metallic,
                BlockFields.SurfaceDescription.Specular,
                BlockFields.SurfaceDescription.Smoothness,
                BlockFields.SurfaceDescription.Occlusion,
                BlockFields.SurfaceDescription.Alpha,
                BlockFields.SurfaceDescription.AlphaClipThreshold,
                LoogaSurfaceDescription.MinnaertK,
                LoogaSurfaceDescription.MinnaertIndirectModel,
                LoogaSurfaceDescription.OverwatchWrap,
                LoogaSurfaceDescription.OrenNayarSigma,
                LoogaSurfaceDescription.OrenNayarIndirectModel,
                LoogaSurfaceDescription.ArkaneBandCount,
                LoogaSurfaceDescription.ArkaneBandFeather,
                LoogaSurfaceDescription.SecondarySmoothness,
                LoogaSurfaceDescription.SecondaryLobeMix,
                LoogaSurfaceDescription.SubsurfaceColor,
                LoogaSurfaceDescription.ScatterWidth,
                LoogaSurfaceDescription.AmbientScatter,
                LoogaSurfaceDescription.Transmission,
                LoogaSurfaceDescription.TransmissionShadowSoftness,
                LoogaSurfaceDescription.BacklightRimPower,
                LoogaSurfaceDescription.BacklightDistortion
            };
        }

        private static class LoogaRequiredFields
        {
            public static readonly FieldCollection Sidecar = new FieldCollection
            {
                StructFields.Attributes.uv0,
                StructFields.Varyings.positionWS,
                StructFields.Varyings.normalWS,
                StructFields.Varyings.tangentWS,
                StructFields.Varyings.texCoord0
            };
        }

        private static class LoogaPragmas
        {
            public static readonly PragmaCollection Forward = new PragmaCollection
            {
                { Pragma.Target(ShaderModel.Target45) },
                { Pragma.MultiCompileInstancing },
                { Pragma.InstancingOptions(InstancingOptions.RenderingLayer) },
                { Pragma.Vertex("vert") },
                { Pragma.Fragment("frag") }
            };

            public static readonly PragmaCollection Sidecar = new PragmaCollection
            {
                { Pragma.Target(ShaderModel.Target45) },
                { Pragma.MultiCompileInstancing },
                { Pragma.Vertex("vert") },
                { Pragma.Fragment("frag") }
            };
        }
    }

    static class CreateLoogaLitShaderGraph
    {
        [MenuItem("Assets/Create/Shader Graph/URP/Looga Lit Shader Graph", priority = CoreUtils.Priorities.assetsCreateShaderMenuPriority + 2)]
        public static void CreateGraph()
        {
            UniversalTarget target = (UniversalTarget)Activator.CreateInstance(typeof(UniversalTarget));
            target.TrySetActiveSubTarget(typeof(LoogaLitSubTarget));

            BlockFieldDescriptor[] blocks =
            {
                BlockFields.VertexDescription.Position,
                BlockFields.VertexDescription.Normal,
                BlockFields.VertexDescription.Tangent,
                BlockFields.SurfaceDescription.BaseColor,
                BlockFields.SurfaceDescription.NormalTS,
                BlockFields.SurfaceDescription.Metallic,
                BlockFields.SurfaceDescription.Smoothness,
                BlockFields.SurfaceDescription.Emission,
                BlockFields.SurfaceDescription.Occlusion
            };

            GraphUtil.CreateNewGraphWithOutputs(new[] { target }, blocks);
        }
    }

}
