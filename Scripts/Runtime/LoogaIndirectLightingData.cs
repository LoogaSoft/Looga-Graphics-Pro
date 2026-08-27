using System;
using UnityEngine;

namespace LoogaSoft.Lighting
{
    [CreateAssetMenu(fileName = "Looga Indirect Lighting Data", menuName = "LoogaSoft/Graphics Pro/Lighting/Indirect Lighting Data")]
    public sealed class LoogaIndirectLightingData : ScriptableObject
    {
        [Serializable]
        public struct ReflectionProbeRecord
        {
            public Vector3 center;
            public Vector3 extents;
            public Vector3 capturePosition;
            public Quaternion rotation;
            public float blendDistance;
            public float intensity;
            public int slice;
            public bool boxProjection;
        }

        [Serializable]
        public struct AuxiliaryLightmapSource
        {
            [Tooltip("Additional baked radiance for lobe 0. RGB stores radiance and alpha stores lobe energy.")]
            public Texture2D lobe0Radiance;
            [Tooltip("Additional baked radiance for lobe 1. RGB stores radiance and alpha stores lobe energy.")]
            public Texture2D lobe1Radiance;
            [Tooltip("RG stores octahedral lobe 0 direction; BA stores octahedral lobe 1 direction.")]
            public Texture2D directions;
        }

        [Header("Generated Model-Aware Reflections")]
        public CubemapArray ggxReflectionProbes;
        public CubemapArray beckmannReflectionProbes;
        public CubemapArray phongReflectionProbes;
        public Texture2D ggxBrdfLut;
        public Texture2D beckmannBrdfLut;
        public Texture2D phongBrdfLut;
        public ReflectionProbeRecord[] reflectionProbes = Array.Empty<ReflectionProbeRecord>();

        [Header("Auxiliary Multi-Lobe Lightmap Sources")]
        [Tooltip("One entry per Unity lightmap index. This is an optional importer contract for Bakery or another external baker.")]
        public AuxiliaryLightmapSource[] auxiliarySources = Array.Empty<AuxiliaryLightmapSource>();
        [Header("Generated Auxiliary Lightmap Arrays")]
        public Texture2DArray auxiliaryLobe0Array;
        public Texture2DArray auxiliaryLobe1Array;
        public Texture2DArray auxiliaryDirectionArray;

        [Header("Generated Radiance Probe Volume")]
        public Bounds radianceProbeBounds = new Bounds(Vector3.zero, new Vector3(20f, 10f, 20f));
        public Vector3Int radianceProbeResolution = new Vector3Int(4, 2, 4);
        public Texture3D radianceLobe0;
        public Texture3D radianceDirection0;
        public Texture3D radianceLobe1;
        public Texture3D radianceDirection1;

        public bool HasReflectionData => reflectionProbes != null && reflectionProbes.Length > 0 &&
                                         ggxReflectionProbes != null && beckmannReflectionProbes != null && phongReflectionProbes != null &&
                                         ggxBrdfLut != null && beckmannBrdfLut != null && phongBrdfLut != null;

        public bool HasAuxiliaryLightmaps => auxiliaryLobe0Array != null && auxiliaryLobe1Array != null && auxiliaryDirectionArray != null;

        public bool HasRadianceVolume => radianceLobe0 != null && radianceDirection0 != null && radianceLobe1 != null && radianceDirection1 != null;
    }
}
