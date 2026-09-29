using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace LoogaSoft.PostProcessing
{
    /// <summary>
    /// Dispatches the FFT convolution bloom kernels of LoogaBloomFFT.compute.
    /// The image fills a region at the origin of an N x N buffer. The kernel radius fits in the rest of the buffer,
    /// so the circular convolution does not wrap light around the image borders.
    /// </summary>
    public sealed class LoogaBloomFFT
    {
        /// <summary>Format of the complex buffers. FFT sums need full float precision.</summary>
        public const GraphicsFormat BufferFormat = GraphicsFormat.R32G32B32A32_SFloat;
        private const int GroupSize = 8;

        private static readonly int ImageId = Shader.PropertyToID("_Image");
        private static readonly int KernelTextureId = Shader.PropertyToID("_KernelTexture");
        private static readonly int SpectrumId = Shader.PropertyToID("_Spectrum");
        private static readonly int KernelSpectrumId = Shader.PropertyToID("_KernelSpectrum");
        private static readonly int BufferId = Shader.PropertyToID("_Buffer");
        private static readonly int SizeId = Shader.PropertyToID("_Size");
        private static readonly int RegionId = Shader.PropertyToID("_Region");
        private static readonly int KernelParamsId = Shader.PropertyToID("_KernelParams");
        private static readonly int VerticalId = Shader.PropertyToID("_Vertical");
        private static readonly int InverseId = Shader.PropertyToID("_Inverse");

        private readonly ComputeShader _shader;
        private readonly int _generateKernel;
        private readonly int _packKernel;
        private readonly int _multiplyKernel;
        private readonly int _fft256Kernel;
        private readonly int _fft512Kernel;
        private readonly int _fft1024Kernel;

        public LoogaBloomFFT(ComputeShader shader)
        {
            _shader = shader;
            _generateKernel = shader.FindKernel("GenerateKernel");
            _packKernel = shader.FindKernel("Pack");
            _multiplyKernel = shader.FindKernel("Multiply");
            _fft256Kernel = shader.FindKernel("FFT256");
            _fft512Kernel = shader.FindKernel("FFT512");
            _fft1024Kernel = shader.FindKernel("FFT1024");
        }

        public ComputeShader Compute => _shader;

        /// <summary>Returns true for the supported buffer sizes: 256, 512 and 1024.</summary>
        public static bool IsSupportedSize(int size) => size == 256 || size == 512 || size == 1024;

        /// <summary>
        /// Returns the largest image side in texels. The image side plus the kernel radius fits in the buffer.
        /// </summary>
        /// <param name="kernelSize">Kernel diameter as a part of the largest image side.</param>
        public static int GetImageExtent(int size, float kernelSize)
        {
            return Mathf.FloorToInt(size / (1f + 0.5f * Mathf.Max(0f, kernelSize)));
        }

        /// <summary>Returns the kernel radius in buffer texels.</summary>
        public static float GetKernelRadius(int size, float kernelSize)
        {
            return Mathf.Max(1f, 0.5f * kernelSize * GetImageExtent(size, kernelSize));
        }

        /// <summary>Returns the image region in the buffer. It keeps the source aspect ratio.</summary>
        public static Vector2Int GetRegionSize(int sourceWidth, int sourceHeight, int size, float kernelSize)
        {
            int extent = GetImageExtent(size, kernelSize);
            if (sourceWidth >= sourceHeight)
            {
                return new Vector2Int(extent, Mathf.Clamp(Mathf.RoundToInt(extent * (float)sourceHeight / sourceWidth), 1, extent));
            }
            return new Vector2Int(Mathf.Clamp(Mathf.RoundToInt(extent * (float)sourceWidth / sourceHeight), 1, extent), extent);
        }

        /// <summary>
        /// Records the kernel spectrum build. Run it only when the size, the kernel size or the kernel texture changes.
        /// </summary>
        /// <param name="kernelSpectrum">N x N random-write target in BufferFormat.</param>
        /// <param name="kernelTexture">Point spread function, centered in the texture. Null uses the default kernel.</param>
        public void BuildKernelSpectrum(CommandBuffer commandBuffer, RenderTargetIdentifier kernelSpectrum, int size,
            float kernelSize, Texture kernelTexture)
        {
            float radius = GetKernelRadius(size, kernelSize);
            float mipLevel = 0f;
            if (kernelTexture != null)
            {
                // Sample a mip level near the kernel resolution, so that a large texture does not alias.
                mipLevel = Mathf.Max(0f, Mathf.Log(Mathf.Max(kernelTexture.width, kernelTexture.height) / (2f * radius), 2f));
            }
            commandBuffer.SetComputeIntParam(_shader, SizeId, size);
            commandBuffer.SetComputeVectorParam(_shader, KernelParamsId,
                new Vector4(radius, mipLevel, kernelTexture != null ? 1f : 0f, 0f));
            commandBuffer.SetComputeTextureParam(_shader, _generateKernel, KernelTextureId,
                kernelTexture != null ? kernelTexture : Texture2D.blackTexture);
            commandBuffer.SetComputeTextureParam(_shader, _generateKernel, BufferId, kernelSpectrum);
            DispatchFor(commandBuffer, _generateKernel, size);
            DispatchFFT(commandBuffer, kernelSpectrum, size, false, false);
            DispatchFFT(commandBuffer, kernelSpectrum, size, true, false);
        }

        /// <summary>Records the convolution of the image with the kernel. The result is in the output buffer.</summary>
        /// <param name="image">Thresholded image. It is sampled with bilinear filtering to the region size.</param>
        /// <param name="region">Image region size in the buffer, from GetRegionSize.</param>
        /// <param name="buffer">N x N random-write target in BufferFormat for the image spectrum.</param>
        /// <param name="output">N x N random-write target in BufferFormat. RGB holds the bloom.</param>
        public void Convolve(CommandBuffer commandBuffer, RenderTargetIdentifier image, Vector2Int region,
            RenderTargetIdentifier buffer, RenderTargetIdentifier output, RenderTargetIdentifier kernelSpectrum, int size)
        {
            commandBuffer.SetComputeIntParam(_shader, SizeId, size);
            commandBuffer.SetComputeVectorParam(_shader, RegionId,
                new Vector4(region.x, region.y, 1f / region.x, 1f / region.y));
            commandBuffer.SetComputeTextureParam(_shader, _packKernel, ImageId, image);
            commandBuffer.SetComputeTextureParam(_shader, _packKernel, BufferId, buffer);
            DispatchFor(commandBuffer, _packKernel, size);
            DispatchFFT(commandBuffer, buffer, size, false, false);
            DispatchFFT(commandBuffer, buffer, size, true, false);

            commandBuffer.SetComputeTextureParam(_shader, _multiplyKernel, SpectrumId, buffer);
            commandBuffer.SetComputeTextureParam(_shader, _multiplyKernel, KernelSpectrumId, kernelSpectrum);
            commandBuffer.SetComputeTextureParam(_shader, _multiplyKernel, BufferId, output);
            DispatchFor(commandBuffer, _multiplyKernel, size);
            DispatchFFT(commandBuffer, output, size, true, true);
            DispatchFFT(commandBuffer, output, size, false, true);
        }

        private void DispatchFFT(CommandBuffer commandBuffer, RenderTargetIdentifier target, int size, bool vertical,
            bool inverse)
        {
            int kernel = size switch
            {
                256 => _fft256Kernel,
                512 => _fft512Kernel,
                1024 => _fft1024Kernel,
                _ => throw new System.ArgumentOutOfRangeException(nameof(size), size, "Use 256, 512 or 1024.")
            };
            commandBuffer.SetComputeIntParam(_shader, VerticalId, vertical ? 1 : 0);
            commandBuffer.SetComputeIntParam(_shader, InverseId, inverse ? 1 : 0);
            commandBuffer.SetComputeTextureParam(_shader, kernel, BufferId, target);
            // One thread group transforms one row or one column.
            commandBuffer.DispatchCompute(_shader, kernel, size, 1, 1);
        }

        private void DispatchFor(CommandBuffer commandBuffer, int kernel, int size)
        {
            int groups = (size + GroupSize - 1) / GroupSize;
            commandBuffer.DispatchCompute(_shader, kernel, groups, groups, 1);
        }
    }
}
