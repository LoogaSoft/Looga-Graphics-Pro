using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LoogaSoft.PostProcessing
{
    /// <summary>Bloom values that the compute kernels use for one frame.</summary>
    public readonly struct LoogaBloomSettings
    {
        public readonly float Threshold;
        public readonly float Knee;
        public readonly float Scatter;

        /// <param name="threshold">Brightness where the bloom starts, in source units. 0 = no threshold.</param>
        /// <param name="knee">Soft transition width as a part of the threshold.</param>
        /// <param name="scatter">Blend toward the wider levels during the upsample.</param>
        public LoogaBloomSettings(float threshold, float knee, float scatter)
        {
            Threshold = threshold;
            Knee = knee;
            Scatter = scatter;
        }
    }

    /// <summary>
    /// Dispatches the bloom pyramid kernels of LoogaBloom.compute.
    /// Level 0 has half the source size. Each level has half the size of the previous level, rounded up.
    /// The result is in the first upsample target, or in level 0 when the pyramid has one level.
    /// </summary>
    public sealed class LoogaBloomKernels
    {
        public const int MaxLevels = 8;
        // The smallest level must keep at least this many texels on its short side.
        private const int MinLevelSize = 2;
        private const int GroupSize = 8;

        private static readonly int SourceId = Shader.PropertyToID("_Source");
        private static readonly int HigherId = Shader.PropertyToID("_Higher");
        private static readonly int LowerId = Shader.PropertyToID("_Lower");
        private static readonly int DestinationId = Shader.PropertyToID("_Destination");
        private static readonly int SourceSizeId = Shader.PropertyToID("_SourceSize");
        private static readonly int DestinationSizeId = Shader.PropertyToID("_DestinationSize");
        private static readonly int LowerSizeId = Shader.PropertyToID("_LowerSize");
        private static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
        private static readonly int ScatterId = Shader.PropertyToID("_Scatter");

        private readonly ComputeShader _shader;
        private readonly int _prefilterKernel;
        private readonly int _downsampleKernel;
        private readonly int _upsampleKernel;

        public LoogaBloomKernels(ComputeShader shader)
        {
            _shader = shader;
            _prefilterKernel = shader.FindKernel("Prefilter");
            _downsampleKernel = shader.FindKernel("Downsample");
            _upsampleKernel = shader.FindKernel("Upsample");
        }

        public ComputeShader Compute => _shader;

        /// <summary>Returns the number of pyramid levels for a source size.</summary>
        public static int GetLevelCount(int width, int height, int maxLevels)
        {
            int levels = 0;
            Vector2Int size = new Vector2Int(width, height);
            while (levels < Mathf.Min(maxLevels, MaxLevels))
            {
                size = Half(size);
                if (Mathf.Min(size.x, size.y) < MinLevelSize) break;
                levels++;
            }
            return levels;
        }

        /// <summary>Returns the size of a pyramid level. Level 0 has half the source size.</summary>
        public static Vector2Int GetLevelSize(int width, int height, int level)
        {
            Vector2Int size = new Vector2Int(width, height);
            for (int index = 0; index <= level; index++)
            {
                size = Half(size);
            }
            return size;
        }

        /// <summary>Records the prefilter, downsample and upsample dispatches.</summary>
        /// <param name="down">Level targets. The count must be at least the level count.</param>
        /// <param name="up">Upsample targets for levels 0 to levelCount - 2.</param>
        public void Dispatch(CommandBuffer commandBuffer, RenderTargetIdentifier source, Vector2Int sourceSize,
            IReadOnlyList<RenderTargetIdentifier> down, IReadOnlyList<RenderTargetIdentifier> up, int levelCount,
            in LoogaBloomSettings settings)
        {
            DispatchDownsample(commandBuffer, source, sourceSize, down, levelCount, settings);
            DispatchUpsample(commandBuffer, sourceSize, down, up, levelCount, settings.Scatter);
        }

        /// <summary>Records the prefilter into level 0 and the downsample into levels 1 to levelCount - 1.</summary>
        public void DispatchDownsample(CommandBuffer commandBuffer, RenderTargetIdentifier source, Vector2Int sourceSize,
            IReadOnlyList<RenderTargetIdentifier> down, int levelCount, in LoogaBloomSettings settings)
        {
            commandBuffer.SetComputeVectorParam(_shader, ThresholdId, ThresholdVector(settings));

            Vector2Int level0 = GetLevelSize(sourceSize.x, sourceSize.y, 0);
            SetSizes(commandBuffer, sourceSize, level0);
            commandBuffer.SetComputeTextureParam(_shader, _prefilterKernel, SourceId, source);
            commandBuffer.SetComputeTextureParam(_shader, _prefilterKernel, DestinationId, down[0]);
            DispatchFor(commandBuffer, _prefilterKernel, level0);

            for (int level = 1; level < levelCount; level++)
            {
                Vector2Int higher = GetLevelSize(sourceSize.x, sourceSize.y, level - 1);
                Vector2Int size = GetLevelSize(sourceSize.x, sourceSize.y, level);
                SetSizes(commandBuffer, higher, size);
                commandBuffer.SetComputeTextureParam(_shader, _downsampleKernel, SourceId, down[level - 1]);
                commandBuffer.SetComputeTextureParam(_shader, _downsampleKernel, DestinationId, down[level]);
                DispatchFor(commandBuffer, _downsampleKernel, size);
            }
        }

        /// <summary>Records the upsample from the smallest level to level 0 into the upsample targets.</summary>
        public void DispatchUpsample(CommandBuffer commandBuffer, Vector2Int sourceSize,
            IReadOnlyList<RenderTargetIdentifier> down, IReadOnlyList<RenderTargetIdentifier> up, int levelCount,
            float scatter)
        {
            commandBuffer.SetComputeFloatParam(_shader, ScatterId, scatter);
            for (int level = levelCount - 2; level >= 0; level--)
            {
                Vector2Int size = GetLevelSize(sourceSize.x, sourceSize.y, level);
                Vector2Int lower = GetLevelSize(sourceSize.x, sourceSize.y, level + 1);
                RenderTargetIdentifier lowerTarget = level == levelCount - 2 ? down[level + 1] : up[level + 1];
                commandBuffer.SetComputeVectorParam(_shader, DestinationSizeId, SizeVector(size));
                commandBuffer.SetComputeVectorParam(_shader, LowerSizeId, SizeVector(lower));
                commandBuffer.SetComputeTextureParam(_shader, _upsampleKernel, HigherId, down[level]);
                commandBuffer.SetComputeTextureParam(_shader, _upsampleKernel, LowerId, lowerTarget);
                commandBuffer.SetComputeTextureParam(_shader, _upsampleKernel, DestinationId, up[level]);
                DispatchFor(commandBuffer, _upsampleKernel, size);
            }
        }

        /// <summary>
        /// Returns the number of levels to downsample so that the last level is the smallest level that is not
        /// smaller than the target size. The FFT bloom samples that level.
        /// </summary>
        public static int GetLevelCountForTarget(int width, int height, Vector2Int target)
        {
            int levels = 1;
            while (levels < MaxLevels)
            {
                Vector2Int next = GetLevelSize(width, height, levels);
                if (next.x < target.x || next.y < target.y) break;
                levels++;
            }
            return levels;
        }

        /// <summary>
        /// Returns the threshold curve constants: threshold, threshold - knee, 2 * knee and 0.25 / knee.
        /// The prefilter and the composite use the same curve.
        /// </summary>
        public static Vector4 ThresholdVector(in LoogaBloomSettings settings)
        {
            float knee = settings.Threshold * settings.Knee;
            return new Vector4(settings.Threshold, settings.Threshold - knee, 2f * knee, knee > 0f ? 0.25f / knee : 0f);
        }

        private void SetSizes(CommandBuffer commandBuffer, Vector2Int source, Vector2Int destination)
        {
            commandBuffer.SetComputeVectorParam(_shader, SourceSizeId, SizeVector(source));
            commandBuffer.SetComputeVectorParam(_shader, DestinationSizeId, SizeVector(destination));
        }

        private void DispatchFor(CommandBuffer commandBuffer, int kernel, Vector2Int size)
        {
            commandBuffer.DispatchCompute(_shader, kernel, (size.x + GroupSize - 1) / GroupSize,
                (size.y + GroupSize - 1) / GroupSize, 1);
        }

        private static Vector2Int Half(Vector2Int size)
        {
            return new Vector2Int(Mathf.Max(1, (size.x + 1) / 2), Mathf.Max(1, (size.y + 1) / 2));
        }

        private static Vector4 SizeVector(Vector2Int size)
        {
            return new Vector4(size.x, size.y, 1f / size.x, 1f / size.y);
        }
    }
}
