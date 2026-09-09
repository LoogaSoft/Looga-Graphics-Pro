using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Temporary, in-memory test feature. Never added to a saved renderer asset.
public sealed class LoogaDepthIsolationProbe : ScriptableRendererFeature
{
    public RenderTexture result;
    public int executions;
    RTHandle target;
    Material material;
    ProbePass pass;
    public override void Create()
    {
        if (!material) material = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/Tests/LoogaDepthIsolation"));
        pass = new ProbePass(this) { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
        pass.ConfigureInput(ScriptableRenderPassInput.Depth);
    }
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
    {
        if (!result || data.cameraData.cameraType != CameraType.Game) return;
        if (target == null) target = RTHandles.Alloc(result);
        renderer.EnqueuePass(pass);
    }
    protected override void Dispose(bool disposing)
    {
        target?.Release();target=null;CoreUtils.Destroy(material);material=null;
    }
    sealed class ProbePass : ScriptableRenderPass
    {
        readonly LoogaDepthIsolationProbe owner;
        public ProbePass(LoogaDepthIsolationProbe owner) { this.owner=owner; }
        sealed class Data { public TextureHandle depth; public Material material; }
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (!resources.cameraDepthTexture.IsValid()) return;
            using var builder = graph.AddRasterRenderPass<Data>("Validate Looga scene depth isolation", out var data);
            data.depth=resources.cameraDepthTexture;data.material=owner.material;
            builder.UseTexture(data.depth,AccessFlags.Read);
            builder.UseAllGlobalTextures(true);
            builder.SetRenderAttachment(graph.ImportTexture(owner.target),0);
            builder.AllowGlobalStateModification(true);builder.AllowPassCulling(false);
            builder.SetRenderFunc((Data d,RasterGraphContext context) => {
                context.cmd.SetGlobalTexture("_LoogaValidationExpectedDepth",d.depth);
                Blitter.BlitTexture(context.cmd,new Vector4(1,1,0,0),d.material,0);
                owner.executions++;
            });
        }
    }
}
