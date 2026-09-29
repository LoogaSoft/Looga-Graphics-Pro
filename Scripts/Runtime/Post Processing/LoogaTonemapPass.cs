using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace LoogaSoft.PostProcessing
{
    /// <summary>
    /// Applies the Looga tonemap curve and color grading after URP post-processing.
    /// The pass copies the camera color and writes the graded result back to it.
    /// </summary>
    internal sealed class LoogaTonemapPass : ScriptableRenderPass
    {
        private static readonly int ModeId = Shader.PropertyToID("_TonemapMode");
        private static readonly int PreExposureId = Shader.PropertyToID("_PreExposure");
        private static readonly int PostExposureId = Shader.PropertyToID("_PostExposure");
        private static readonly int BlackPointId = Shader.PropertyToID("_BlackPoint");
        private static readonly int WhitePointId = Shader.PropertyToID("_WhitePoint");
        private static readonly int ContrastId = Shader.PropertyToID("_Contrast");
        private static readonly int SaturationId = Shader.PropertyToID("_Saturation");
        private static readonly int SigmoidCurveId = Shader.PropertyToID("_SigmoidCurve");
        private static readonly int ReinhardLimitId = Shader.PropertyToID("_ReinhardLimit");

        private Material _material;
        private LoogaPostProcessing _settings;

        public LoogaTonemapPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            requiresIntermediateTexture = true;
        }

        public void Setup(Material material, LoogaPostProcessing settings)
        {
            _material = material;
            _settings = settings;
        }

        private sealed class PassData
        {
            public TextureHandle source;
            public Material material;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null || _settings == null) return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            TextureHandle sourceTexture = resourceData.activeColorTexture;
            TextureDesc copyDesc = renderGraph.GetTextureDesc(sourceTexture);
            copyDesc.name = "Looga Tonemap Source Copy";
            copyDesc.clearBuffer = false;
            TextureHandle copyTexture = renderGraph.CreateTexture(copyDesc);

            _material.SetInteger(ModeId, (int)_settings.tonemapMode.value);
            _material.SetFloat(PreExposureId, Mathf.Pow(2.0f, _settings.preExposure.value));
            _material.SetFloat(PostExposureId, Mathf.Pow(2.0f, _settings.postExposure.value));
            _material.SetFloat(BlackPointId, _settings.blackPoint.value);
            _material.SetFloat(WhitePointId, _settings.whitePoint.value);
            _material.SetFloat(ContrastId, _settings.contrast.value);
            _material.SetFloat(SaturationId, _settings.saturation.value);
            _material.SetFloat(SigmoidCurveId, _settings.sigmoidCurve.value);
            _material.SetFloat(ReinhardLimitId, _settings.reinhardLimit.value);

            using (IRasterRenderGraphBuilder builder =
                   renderGraph.AddRasterRenderPass("Looga Tonemap Copy", out PassData passData))
            {
                passData.source = sourceTexture;
                builder.UseTexture(sourceTexture, AccessFlags.Read);
                builder.SetRenderAttachment(copyTexture, 0, AccessFlags.Write);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), 0f, false);
                });
            }

            using (IRasterRenderGraphBuilder builder =
                   renderGraph.AddRasterRenderPass("Looga Tonemap Apply", out PassData passData))
            {
                passData.source = copyTexture;
                passData.material = _material;
                builder.UseTexture(copyTexture, AccessFlags.Read);
                builder.SetRenderAttachment(sourceTexture, 0, AccessFlags.Write);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                });
            }
        }
    }
}
