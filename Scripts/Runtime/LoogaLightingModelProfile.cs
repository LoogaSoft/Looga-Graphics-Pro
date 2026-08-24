using System;
using UnityEngine;

namespace LoogaSoft.Lighting
{
    public enum LoogaDiffuseModel
    {
        Lambert = 0,
        [InspectorName("Disney/Burley")]
        DisneyBurley = 1,
        Minnaert = 2,
        [InspectorName("Oren-Nayar")]
        OrenNayar = 3,
        Wrapped = 4,
        Banded = 5
    }

    public enum LoogaSpecularModel
    {
        GGX = 0,
        Beckmann = 1,
        Phong = 2
    }

    public enum LoogaSpecularOcclusionModel
    {
        Standard = 0,
        [InspectorName("Source 2 Bent Normal")]
        Source2BentNormal = 1
    }

    [Serializable]
    public sealed class LoogaLightingModelSettings
    {
        public LoogaDiffuseModel diffuseModel = LoogaDiffuseModel.DisneyBurley;
        public LoogaSpecularModel directSpecularModel = LoogaSpecularModel.GGX;
        public LoogaSpecularModel indirectSpecularModel = LoogaSpecularModel.GGX;
        public LoogaSpecularOcclusionModel specularOcclusionModel =
            LoogaSpecularOcclusionModel.Standard;

        [Min(0.0f)] public float diffuseStrength = 1.0f;
        [Min(0.0f)] public float directSpecularStrength = 1.0f;
        [Min(0.0f)] public float indirectSpecularStrength = 1.0f;

        [Range(0.0f, 2.0f)] public float directRoughnessScale = 1.0f;
        [Range(-1.0f, 1.0f)] public float directRoughnessBias;
        [Range(0.0f, 2.0f)] public float indirectRoughnessScale = 1.0f;
        [Range(-1.0f, 1.0f)] public float indirectRoughnessBias;
        [Range(1.0f, 8.0f)] public float indirectFresnelPower = 5.0f;

        [Range(0.0f, 2.0f)] public float minnaertK = 0.7f;
        [Range(0.0f, 90.0f)] public float orenNayarSigma = 30.0f;
        [Range(0.0f, 0.5f)] public float diffuseWrap = 0.08f;
        [Range(1.0f, 8.0f)] public float bandCount = 3.0f;
        [Range(0.001f, 0.5f)] public float bandFeather = 0.15f;
        [Range(0.0f, 1.0f)] public float bandBlend = 0.1f;

        [Range(0.0f, 1.0f)] public float secondarySpecularWeight;
        [Range(0.0f, 1.0f)] public float secondaryRoughnessSpread = 0.12f;
        [Range(0.0f, 1.0f)] public float highlightShapeStrength;
        [Range(0.0f, 1.0f)] public float highlightShapeFloor = 0.72f;
        [Range(0.0f, 1.0f)] public float highlightShapeStart = 0.015f;
        [Range(0.0f, 1.0f)] public float highlightShapeEnd = 0.18f;

        [Range(0.0f, 1.0f)] public float grazingOcclusionStrength;
        [Range(0.0f, 1.0f)] public float edgeOcclusionStrength;
        [Range(0.0f, 1.0f)] public float edgeOcclusionStart = 0.2f;
        [Range(0.0f, 1.0f)] public float edgeOcclusionEnd = 0.75f;

        public LoogaLightingModelSettings Clone()
        {
            return (LoogaLightingModelSettings)MemberwiseClone();
        }

