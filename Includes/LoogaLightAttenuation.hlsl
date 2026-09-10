#ifndef LOOGA_LIGHT_ATTENUATION_INCLUDED
#define LOOGA_LIGHT_ATTENUATION_INCLUDED

#define LOOGA_ATTENUATION_URP_DEFAULT 0
#define LOOGA_ATTENUATION_PHYSICAL 1
#define LOOGA_ATTENUATION_SOFT_PHYSICAL 2
#define LOOGA_ATTENUATION_LINEAR 3
#define LOOGA_ATTENUATION_QUADRATIC 4
#define LOOGA_ATTENUATION_POWER 5
#define LOOGA_ATTENUATION_CUSTOM_CURVE 6

int _LoogaAdditionalLightAttenuationCount;
float4 _LoogaAdditionalLightAttenuationPositionRange[MAX_VISIBLE_LIGHTS];
float4 _LoogaAdditionalLightAttenuationParameters[MAX_VISIBLE_LIGHTS];
float4 _LoogaAdditionalLightAttenuationCurveA[MAX_VISIBLE_LIGHTS];
float4 _LoogaAdditionalLightAttenuationCurveB[MAX_VISIBLE_LIGHTS];

float LoogaUrpDistanceAttenuation(float distanceSquared, float range)
{
    float normalizedDistanceSquared = distanceSquared / max(range * range, 0.000001);
    float smoothFactor = saturate(1.0 - normalizedDistanceSquared * normalizedDistanceSquared);
    return rcp(max(distanceSquared, HALF_MIN)) * smoothFactor * smoothFactor;
}

float LoogaRangeFade(float normalizedDistance, float fadeStart)
{
    float fade = saturate((normalizedDistance - fadeStart) / max(1.0 - fadeStart, 0.0001));
    fade = fade * fade * (3.0 - 2.0 * fade);
    return 1.0 - fade;
}

float LoogaCurveSampleValue(float4 samplesA, float4 samplesB, int index)
{
    if (index < 4)
        return samplesA[index];

    return samplesB[index - 4];
}

float LoogaSampleAttenuationCurve(int lightIndex, float normalizedDistance)
{
    float samplePosition = saturate(normalizedDistance) * 7.0;
    int firstSample = min((int)floor(samplePosition), 6);
    int secondSample = firstSample + 1;
    float blend = frac(samplePosition);
    float4 samplesA = _LoogaAdditionalLightAttenuationCurveA[lightIndex];
    float4 samplesB = _LoogaAdditionalLightAttenuationCurveB[lightIndex];
    return lerp(
        LoogaCurveSampleValue(samplesA, samplesB, firstSample),
        LoogaCurveSampleValue(samplesA, samplesB, secondSample),
        blend);
}

int GetLoogaAdditionalLightDataIndex(uint loopIndex)
{
    #if USE_CLUSTER_LIGHT_LOOP
        return (int)loopIndex;
    #else
        return GetPerObjectLightIndex(loopIndex);
    #endif
}

float ApplyLoogaLightAttenuation(
    float urpAttenuation,
    int lightIndex,
    float3 positionWS)
{
    if (lightIndex < 0 || lightIndex >= _LoogaAdditionalLightAttenuationCount)
        return urpAttenuation;

    float4 parameters = _LoogaAdditionalLightAttenuationParameters[lightIndex];
    int mode = (int)round(parameters.x);
    if (mode == LOOGA_ATTENUATION_URP_DEFAULT)
        return urpAttenuation;

    float4 positionRange = _LoogaAdditionalLightAttenuationPositionRange[lightIndex];
    float range = positionRange.w;
    if (range <= 0.0)
        return urpAttenuation;

    float3 lightVector = positionRange.xyz - positionWS;
    float distanceSquared = max(dot(lightVector, lightVector), HALF_MIN);
    float distance = sqrt(distanceSquared);
    float normalizedDistance = saturate(distance / range);
    if (normalizedDistance >= 1.0)
        return 0.0;

    float urpDistanceAttenuation = LoogaUrpDistanceAttenuation(distanceSquared, range);
    float angularAttenuation = urpDistanceAttenuation > 0.000001
        ? saturate(urpAttenuation / urpDistanceAttenuation)
        : 0.0;

    float attenuation;
    float remainingDistance = 1.0 - normalizedDistance;
    float sourceRadiusSquared = max(parameters.w * parameters.w, 0.000001);
    float inverseDistanceSquared = rcp(max(distanceSquared, sourceRadiusSquared));

    if (mode == LOOGA_ATTENUATION_PHYSICAL)
    {
        attenuation = inverseDistanceSquared;
    }
    else if (mode == LOOGA_ATTENUATION_SOFT_PHYSICAL)
    {
        attenuation = inverseDistanceSquared * LoogaRangeFade(
            normalizedDistance,
            parameters.y);
    }
    else if (mode == LOOGA_ATTENUATION_LINEAR)
    {
        attenuation = remainingDistance;
    }
    else if (mode == LOOGA_ATTENUATION_QUADRATIC)
    {
        attenuation = remainingDistance * remainingDistance;
    }
    else if (mode == LOOGA_ATTENUATION_POWER)
    {
        attenuation = pow(remainingDistance, parameters.z);
    }
    else if (mode == LOOGA_ATTENUATION_CUSTOM_CURVE)
    {
        attenuation = max(
            0.0,
            LoogaSampleAttenuationCurve(lightIndex, normalizedDistance));
    }
    else
    {
        return urpAttenuation;
    }

    return attenuation * angularAttenuation;
}

Light GetLoogaAdditionalLight(
    uint loopIndex,
    InputData inputData,
    half4 shadowMask,
    AmbientOcclusionFactor aoFactor)
{
    Light light = GetAdditionalLight(loopIndex, inputData, shadowMask, aoFactor);
    int lightIndex = GetLoogaAdditionalLightDataIndex(loopIndex);
    light.distanceAttenuation = ApplyLoogaLightAttenuation(
        light.distanceAttenuation,
        lightIndex,
        inputData.positionWS);
    return light;
}

#endif
