using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LoogaSoft.Tonemapper.Runtime
{
    [Serializable]
    public sealed class LoogaTonemapModeParameter : VolumeParameter<LoogaTonemapMode>
    {
        public LoogaTonemapModeParameter(LoogaTonemapMode value, bool overrideState = false) : base(value, overrideState) { }
    }

    public enum LoogaTonemapMode
    {
        AgX,
        [InspectorName("Khronos PBR Neutral")]
        KhronosPBRNeutral,
        [InspectorName("Sigmoid (Log-Logistic)")]
        Sigmoid,
        [InspectorName("Reinhard Extended")]
        ReinhardExtended
    }

    [Serializable, VolumeComponentMenu("LoogaSoft/Looga Tonemapper")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class LoogaTonemapper : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("The curve applied to the final HDR buffer. All modes share the same 18% middle-gray calibration so they can be compared without readjusting exposure.")]
        public LoogaTonemapModeParameter tonemapMode = new LoogaTonemapModeParameter(LoogaTonemapMode.KhronosPBRNeutral);

        [Header("Exposure")]
        [Tooltip("Exposure in stops applied before tonemapping.")]
        public FloatParameter preExposure = new FloatParameter(0f);

        [Tooltip("Exposure in stops applied after tonemapping and color grading.")]
        public FloatParameter postExposure = new FloatParameter(0f);

        [Header("Color Grading")]
        [Tooltip("Sets the darkest point of the image. Useful for lifting crushed blacks.")]
        public ClampedFloatParameter blackPoint = new ClampedFloatParameter(0.0f, -0.1f, 0.5f);
        
        [Tooltip("Sets the brightest point of the image. Low values crush highlights to white.")]
        public ClampedFloatParameter whitePoint = new ClampedFloatParameter(1.0f, 0.5f, 2.0f);
        
        [Tooltip("Global contrast applied after the tonemap curve.")]
        public ClampedFloatParameter contrast = new ClampedFloatParameter(1.0f, 0.5f, 2.0f);
        
        [Tooltip("Global saturation applied after the tonemap curve.")]
        public ClampedFloatParameter saturation = new ClampedFloatParameter(1.0f, 0.0f, 2.0f);

        [Header("Algorithm Specific Tuning")]
        [InspectorName("Contrast")]
        [Tooltip("Controls the steepness of the Sigmoid S-curve while preserving 18% middle gray.")]
        public ClampedFloatParameter sigmoidCurve = new ClampedFloatParameter(1.5f, 0.5f, 3.0f);

        [InspectorName("White Point")]
        [Tooltip("The scene-linear white point used by Reinhard Extended. Middle gray remains exposure-matched as this changes.")]
        public MinFloatParameter reinhardLimit = new MinFloatParameter(1.5f, 0.1f);

        public bool IsActive() => active;
        public bool IsTileCompatible() => false;
    }
}
