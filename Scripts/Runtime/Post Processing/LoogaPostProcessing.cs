using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LoogaSoft.PostProcessing
{
    [Serializable]
    public sealed class LoogaTonemapModeParameter : VolumeParameter<LoogaTonemapMode>
    {
        public LoogaTonemapModeParameter(LoogaTonemapMode value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class LoogaBloomModeParameter : VolumeParameter<LoogaBloomMode>
    {
        public LoogaBloomModeParameter(LoogaBloomMode value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class LoogaBloomFFTSizeParameter : VolumeParameter<LoogaBloomFFTSize>
    {
        public LoogaBloomFFTSizeParameter(LoogaBloomFFTSize value, bool overrideState = false) : base(value, overrideState) { }
    }

    public enum LoogaBloomMode
    {
        // Mip pyramid with a 13-tap downsample and a 9-tap tent upsample.
        Pyramid = 0,
        // Convolution of the thresholded image with a kernel texture, through a fast Fourier transform.
        [InspectorName("FFT Convolution")]
        FFTConvolution = 1
    }

    // The values are the FFT buffer sizes in texels.
    public enum LoogaBloomFFTSize
    {
        [InspectorName("256")]
        Low = 256,
        [InspectorName("512")]
        Medium = 512,
        [InspectorName("1024")]
        High = 1024
    }

    public enum LoogaTonemapMode
    {
        // Preserve existing serialized curve IDs when adding the inactive mode.
        None = -1,
        AgX = 0,
        [InspectorName("Khronos PBR Neutral")]
        KhronosPBRNeutral = 1,
        [InspectorName("Sigmoid (Log-Logistic)")]
        Sigmoid = 2,
        [InspectorName("Reinhard Extended")]
        ReinhardExtended = 3
    }

    /// <summary>
    /// Looga post-processing settings for a Volume profile: bloom, lens dirt and tonemapping.
    /// The Looga Post Processing renderer feature renders the effects that these settings enable.
    /// </summary>
    [Serializable, VolumeComponentMenu("LoogaSoft/Looga Post Processing")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class LoogaPostProcessing : VolumeComponent, IPostProcessComponent
    {
        // The tonemapping parameters keep the names and defaults of the former Looga Tonemapper override.
        [Tooltip("None leaves the image unchanged. Override this parameter and select a curve to enable tonemapping. " +
                 "All curves share the same 18% middle-gray calibration.")]
        public LoogaTonemapModeParameter tonemapMode = new LoogaTonemapModeParameter(LoogaTonemapMode.None);

        [Tooltip("Exposure in stops applied before tonemapping.")]
        public FloatParameter preExposure = new FloatParameter(0f);

        [Tooltip("Exposure in stops applied after tonemapping and color grading.")]
        public FloatParameter postExposure = new FloatParameter(0f);

        [Tooltip("Sets the darkest point of the image. Negative values lift the shadows. Positive values crush them.")]
        public ClampedFloatParameter blackPoint = new ClampedFloatParameter(0.0f, -0.1f, 0.5f);

        [Tooltip("Sets the brightest point of the image. Low values crush highlights to white.")]
        public ClampedFloatParameter whitePoint = new ClampedFloatParameter(1.0f, 0.5f, 2.0f);

        [Tooltip("Global contrast applied after the tonemap curve.")]
        public ClampedFloatParameter contrast = new ClampedFloatParameter(1.0f, 0.5f, 2.0f);

        [Tooltip("Global saturation applied after the tonemap curve.")]
        public ClampedFloatParameter saturation = new ClampedFloatParameter(1.0f, 0.0f, 2.0f);

        [InspectorName("Contrast")]
        [Tooltip("Controls the steepness of the Sigmoid S-curve while preserving 18% middle gray.")]
        public ClampedFloatParameter sigmoidCurve = new ClampedFloatParameter(1.5f, 0.5f, 3.0f);

        [InspectorName("White Point")]
        [Tooltip("The scene-linear white point used by Reinhard Extended. Middle gray remains exposure-matched as this changes.")]
        public MinFloatParameter reinhardLimit = new MinFloatParameter(1.5f, 0.1f);

        [InspectorName("Intensity")]
        [Tooltip("Part of the light that the lens scatters into the bloom. 0 = no bloom. " +
                 "The bloom replaces this part of the image, so the total light stays the same.")]
        public ClampedFloatParameter bloomIntensity = new ClampedFloatParameter(0f, 0f, 1f);

        [InspectorName("Threshold")]
        [Tooltip("Brightness where the bloom starts, measured after pre-exposure. 0 = all light blooms, as in a physical lens.")]
        public MinFloatParameter bloomThreshold = new MinFloatParameter(0f, 0f);

        [InspectorName("Threshold Knee")]
        [Tooltip("Width of the soft transition below the threshold, as a part of the threshold. 0 = hard cut.")]
        public ClampedFloatParameter bloomKnee = new ClampedFloatParameter(0.5f, 0f, 1f);

        [InspectorName("Scatter")]
        [Tooltip("Blend toward the wider pyramid levels. 0 = tight bloom around the source, 1 = wide, soft bloom.")]
        public ClampedFloatParameter bloomScatter = new ClampedFloatParameter(0.7f, 0f, 1f);

        [InspectorName("Tint")]
        [Tooltip("Color multiplier for the bloom.")]
        public ColorParameter bloomTint = new ColorParameter(Color.white, false, false, true);

        [InspectorName("Levels")]
        [Tooltip("Maximum number of pyramid levels. More levels give a wider bloom. Small screens can use fewer levels.")]
        public ClampedIntParameter bloomLevels = new ClampedIntParameter(7, 2, LoogaBloomKernels.MaxLevels);

        [InspectorName("Mode")]
        [Tooltip("Pyramid is fast and suits real-time use. FFT Convolution convolves the image with a kernel texture " +
                 "that represents the lens. It costs more and suits high-end settings and cinematics.")]
        public LoogaBloomModeParameter bloomMode = new LoogaBloomModeParameter(LoogaBloomMode.Pyramid);

        [InspectorName("FFT Size")]
        [Tooltip("Size of the FFT buffers in texels. Larger sizes give more detail and cost more.")]
        public LoogaBloomFFTSizeParameter bloomFFTSize = new LoogaBloomFFTSizeParameter(LoogaBloomFFTSize.Medium);

        [InspectorName("Kernel Size")]
        [Tooltip("Kernel diameter as a part of the largest screen side. Larger kernels spread the light farther, " +
                 "and the image then uses less of the FFT buffer.")]
        public ClampedFloatParameter bloomKernelSize = new ClampedFloatParameter(1f, 0.1f, 2f);

        [InspectorName("Kernel Texture")]
        [Tooltip("Point spread function of the lens, centered in the texture. Only the scattered light belongs in the " +
                 "kernel: the light that does not scatter stays in the image. Each channel is normalized, so the " +
                 "bloom keeps the light energy. None uses a physically based glare kernel.")]
        public TextureParameter bloomKernelTexture = new TextureParameter(null);

        [InspectorName("Dirt Texture")]
        [Tooltip("Lens dirt texture. It is scaled to cover the screen and keeps its aspect ratio. The excess is cropped.")]
        public TextureParameter lensDirtTexture = new TextureParameter(null);

        [InspectorName("Dirt Intensity")]
        [Tooltip("Lens dirt brightness. The dirt multiplies the bloom, so it shows only where the bloom is bright.")]
        public MinFloatParameter lensDirtIntensity = new MinFloatParameter(0f, 0f);

        // Evaluate the blended values, not overrideState: the Volume stack resolves unchecked parameters and volumes
        // with zero influence back to the defaults.
        public bool IsActive() => IsTonemapActive() || IsBloomActive();

        /// <summary>Returns true when the blended settings select a tonemap curve.</summary>
        public bool IsTonemapActive() => active && tonemapMode.value != LoogaTonemapMode.None;

        /// <summary>Returns true when the blended settings request bloom.</summary>
        public bool IsBloomActive() => active && bloomIntensity.value > 0f;

        /// <summary>Returns true when the blended settings select the FFT convolution bloom.</summary>
        public bool IsFFTBloom() => bloomMode.value == LoogaBloomMode.FFTConvolution;

        /// <summary>Linear pre-exposure multiplier. It is 1 when tonemapping is off, because then no exposure applies.</summary>
        public float PreExposureScale => IsTonemapActive() ? Mathf.Pow(2f, preExposure.value) : 1f;

        public bool IsTileCompatible() => false;
    }
}
