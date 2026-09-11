using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace LoogaSoft.Rendering.StreamingVirtualTexturing
{
    /// <summary>Collects visible virtual texture requests without modifying camera culling.</summary>
    [DisallowMultipleRendererFeature("Looga Streaming Virtual Texture")]
    public sealed class LoogaStreamingVirtualTextureFeature : ScriptableRendererFeature
    {
        [SerializeField, Range(1, 8)] private int _feedbackDownsample = 4;
        [SerializeField] private bool _sceneView = true;
        private FeedbackPass _pass;

        public override void Create()
        {
            _pass?.Dispose();
            _pass = new FeedbackPass { renderPassEvent = RenderPassEvent.AfterRenderingOpaques };
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            if (!LoogaSvtRegistry.HasCaches || !SystemInfo.supportsAsyncGPUReadback ||
                (camera.cameraType != CameraType.Game && (!_sceneView || camera.cameraType != CameraType.SceneView)) ||
                renderingData.cameraData.renderType == CameraRenderType.Overlay)
                return;
            LoogaSvtRegistry.Tick();
            _pass.Downsample = Mathf.Clamp(_feedbackDownsample, 1, 8);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            _pass?.Dispose();
            _pass = null;
        }

        private sealed class FeedbackPass : ScriptableRenderPass, IDisposable
        {
            private sealed class Data
            {
                public TextureHandle Output;
                public RendererListHandle Renderers;
                public FeedbackPass Owner;
                public float Scale;
                public Dictionary<int, LoogaSvtCache> Caches;
            }

            public int Downsample = 4;
            private bool _pending;
            private bool _disposed;
            private readonly HashSet<uint> _unique = new();

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                if (_pending || _disposed)
                    return;
                var camera = frame.Get<UniversalCameraData>();
                var resources = frame.Get<UniversalResourceData>();
                var rendering = frame.Get<UniversalRenderingData>();
                var lights = frame.Get<UniversalLightData>();
                if (!resources.cameraDepthTexture.IsValid())
                    return;
                int width = Mathf.Max(1, camera.cameraTargetDescriptor.width / Downsample);
                int height = Mathf.Max(1, camera.cameraTargetDescriptor.height / Downsample);
                var output = graph.CreateTexture(new TextureDesc(width, height)
                {
                    name = "Looga SVT feedback", colorFormat = GraphicsFormat.R32_UInt,
                    clearBuffer = true, clearColor = Color.clear
                });
                var drawing = RenderingUtils.CreateDrawingSettings(new ShaderTagId("LoogaSvtFeedback"), rendering, camera, lights, SortingCriteria.CommonOpaque);
                drawing.enableInstancing = false;
                var filtering = new FilteringSettings(RenderQueueRange.opaque);
                var list = graph.CreateRendererList(new RendererListParams(rendering.cullResults, drawing, filtering));
                using var builder = graph.AddUnsafePass<Data>("Looga SVT feedback and readback", out var data);
                data.Output = output;
                data.Renderers = list;
                data.Owner = this;
                data.Scale = 1f / Downsample;
                data.Caches = LoogaSvtRegistry.Snapshot();
                builder.UseTexture(output, AccessFlags.Write);
                builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                builder.UseRendererList(list);
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (Data pass, UnsafeGraphContext context) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    cmd.SetRenderTarget(pass.Output);
                    cmd.ClearRenderTarget(false, true, Color.clear);
                    cmd.SetGlobalFloat("_LoogaSvtFeedbackScale", pass.Scale);
                    cmd.DrawRendererList(pass.Renderers);
                    pass.Owner._pending = true;
                    var owner = pass.Owner;
                    var caches = pass.Caches;
                    cmd.RequestAsyncReadback(pass.Output, 0, request => owner.Readback(request, caches));
                });
            }

            private void Readback(AsyncGPUReadbackRequest request, Dictionary<int, LoogaSvtCache> caches)
            {
                _pending = false;
                if (_disposed || request.hasError)
                    return;
                _unique.Clear();
                foreach (uint value in request.GetData<uint>())
                {
                    if (value != 0 && _unique.Add(value))
                    {
                        if (caches.TryGetValue((int)(value >> 20), out LoogaSvtCache cache))
                        {
                            cache.RequestPage((int)((value >> 16) & 15), (int)(value & 255), (int)((value >> 8) & 255));
                        }
                    }
                }
            }

            public void Dispose()
            {
                _disposed = true;
            }
        }
    }
}
