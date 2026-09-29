using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace LoogaSoft.PostProcessing
{
    /// <summary>
    /// Renders Looga bloom before URP post-processing, while the camera color is still linear HDR.
    /// Compute dispatches build the bloom in transient Render Graph textures, either with the mip pyramid or with the
    /// FFT convolution. They use an unsafe pass, so the renderer and the tests share one dispatch path that takes a
    /// command buffer. A raster pass then writes the scene with the bloom and the lens dirt to a new camera color texture.
    /// </summary>
    internal sealed class LoogaBloomPass : ScriptableRenderPass
    {
        // Half-float targets support random writes on all compute platforms.
        private const GraphicsFormat PyramidFormat = GraphicsFormat.R16G16B16A16_SFloat;

        private static readonly int BloomTextureId = Shader.PropertyToID("_LoogaBloomTexture");
        private static readonly int BloomScaleOffsetId = Shader.PropertyToID("_LoogaBloomScaleOffset");
        private static readonly int BloomThresholdId = Shader.PropertyToID("_LoogaBloomThreshold");
        private static readonly int BloomTexelSizeId = Shader.PropertyToID("_LoogaBloomTexelSize");
        private static readonly int BloomParamsId = Shader.PropertyToID("_LoogaBloomParams");
        private static readonly int BloomTintId = Shader.PropertyToID("_LoogaBloomTint");
        private static readonly int LensDirtTextureId = Shader.PropertyToID("_LoogaLensDirtTexture");
        private static readonly int LensDirtScaleOffsetId = Shader.PropertyToID("_LoogaLensDirtScaleOffset");

        private static readonly string[] DownNames = LevelNames("Looga Bloom Down ");
        private static readonly string[] UpNames = LevelNames("Looga Bloom Up ");

        private LoogaBloomKernels _kernels;
        private LoogaBloomFFT _fft;
        private Material _compositeMaterial;
        private LoogaPostProcessing _settings;

        // The kernel spectrum persists between frames. It is rebuilt only when its inputs change.
        private RTHandle _kernelSpectrum;
        private KernelKey _kernelKey;

        public LoogaBloomPass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            // Compute kernels cannot read the back buffer.
            requiresIntermediateTexture = true;
        }

        public void Setup(LoogaBloomKernels kernels, LoogaBloomFFT fft, Material compositeMaterial,
            LoogaPostProcessing settings)
        {
            _kernels = kernels;
            _fft = fft;
            _compositeMaterial = compositeMaterial;
            _settings = settings;
        }

        public void Dispose()
        {
            _kernelSpectrum?.Release();
            _kernelSpectrum = null;
            _kernelKey = default;
        }

        // Inputs of the kernel spectrum. The texture update count detects a changed texture with the same instance.
        private readonly struct KernelKey : System.IEquatable<KernelKey>
        {
            private readonly int _size;
            private readonly float _kernelSize;
            private readonly int _textureId;
            private readonly uint _textureUpdateCount;

            public KernelKey(int size, float kernelSize, Texture texture)
            {
                _size = size;
                _kernelSize = kernelSize;
                _textureId = texture != null ? texture.GetInstanceID() : 0;
                _textureUpdateCount = texture != null ? texture.updateCount : 0u;
            }

            public bool Equals(KernelKey other) => _size == other._size && _kernelSize.Equals(other._kernelSize) &&
                _textureId == other._textureId && _textureUpdateCount == other._textureUpdateCount;
        }

        private sealed class BloomPassData
        {
            public LoogaBloomKernels kernels;
            public LoogaBloomFFT fft;
            public TextureHandle source;
            public Vector2Int sourceSize;
            public int levelCount;
            public LoogaBloomSettings settings;
            public readonly TextureHandle[] down = new TextureHandle[LoogaBloomKernels.MaxLevels];
            public readonly TextureHandle[] up = new TextureHandle[LoogaBloomKernels.MaxLevels];
            public readonly RenderTargetIdentifier[] downTargets = new RenderTargetIdentifier[LoogaBloomKernels.MaxLevels];
            public readonly RenderTargetIdentifier[] upTargets = new RenderTargetIdentifier[LoogaBloomKernels.MaxLevels];

            // FFT convolution only.
            public int fftSize;
            public Vector2Int region;
            public float kernelSize;
            public Texture kernelTexture;
            public bool buildKernel;
            public TextureHandle kernelSpectrum;
            public TextureHandle fftBuffer;
            public TextureHandle fftOutput;
        }

        private sealed class CompositePassData
        {
            public TextureHandle source;
            public TextureHandle bloom;
            public Material material;
            public Vector4 bloomScaleOffset;
            public Vector4 texelSize;
            public Vector4 bloomParams;
            public Vector4 threshold;
            public Vector4 tint;
            public Texture lensDirt;
            public Vector4 lensDirtScaleOffset;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_kernels == null || _compositeMaterial == null || _settings == null) return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;
            TextureHandle source = resourceData.activeColorTexture;
            TextureDesc sourceDesc = renderGraph.GetTextureDesc(source);
            // Compute kernels load single texels. Multisampled color is not supported.
            if (sourceDesc.msaaSamples != MSAASamples.None) return;

            Vector2Int sourceSize = new Vector2Int(sourceDesc.width, sourceDesc.height);
            if (LoogaBloomKernels.GetLevelCount(sourceSize.x, sourceSize.y, LoogaBloomKernels.MaxLevels) < 1) return;
            bool useFFT = _settings.IsFFTBloom() && _fft != null;

            // The threshold is measured after pre-exposure, so it matches the brightness the viewer sees.
            LoogaBloomSettings settings = new LoogaBloomSettings(_settings.bloomThreshold.value / _settings.PreExposureScale,
                _settings.bloomKnee.value, _settings.bloomScatter.value);
            TextureHandle bloom;
            Vector4 bloomScaleOffset = new Vector4(1f, 1f, 0f, 0f);
            Vector4 texelSize;
            using (IUnsafeRenderGraphBuilder builder =
                   renderGraph.AddUnsafePass("Looga Bloom", out BloomPassData passData))
            {
                passData.kernels = _kernels;
                passData.fft = useFFT ? _fft : null;
                passData.source = source;
                passData.sourceSize = sourceSize;
                passData.settings = settings;
                builder.UseTexture(source, AccessFlags.Read);

                if (useFFT)
                {
                    int fftSize = (int)_settings.bloomFFTSize.value;
                    float kernelSize = _settings.bloomKernelSize.value;
                    Vector2Int region = LoogaBloomFFT.GetRegionSize(sourceSize.x, sourceSize.y, fftSize, kernelSize);
                    passData.fftSize = fftSize;
                    passData.region = region;
                    passData.kernelSize = kernelSize;
                    passData.kernelTexture = _settings.bloomKernelTexture.value;
                    passData.levelCount = LoogaBloomKernels.GetLevelCountForTarget(sourceSize.x, sourceSize.y, region);
                    passData.buildKernel = EnsureKernelSpectrum(fftSize, kernelSize, passData.kernelTexture);
                    passData.kernelSpectrum = renderGraph.ImportTexture(_kernelSpectrum);
                    passData.fftBuffer = CreateFFTBuffer(renderGraph, fftSize, "Looga Bloom FFT Spectrum");
                    passData.fftOutput = CreateFFTBuffer(renderGraph, fftSize, "Looga Bloom FFT Output");
                    builder.UseTexture(passData.kernelSpectrum, passData.buildKernel ? AccessFlags.ReadWrite : AccessFlags.Read);
                    builder.UseTexture(passData.fftBuffer, AccessFlags.ReadWrite);
                    builder.UseTexture(passData.fftOutput, AccessFlags.ReadWrite);
                    // The pass writes the persistent kernel spectrum, so it must not be culled.
                    builder.AllowPassCulling(false);
                    bloom = passData.fftOutput;
                    bloomScaleOffset = new Vector4((float)region.x / fftSize, (float)region.y / fftSize, 0f, 0f);
                    texelSize = new Vector4(1f / fftSize, 1f / fftSize, 0f, 0f);
                }
                else
                {
                    passData.levelCount = LoogaBloomKernels.GetLevelCount(sourceSize.x, sourceSize.y,
                        _settings.bloomLevels.value);
                    Vector2Int level0 = LoogaBloomKernels.GetLevelSize(sourceSize.x, sourceSize.y, 0);
                    texelSize = new Vector4(1f / level0.x, 1f / level0.y, 0f, 0f);
                    bloom = default;
                }

                for (int level = 0; level < passData.levelCount; level++)
                {
                    Vector2Int size = LoogaBloomKernels.GetLevelSize(sourceSize.x, sourceSize.y, level);
                    passData.down[level] = CreateLevel(renderGraph, size, DownNames[level]);
                    builder.UseTexture(passData.down[level], AccessFlags.ReadWrite);
                    if (!useFFT && level < passData.levelCount - 1)
                    {
                        passData.up[level] = CreateLevel(renderGraph, size, UpNames[level]);
                        builder.UseTexture(passData.up[level], AccessFlags.ReadWrite);
                    }
                }
                if (!useFFT) bloom = passData.levelCount > 1 ? passData.up[0] : passData.down[0];

                builder.SetRenderFunc((BloomPassData data, UnsafeGraphContext context) => ExecuteBloom(data,
                    CommandBufferHelpers.GetNativeCommandBuffer(context.cmd)));
            }

            TextureDesc destinationDesc = sourceDesc;
            destinationDesc.name = "Looga Bloom Camera Color";
            destinationDesc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);
            using (IRasterRenderGraphBuilder builder =
                   renderGraph.AddRasterRenderPass("Looga Bloom Composite", out CompositePassData passData))
            {
                passData.source = source;
                passData.bloom = bloom;
                passData.material = _compositeMaterial;
                passData.bloomScaleOffset = bloomScaleOffset;
                passData.texelSize = texelSize;
                passData.bloomParams = new Vector4(_settings.bloomIntensity.value, _settings.lensDirtIntensity.value, 0f, 0f);
                passData.threshold = LoogaBloomKernels.ThresholdVector(settings);
                passData.tint = _settings.bloomTint.value;
                Texture dirt = _settings.lensDirtTexture.value;
                bool hasDirt = dirt != null && _settings.lensDirtIntensity.value > 0f;
                passData.lensDirt = hasDirt ? dirt : Texture2D.blackTexture;
                passData.lensDirtScaleOffset = hasDirt ? CoverFit(dirt, sourceSize) : new Vector4(1f, 1f, 0f, 0f);
                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(bloom, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc((CompositePassData data, RasterGraphContext context) =>
                {
                    data.material.SetTexture(BloomTextureId, (RTHandle)data.bloom);
                    data.material.SetVector(BloomScaleOffsetId, data.bloomScaleOffset);
                    data.material.SetVector(BloomThresholdId, data.threshold);
                    data.material.SetVector(BloomTexelSizeId, data.texelSize);
                    data.material.SetVector(BloomParamsId, data.bloomParams);
                    data.material.SetVector(BloomTintId, data.tint);
                    data.material.SetTexture(LensDirtTextureId, data.lensDirt);
                    data.material.SetVector(LensDirtScaleOffsetId, data.lensDirtScaleOffset);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                });
            }
            // Later passes read the composited color. This avoids a copy of the camera color.
            resourceData.cameraColor = destination;
        }

        private static void ExecuteBloom(BloomPassData data, CommandBuffer commandBuffer)
        {
            for (int level = 0; level < data.levelCount; level++)
            {
                data.downTargets[level] = data.down[level];
                if (data.fft == null && level < data.levelCount - 1) data.upTargets[level] = data.up[level];
            }
            data.kernels.DispatchDownsample(commandBuffer, data.source, data.sourceSize, data.downTargets,
                data.levelCount, data.settings);
            if (data.fft == null)
            {
                data.kernels.DispatchUpsample(commandBuffer, data.sourceSize, data.downTargets, data.upTargets,
                    data.levelCount, data.settings.Scatter);
                return;
            }

            if (data.buildKernel)
            {
                data.fft.BuildKernelSpectrum(commandBuffer, data.kernelSpectrum, data.fftSize, data.kernelSize,
                    data.kernelTexture);
            }
            data.fft.Convolve(commandBuffer, data.downTargets[data.levelCount - 1], data.region, data.fftBuffer,
                data.fftOutput, data.kernelSpectrum, data.fftSize);
        }

        // Allocates the kernel spectrum for the size. Returns true when the spectrum must be rebuilt.
        private bool EnsureKernelSpectrum(int size, float kernelSize, Texture kernelTexture)
        {
            var key = new KernelKey(size, kernelSize, kernelTexture);
            bool reallocated = _kernelSpectrum == null || _kernelSpectrum.rt == null || _kernelSpectrum.rt.width != size;
            if (reallocated)
            {
                _kernelSpectrum?.Release();
                _kernelSpectrum = RTHandles.Alloc(size, size, LoogaBloomFFT.BufferFormat, enableRandomWrite: true,
                    filterMode: FilterMode.Point, wrapMode: TextureWrapMode.Repeat, name: "Looga Bloom Kernel Spectrum");
            }
            if (!reallocated && _kernelKey.Equals(key)) return false;
            _kernelKey = key;
            return true;
        }

        private static string[] LevelNames(string prefix)
        {
            var names = new string[LoogaBloomKernels.MaxLevels];
            for (int level = 0; level < names.Length; level++)
            {
                names[level] = prefix + level;
            }
            return names;
        }

        private static TextureHandle CreateLevel(RenderGraph renderGraph, Vector2Int size, string name)
        {
            return renderGraph.CreateTexture(new TextureDesc(size.x, size.y)
            {
                name = name,
                format = PyramidFormat,
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = false
            });
        }

        private static TextureHandle CreateFFTBuffer(RenderGraph renderGraph, int size, string name)
        {
            return renderGraph.CreateTexture(new TextureDesc(size, size)
            {
                name = name,
                format = LoogaBloomFFT.BufferFormat,
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                clearBuffer = false
            });
        }

        // Scale and offset that make the dirt texture cover the screen without stretching. The excess is cropped.
        private static Vector4 CoverFit(Texture dirt, Vector2Int screen)
        {
            float screenAspect = (float)screen.x / screen.y;
            float dirtAspect = (float)dirt.width / dirt.height;
            Vector2 scale = screenAspect > dirtAspect
                ? new Vector2(1f, dirtAspect / screenAspect)
                : new Vector2(screenAspect / dirtAspect, 1f);
            return new Vector4(scale.x, scale.y, 0.5f - 0.5f * scale.x, 0.5f - 0.5f * scale.y);
        }
    }
}
