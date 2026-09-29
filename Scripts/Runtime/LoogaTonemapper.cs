using System;
using LoogaSoft.PostProcessing;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LoogaSoft.Tonemapper.Runtime
{
    /// <summary>
    /// Former tonemapping override. It stays only so that existing profiles load and can be migrated.
    /// Use Tools > LoogaSoft > Migrate Tonemapper To Post Processing to convert profiles to LoogaPostProcessing.
    /// </summary>
    /// <remarks>
    /// The Volume framework hides obsolete components from the Add Override menu and from the Volume stack.
    /// This component has no effect on rendering.
    /// </remarks>
    [Serializable]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [Obsolete("Use LoogaPostProcessing. Tools > LoogaSoft > Migrate Tonemapper To Post Processing converts profiles.")]
    public sealed class LoogaTonemapper : VolumeComponent
    {
        public LoogaTonemapModeParameter tonemapMode = new LoogaTonemapModeParameter(LoogaTonemapMode.None);
        public FloatParameter preExposure = new FloatParameter(0f);
        public FloatParameter postExposure = new FloatParameter(0f);
        public ClampedFloatParameter blackPoint = new ClampedFloatParameter(0.0f, -0.1f, 0.5f);
        public ClampedFloatParameter whitePoint = new ClampedFloatParameter(1.0f, 0.5f, 2.0f);
        public ClampedFloatParameter contrast = new ClampedFloatParameter(1.0f, 0.5f, 2.0f);
        public ClampedFloatParameter saturation = new ClampedFloatParameter(1.0f, 0.0f, 2.0f);
        public ClampedFloatParameter sigmoidCurve = new ClampedFloatParameter(1.5f, 0.5f, 3.0f);
        public MinFloatParameter reinhardLimit = new MinFloatParameter(1.5f, 0.1f);
    }
}
