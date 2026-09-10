using UnityEngine;

namespace LoogaSoft.Lighting
{
    public enum LoogaLightAttenuationMode
    {
        [InspectorName("URP Default")]
        UrpDefault = 0,
        Physical = 1,
        [InspectorName("Soft Physical")]
        SoftPhysical = 2,
        Linear = 3,
        Quadratic = 4,
        Power = 5,
        [InspectorName("Custom Curve")]
        CustomCurve = 6
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    [AddComponentMenu("LoogaSoft/Lighting/Looga Light Attenuation")]
    public sealed class LoogaLightAttenuation : MonoBehaviour
    {
        private const int CurveSampleCount = 8;

        [SerializeField] private LoogaLightAttenuationMode _mode =
            LoogaLightAttenuationMode.UrpDefault;
        [SerializeField, Range(0f, 0.99f)] private float _rangeFadeStart = 0.8f;
        [SerializeField, Min(0.001f)] private float _sourceRadius = 0.05f;
        [SerializeField, Range(0.1f, 8f)] private float _falloffExponent = 2f;
        [SerializeField] private AnimationCurve _customCurve = CreateDefaultCurve();

        public LoogaLightAttenuationMode Mode => _mode;
        public float RangeFadeStart => _rangeFadeStart;
        public float SourceRadius => _sourceRadius;
        public float FalloffExponent => _falloffExponent;
        public AnimationCurve CustomCurve => _customCurve;

        /// <summary>
        /// Sets the attenuation mode for this light.
        /// </summary>
        public void SetMode(LoogaLightAttenuationMode mode)
        {
            _mode = mode;
        }

        /// <summary>
        /// Sets the parameters used by the physical and power attenuation modes.
        /// </summary>
        /// <param name="rangeFadeStart">Normalized distance where the soft range fade starts.</param>
        /// <param name="sourceRadius">Minimum physical distance used by inverse-square attenuation.</param>
        /// <param name="falloffExponent">Exponent used by the power attenuation mode.</param>
        public void SetParameters(
            float rangeFadeStart,
            float sourceRadius,
            float falloffExponent)
        {
            _rangeFadeStart = Mathf.Clamp(rangeFadeStart, 0f, 0.99f);
            _sourceRadius = Mathf.Max(0.001f, sourceRadius);
            _falloffExponent = Mathf.Clamp(falloffExponent, 0.1f, 8f);
        }

        /// <summary>
        /// Sets the normalized distance curve for the custom curve mode.
        /// </summary>
        /// <param name="curve">Curve from the light source at zero to the range at one.</param>
        public void SetCustomCurve(AnimationCurve curve)
        {
            if (curve == null)
            {
                _customCurve = CreateDefaultCurve();
                return;
            }

            _customCurve = new AnimationCurve(curve.keys)
            {
                preWrapMode = curve.preWrapMode,
                postWrapMode = curve.postWrapMode
            };
        }

        /// <summary>
        /// Evaluates this component's distance attenuation on the CPU.
        /// </summary>
        /// <param name="distance">Distance from the light, in meters.</param>
        /// <param name="range">Light range, in meters.</param>
        /// <returns>The distance attenuation before spot, shadow, and cookie terms.</returns>
        public float EvaluateDistance(float distance, float range)
        {
            return EvaluateDistance(
                _mode,
                distance,
                range,
                _rangeFadeStart,
                _sourceRadius,
                _falloffExponent,
                _customCurve);
        }

        internal void GetShaderData(
            out Vector4 parameters,
            out Vector4 curveSamplesA,
            out Vector4 curveSamplesB)
        {
            BuildShaderData(
                _mode,
                _rangeFadeStart,
                _sourceRadius,
                _falloffExponent,
                _customCurve,
                out parameters,
                out curveSamplesA,
                out curveSamplesB);
        }