        public void Clamp()
        {
            diffuseStrength = Mathf.Max(0.0f, diffuseStrength);
            directSpecularStrength = Mathf.Max(0.0f, directSpecularStrength);
            indirectSpecularStrength = Mathf.Max(0.0f, indirectSpecularStrength);
            directRoughnessScale = Mathf.Clamp(directRoughnessScale, 0.0f, 2.0f);
            directRoughnessBias = Mathf.Clamp(directRoughnessBias, -1.0f, 1.0f);
            indirectRoughnessScale = Mathf.Clamp(indirectRoughnessScale, 0.0f, 2.0f);
            indirectRoughnessBias = Mathf.Clamp(indirectRoughnessBias, -1.0f, 1.0f);
            indirectFresnelPower = Mathf.Clamp(indirectFresnelPower, 1.0f, 8.0f);
            minnaertK = Mathf.Clamp(minnaertK, 0.0f, 2.0f);
            orenNayarSigma = Mathf.Clamp(orenNayarSigma, 0.0f, 90.0f);
            diffuseWrap = Mathf.Clamp(diffuseWrap, 0.0f, 0.5f);
            bandCount = Mathf.Clamp(bandCount, 1.0f, 8.0f);
            bandFeather = Mathf.Clamp(bandFeather, 0.001f, 0.5f);
            bandBlend = Mathf.Clamp01(bandBlend);
            secondarySpecularWeight = Mathf.Clamp01(secondarySpecularWeight);
            secondaryRoughnessSpread = Mathf.Clamp01(secondaryRoughnessSpread);
            highlightShapeStrength = Mathf.Clamp01(highlightShapeStrength);
            highlightShapeFloor = Mathf.Clamp01(highlightShapeFloor);
            highlightShapeStart = Mathf.Clamp(highlightShapeStart, 0.0f, 0.9999f);
            highlightShapeEnd = Mathf.Clamp(
                highlightShapeEnd, highlightShapeStart + 0.0001f, 1.0f);
            grazingOcclusionStrength = Mathf.Clamp01(grazingOcclusionStrength);
            edgeOcclusionStrength = Mathf.Clamp01(edgeOcclusionStrength);
            edgeOcclusionStart = Mathf.Clamp(edgeOcclusionStart, 0.0f, 0.9999f);
            edgeOcclusionEnd = Mathf.Clamp(
                edgeOcclusionEnd, edgeOcclusionStart + 0.0001f, 1.0f);
        }
    }

    [CreateAssetMenu(
        fileName = "Looga Lighting Model",
        menuName = "LoogaSoft/Lighting/Lighting Model Profile")]
    public sealed class LoogaLightingModelProfile : ScriptableObject
    {
        [TextArea(2, 5)]
        public string description =
            "A custom Looga lighting model assembled from reusable diffuse, specular, and indirect response controls.";

        public LoogaLightingModelSettings settings = new LoogaLightingModelSettings();

        public void ApplyPreset(LoogaLightingFeature.LightingModel preset)
        {
            settings = CreatePresetSettings(preset);
        }

        public static LoogaLightingModelSettings CreatePresetSettings(
            LoogaLightingFeature.LightingModel preset)
        {
            LoogaLightingModelSettings result = new LoogaLightingModelSettings();

            switch (preset)
            {
                case LoogaLightingFeature.LightingModel.Source2:
                    result.diffuseModel = LoogaDiffuseModel.Lambert;
                    result.specularOcclusionModel =
                        LoogaSpecularOcclusionModel.Source2BentNormal;
                    break;

                case LoogaLightingFeature.LightingModel.Minnaert:
                    result.diffuseModel = LoogaDiffuseModel.Minnaert;
                    result.directSpecularModel = LoogaSpecularModel.Beckmann;
                    result.indirectSpecularModel = LoogaSpecularModel.Beckmann;
                    result.minnaertK = 0.7f;
                    result.grazingOcclusionStrength = 0.35f;
                    break;

                case LoogaLightingFeature.LightingModel.Overwatch:
                    result.diffuseModel = LoogaDiffuseModel.Wrapped;
                    result.diffuseWrap = 0.08f;
                    result.secondarySpecularWeight = 0.2f;
                    result.secondaryRoughnessSpread = 0.12f;
                    result.indirectRoughnessScale = 1.05f;
                    result.indirectRoughnessBias = 0.015f;
                    break;

                case LoogaLightingFeature.LightingModel.OrenNayar:
                    result.diffuseModel = LoogaDiffuseModel.OrenNayar;
                    result.orenNayarSigma = 30.0f;
                    break;

                case LoogaLightingFeature.LightingModel.Arkane:
                    result.diffuseModel = LoogaDiffuseModel.Banded;
                    result.bandCount = 3.0f;
                    result.bandFeather = 0.15f;
                    result.bandBlend = 0.1f;
                    result.highlightShapeStrength = 1.0f;
                    result.highlightShapeFloor = 0.72f;
                    result.highlightShapeStart = 0.015f;
                    result.highlightShapeEnd = 0.18f;
                    result.indirectRoughnessScale = 1.08f;
                    result.indirectRoughnessBias = 0.02f;
                    result.indirectFresnelPower = 4.0f;
                    result.edgeOcclusionStrength = 0.1f;
                    result.edgeOcclusionStart = 0.2f;
                    result.edgeOcclusionEnd = 0.75f;
                    break;

                default:
                    result.diffuseModel = LoogaDiffuseModel.DisneyBurley;
                    break;
            }

            return result;
        }

        private void OnValidate()
        {
            if (settings == null)
                settings = new LoogaLightingModelSettings();
            settings.Clamp();
        }
    }
}
