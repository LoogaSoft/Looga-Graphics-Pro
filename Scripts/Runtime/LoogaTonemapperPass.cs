using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace LoogaSoft.Tonemapper.Runtime
{
    public class LoogaTonemapperPass : ScriptableRenderPass
    {
        private Material material;
        
        private static readonly int ModeId = Shader.PropertyToID("_TonemapMode");
        private static readonly int PreExposureId = Shader.PropertyToID("_PreExposure");
        private static readonly int PostExposureId = Shader.PropertyToID("_PostExposure");
        
        private static readonly int BlackPointId = Shader.PropertyToID("_BlackPoint");
        private static readonly int WhitePointId = Shader.PropertyToID("_WhitePoint");
        private static readonly int ContrastId = Shader.PropertyToID("_Contrast");
        private static readonly int SaturationId = Shader.PropertyToID("_Saturation");
        
        private static readonly int SigmoidCurveId = Shader.PropertyToID("_SigmoidCurve");
        private static readonly int ReinhardLimitId = Shader.PropertyToID("_ReinhardLimit");

        public LoogaTonemapperPass(Material mat)
        {
            material = mat;
        }

        public void UpdateMaterial(Material mat)
        {
            material = mat;
        }

        private class PassData
        {
            public TextureHandle source;
            public Material material;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var volume = VolumeManager.instance.stack.GetComponent<LoogaTonemapper>();
            if (volume == null || !volume.IsActive()) return;
            if (material == null) return;

            var resourceData = frameData.Get<UniversalResourceData>();
            TextureHandle sourceTexture = resourceData.activeColorTexture;

            TextureDesc tempDesc = renderGraph.GetTextureDesc(sourceTexture);
            tempDesc.name = "LoogaTonemap_SourceCopy";
            tempDesc.clearBuffer = false; 
            TextureHandle tempTexture = renderGraph.CreateTexture(tempDesc);

            // Update Material Properties
            material.SetInteger(ModeId, (int)volume.tonemapMode.value);
            material.SetFloat(PreExposureId, Mathf.Pow(2.0f, volume.preExposure.value));
            material.SetFloat(PostExposureId, Mathf.Pow(2.0f, volume.postExposure.value));
            
            material.SetFloat(BlackPointId, volume.blackPoint.value);
            material.SetFloat(WhitePointId, volume.whitePoint.value);
            material.SetFloat(ContrastId, volume.contrast.value);
            material.SetFloat(SaturationId, volume.saturation.value);
            
            material.SetFloat(SigmoidCurveId, volume.sigmoidCurve.value);
            material.SetFloat(ReinhardLimitId, volume.reinhardLimit.value);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Looga Tonemapper Copy", out var passData))
            {
                passData.source = sourceTexture;
                builder.UseTexture(sourceTexture, AccessFlags.Read);
                builder.SetRenderAttachment(tempTexture, 0, AccessFlags.Write);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), 0.0f, false);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Looga Tonemapper Apply", out var passData))
            {
                passData.source = tempTexture;
                passData.material = material;
                builder.UseTexture(tempTexture, AccessFlags.Read);
                builder.SetRenderAttachment(sourceTexture, 0, AccessFlags.Write);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1, 1, 0, 0), data.material, 0);
                });
            }
        }
    }
}