        internal static void BuildShaderData(
            LoogaLightAttenuationMode mode,
            float rangeFadeStart,
            float sourceRadius,
            float falloffExponent,
            AnimationCurve customCurve,
            out Vector4 parameters,
            out Vector4 curveSamplesA,
            out Vector4 curveSamplesB)
        {
            parameters = new Vector4(
                (float)mode,
                Mathf.Clamp(rangeFadeStart, 0f, 0.99f),
                Mathf.Clamp(falloffExponent, 0.1f, 8f),
                Mathf.Max(0.001f, sourceRadius));

            if (mode != LoogaLightAttenuationMode.CustomCurve)
            {
                curveSamplesA = Vector4.zero;
                curveSamplesB = Vector4.zero;
                return;
            }

            AnimationCurve curve = customCurve ?? CreateDefaultCurve();
            curveSamplesA = new Vector4(
                SampleCurve(curve, 0),
                SampleCurve(curve, 1),
                SampleCurve(curve, 2),
                SampleCurve(curve, 3));
            curveSamplesB = new Vector4(
                SampleCurve(curve, 4),
                SampleCurve(curve, 5),
                SampleCurve(curve, 6),
                SampleCurve(curve, 7));
        }

        internal static float EvaluateDistance(
            LoogaLightAttenuationMode mode,
            float distance,
            float range,
            float rangeFadeStart,
            float sourceRadius,
            float falloffExponent,
            AnimationCurve customCurve)
        {
            float safeRange = Mathf.Max(0.001f, range);
            float safeDistance = Mathf.Max(0f, distance);
            float normalizedDistance = Mathf.Clamp01(safeDistance / safeRange);

            if (normalizedDistance >= 1f)
                return 0f;

            float inverseDistanceSquared = 1f / Mathf.Max(
                safeDistance * safeDistance,
                sourceRadius * sourceRadius);

            switch (mode)
            {
                case LoogaLightAttenuationMode.Physical:
                    return inverseDistanceSquared;
                case LoogaLightAttenuationMode.SoftPhysical:
                    return inverseDistanceSquared * RangeFade(
                        normalizedDistance,
                        rangeFadeStart);
                case LoogaLightAttenuationMode.Linear:
                    return 1f - normalizedDistance;
                case LoogaLightAttenuationMode.Quadratic:
                {
                    float remainingDistance = 1f - normalizedDistance;
                    return remainingDistance * remainingDistance;
                }
                case LoogaLightAttenuationMode.Power:
                    return Mathf.Pow(
                        1f - normalizedDistance,
                        Mathf.Clamp(falloffExponent, 0.1f, 8f));
                case LoogaLightAttenuationMode.CustomCurve:
                    return Mathf.Max(
                        0f,
                        (customCurve ?? CreateDefaultCurve()).Evaluate(normalizedDistance));
                default:
                {
                    float factor = normalizedDistance * normalizedDistance;
                    float smoothFactor = Mathf.Clamp01(1f - factor * factor);
                    return 1f / Mathf.Max(safeDistance * safeDistance, 0.00001f) *
                           smoothFactor * smoothFactor;
                }
            }
        }

        private static float RangeFade(float normalizedDistance, float rangeFadeStart)
        {
            float start = Mathf.Clamp(rangeFadeStart, 0f, 0.99f);
            float t = Mathf.Clamp01((normalizedDistance - start) / (1f - start));
            float smoothT = t * t * (3f - 2f * t);
            return 1f - smoothT;
        }

        private static float SampleCurve(AnimationCurve curve, int sampleIndex)
        {
            float time = sampleIndex / (CurveSampleCount - 1f);
            return Mathf.Max(0f, curve.Evaluate(time));
        }

        private static AnimationCurve CreateDefaultCurve()
        {
            return AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        }

        private void OnValidate()
        {
            _rangeFadeStart = Mathf.Clamp(_rangeFadeStart, 0f, 0.99f);
            _sourceRadius = Mathf.Max(0.001f, _sourceRadius);
            _falloffExponent = Mathf.Clamp(_falloffExponent, 0.1f, 8f);
            _customCurve ??= CreateDefaultCurve();
        }
    }
}
