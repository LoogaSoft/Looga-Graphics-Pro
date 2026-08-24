#ifndef LOOGA_INDIRECT_LIGHTING_INCLUDED
#define LOOGA_INDIRECT_LIGHTING_INCLUDED

#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaModelParameters.hlsl"

#define LOOGA_REFLECTION_FAMILY_GGX 0
#define LOOGA_REFLECTION_FAMILY_BECKMANN 1
#define LOOGA_REFLECTION_FAMILY_PHONG 2
#define LOOGA_MAX_REFLECTION_PROBES 32

#if !defined(LOOGA_DISABLE_MODEL_REFLECTIONS)
    struct LoogaReflectionProbeGpuData
    {
        float4 centerAndBlend;
        float4 extentsAndIntensity;
        float4 capturePositionAndSlice;
        float4 axisX;
        float4 axisY;
        float4 axisZ;
        float4 options;
    };

    StructuredBuffer<LoogaReflectionProbeGpuData> _LoogaReflectionProbeData;
    TEXTURECUBE_ARRAY(_LoogaGGXReflectionArray);
    TEXTURECUBE_ARRAY(_LoogaBeckmannReflectionArray);
    TEXTURECUBE_ARRAY(_LoogaPhongReflectionArray);
    TEXTURE2D(_LoogaGGXBrdfLut);
    TEXTURE2D(_LoogaBeckmannBrdfLut);
    TEXTURE2D(_LoogaPhongBrdfLut);
    int _LoogaReflectionProbeCount;
    float _LoogaReflectionMipCount;
#endif
float _LoogaModelReflectionsEnabled;

TEXTURE2D_ARRAY(_LoogaAuxiliaryLobe0Array);
TEXTURE2D_ARRAY(_LoogaAuxiliaryLobe1Array);
TEXTURE2D_ARRAY(_LoogaAuxiliaryDirectionArray);
int _LoogaAuxiliaryLightmapCount;
float _LoogaAuxiliaryLightmapsEnabled;

TEXTURE3D(_LoogaRadianceLobe0);
TEXTURE3D(_LoogaRadianceDirection0);
TEXTURE3D(_LoogaRadianceLobe1);
TEXTURE3D(_LoogaRadianceDirection1);
float3 _LoogaRadianceBoundsMin;
float3 _LoogaRadianceBoundsInvSize;
float _LoogaRadianceProbeVolumeEnabled;

float _LoogaDirectionalLightmapsEnabled;

float3 LoogaOctDecode(float2 encoded)
{
    float2 f = encoded * 2.0 - 1.0;
    float3 n = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
    if (n.z < 0.0)
        n.xy = (1.0 - abs(n.yx)) * float2(n.x >= 0.0 ? 1.0 : -1.0, n.y >= 0.0 ? 1.0 : -1.0);
    return normalize(n);
}

float LoogaDiffuseKernel(float3 normalWS, float3 viewDirWS, float3 lightDirWS, float perceptualRoughness, half4 modelParameters)
{
    float NoLUnclamped = dot(normalWS, lightDirWS);
    float NoL = saturate(NoLUnclamped);
    float NoV = saturate(dot(normalWS, viewDirWS));
    float response = NoL;

    if (_LoogaLightingModel == LOOGA_MODEL_CUSTOM)
    {
        if (_LoogaProfileDiffuseModel == LOOGA_DIFFUSE_MINNAERT)
        {
            response = pow(max(NoL, 1e-4), _LoogaProfileMinnaertK) *
                pow(max(NoV, 1e-4), _LoogaProfileMinnaertK - 1.0);
        }
        else if (_LoogaProfileDiffuseModel == LOOGA_DIFFUSE_WRAPPED)
        {
            float wrap = _LoogaProfileDiffuseWrap;
            response = saturate((NoLUnclamped + wrap) /
                ((1.0 + wrap) * (1.0 + wrap)));
        }
        else if (_LoogaProfileDiffuseModel == LOOGA_DIFFUSE_OREN_NAYAR)
        {
            float sigma = radians(_LoogaProfileOrenNayarSigma);
            float sigma2 = sigma * sigma;
            float A = 1.0 - 0.5 * sigma2 / (sigma2 + 0.33);
            float B = 0.45 * sigma2 / (sigma2 + 0.09);
            float sinI = sqrt(saturate(1.0 - NoL * NoL));
            float sinR = sqrt(saturate(1.0 - NoV * NoV));
            float3 lightProjection = lightDirWS - normalWS * NoL;
            float3 viewProjection = viewDirWS - normalWS * NoV;
            float cosPhi = (sinI > 1e-4 && sinR > 1e-4)
                ? dot(normalize(lightProjection), normalize(viewProjection))
                : 0.0;
            float sinAlpha = max(sinI, sinR);
            float tanBeta = min(
                sinI / max(NoL, 1e-4), sinR / max(NoV, 1e-4));
            response = NoL * max(
                A + B * max(cosPhi, 0.0) * sinAlpha * tanBeta, 0.0);
        }
        else if (_LoogaProfileDiffuseModel == LOOGA_DIFFUSE_BANDED)
        {
            float bands = max(_LoogaProfileBandCount, 1.0);
            float scaled = NoL * bands;
            float banded = (floor(scaled) + smoothstep(
                0.0, max(_LoogaProfileBandFeather, 0.001), frac(scaled))) /
                bands;
            response = lerp(banded, NoL, _LoogaProfileBandBlend);
        }
        else if (_LoogaProfileDiffuseModel == LOOGA_DIFFUSE_DISNEY_BURLEY)
        {
            float3 halfVector = SafeNormalize(lightDirWS + viewDirWS);
            float LoH = saturate(dot(lightDirWS, halfVector));
            float fd90 = 0.5 + 2.0 * perceptualRoughness * LoH * LoH;
            float lightScatter =
                1.0 + (fd90 - 1.0) * Pow4(1.0 - NoL) * (1.0 - NoL);
            float viewScatter =
                1.0 + (fd90 - 1.0) * Pow4(1.0 - NoV) * (1.0 - NoV);
            response = NoL * lightScatter * viewScatter;
        }
    }
    else if (_LoogaLightingModel == LOOGA_MODEL_MINNAERT)
    {
        float k = DecodeLoogaMinnaertK(modelParameters);
        response = pow(max(NoL, 1e-4), k) * pow(max(NoV, 1e-4), k - 1.0);
    }
    else if (_LoogaLightingModel == LOOGA_MODEL_OVERWATCH)
    {
        float wrap = DecodeLoogaOverwatchWrap(modelParameters);
        response = saturate((NoLUnclamped + wrap) / ((1.0 + wrap) * (1.0 + wrap)));
    }
    else if (_LoogaLightingModel == LOOGA_MODEL_OREN_NAYAR)
    {
        float sigma = DecodeLoogaOrenNayarSigma(modelParameters);
        float sigma2 = sigma * sigma;
        float A = 1.0 - 0.5 * sigma2 / (sigma2 + 0.33);
        float B = 0.45 * sigma2 / (sigma2 + 0.09);
        float sinI = sqrt(saturate(1.0 - NoL * NoL));
        float sinR = sqrt(saturate(1.0 - NoV * NoV));
        float3 lightProjection = lightDirWS - normalWS * NoL;
        float3 viewProjection = viewDirWS - normalWS * NoV;
        float cosPhi = (sinI > 1e-4 && sinR > 1e-4) ? dot(normalize(lightProjection), normalize(viewProjection)) : 0.0;
        float sinAlpha = max(sinI, sinR);
        float tanBeta = min(sinI / max(NoL, 1e-4), sinR / max(NoV, 1e-4));
        response = NoL * max(A + B * max(cosPhi, 0.0) * sinAlpha * tanBeta, 0.0);
    }
    else if (_LoogaLightingModel == LOOGA_MODEL_ARKANE)
    {
        float bands = DecodeLoogaArkaneBandCount(modelParameters);
        float feather = max(DecodeLoogaArkaneBandFeather(modelParameters), 0.001);
        float scaled = NoL * bands;
        response = lerp((floor(scaled) + smoothstep(0.0, feather, frac(scaled))) / bands, NoL, 0.1);
    }
    else if (_LoogaLightingModel == LOOGA_MODEL_DISNEY_BURLEY)
    {
        float3 halfVector = SafeNormalize(lightDirWS + viewDirWS);
        float LoH = saturate(dot(lightDirWS, halfVector));
        float fd90 = 0.5 + 2.0 * perceptualRoughness * LoH * LoH;
        float lightScatter = 1.0 + (fd90 - 1.0) * Pow4(1.0 - NoL) * (1.0 - NoL);
        float viewScatter = 1.0 + (fd90 - 1.0) * Pow4(1.0 - NoV) * (1.0 - NoV);
        response = NoL * lightScatter * viewScatter;
    }

    return response;
}

float LoogaDiffuseProfileStrength()
{
    return _LoogaLightingModel == LOOGA_MODEL_CUSTOM
        ? _LoogaProfileDiffuseStrength
        : 1.0;
}

#if defined(LIGHTMAP_ON) && defined(DIRLIGHTMAP_COMBINED)
half3 SampleLoogaDirectionalLightmap(float2 staticLightmapUV, half3 normalWS, half3 geometricNormalWS, float3 viewDirWS, float perceptualRoughness, half4 modelParameters)
{
    half4 transformCoords = half4(1, 1, 0, 0);
    half3 illuminance = SampleSingleLightmap(TEXTURE2D_LIGHTMAP_ARGS(LIGHTMAP_NAME, LIGHTMAP_SAMPLER_NAME), LIGHTMAP_SAMPLE_EXTRA_ARGS, transformCoords, true);

    #if defined(LIGHTMAP_BICUBIC_SAMPLING)
        half4 direction = SampleLightmapBicubic(TEXTURE2D_LIGHTMAP_ARGS(LIGHTMAP_INDIRECTION_NAME, LIGHTMAP_SAMPLER_NAME), LIGHTMAP_SAMPLE_EXTRA_ARGS);
    #else
        half4 direction = SAMPLE_TEXTURE2D_LIGHTMAP(LIGHTMAP_INDIRECTION_NAME, LIGHTMAP_SAMPLER_NAME, LIGHTMAP_SAMPLE_EXTRA_ARGS);
    #endif

    half3 unityResult = SampleDirectionalLightmap(TEXTURE2D_LIGHTMAP_ARGS(LIGHTMAP_NAME, LIGHTMAP_SAMPLER_NAME),
        TEXTURE2D_LIGHTMAP_ARGS(LIGHTMAP_INDIRECTION_NAME, LIGHTMAP_SAMPLER_NAME), LIGHTMAP_SAMPLE_EXTRA_ARGS, transformCoords, normalWS, true);

    if (_LoogaDirectionalLightmapsEnabled < 0.5)
        return unityResult;

    float3 directionVector = direction.xyz - 0.5;
    float directionality = saturate(length(directionVector) * 2.0);
    float3 dominantDirection = normalize(directionVector + float3(0.0, 1e-5, 0.0));
    float shadedResponse = LoogaDiffuseKernel(normalWS, viewDirWS, dominantDirection, perceptualRoughness, modelParameters);
    float referenceResponse = LoogaDiffuseKernel(geometricNormalWS, viewDirWS, dominantDirection, perceptualRoughness, modelParameters);
    half3 modelResult = illuminance *
        min(shadedResponse / max(referenceResponse, 0.08), 8.0) *
        LoogaDiffuseProfileStrength();
    return lerp(illuminance, modelResult, directionality);
}
#endif

half3 SampleLoogaAuxiliaryLightmaps(float2 staticLightmapUV, float3 normalWS, float3 viewDirWS, float perceptualRoughness, half4 modelParameters)
{
    #if defined(LIGHTMAP_ON)
        if (_LoogaAuxiliaryLightmapsEnabled < 0.5)
            return 0.0h;

        int lightmapIndex = (int)round(unity_LightmapIndex.x);
        if (lightmapIndex >= 0 && lightmapIndex < _LoogaAuxiliaryLightmapCount)
        {
            half4 radiance0 = SAMPLE_TEXTURE2D_ARRAY(_LoogaAuxiliaryLobe0Array, sampler_LinearClamp, staticLightmapUV, lightmapIndex);
            half4 radiance1 = SAMPLE_TEXTURE2D_ARRAY(_LoogaAuxiliaryLobe1Array, sampler_LinearClamp, staticLightmapUV, lightmapIndex);
            half4 directions = SAMPLE_TEXTURE2D_ARRAY(_LoogaAuxiliaryDirectionArray, sampler_LinearClamp, staticLightmapUV, lightmapIndex);
            float3 direction0 = LoogaOctDecode(directions.rg);
            float3 direction1 = LoogaOctDecode(directions.ba);
            float response0 = LoogaDiffuseKernel(normalWS, viewDirWS, direction0, perceptualRoughness, modelParameters);
            float response1 = LoogaDiffuseKernel(normalWS, viewDirWS, direction1, perceptualRoughness, modelParameters);
            return (radiance0.rgb * radiance0.a * response0 +
                radiance1.rgb * radiance1.a * response1) *
                LoogaDiffuseProfileStrength();
        }
    #endif
    return 0.0h;
}

half3 SampleLoogaRadianceProbeVolume(float3 positionWS, float3 normalWS, float3 viewDirWS, float perceptualRoughness, half4 modelParameters, out half volumeWeight)
{
    volumeWeight = 0.0h;
    half3 result = 0.0h;
    if (_LoogaRadianceProbeVolumeEnabled > 0.5)
    {
        float3 uvw = (positionWS - _LoogaRadianceBoundsMin) * _LoogaRadianceBoundsInvSize;
        float3 edge = min(uvw, 1.0 - uvw);
        volumeWeight = saturate(min(edge.x, min(edge.y, edge.z)) * 16.0);
        if (volumeWeight > 0.0h)
        {
            half4 radiance0 = SAMPLE_TEXTURE3D(_LoogaRadianceLobe0, sampler_LinearClamp, saturate(uvw));
            half4 direction0 = SAMPLE_TEXTURE3D(_LoogaRadianceDirection0, sampler_LinearClamp, saturate(uvw));
            half4 radiance1 = SAMPLE_TEXTURE3D(_LoogaRadianceLobe1, sampler_LinearClamp, saturate(uvw));
            half4 direction1 = SAMPLE_TEXTURE3D(_LoogaRadianceDirection1, sampler_LinearClamp, saturate(uvw));
            float response0 = LoogaDiffuseKernel(normalWS, viewDirWS, normalize(direction0.xyz * 2.0 - 1.0), perceptualRoughness, modelParameters);
            float response1 = LoogaDiffuseKernel(normalWS, viewDirWS, normalize(direction1.xyz * 2.0 - 1.0), perceptualRoughness, modelParameters);
            result = (radiance0.rgb * radiance0.a * response0 +
                radiance1.rgb * radiance1.a * response1) *
                LoogaDiffuseProfileStrength();
        }
    }
    return result;
}

#if !defined(LOOGA_DISABLE_MODEL_REFLECTIONS)
float3 LoogaWorldToProbeLocal(float3 vectorWS, LoogaReflectionProbeGpuData probe)
{
    return float3(dot(vectorWS, probe.axisX.xyz), dot(vectorWS, probe.axisY.xyz), dot(vectorWS, probe.axisZ.xyz));
}

float3 LoogaProbeLocalToWorld(float3 vectorLS, LoogaReflectionProbeGpuData probe)
{
    return probe.axisX.xyz * vectorLS.x + probe.axisY.xyz * vectorLS.y + probe.axisZ.xyz * vectorLS.z;
}

float LoogaReflectionProbeWeight(float3 positionWS, LoogaReflectionProbeGpuData probe)
{
    float3 positionLS = LoogaWorldToProbeLocal(positionWS - probe.centerAndBlend.xyz, probe);
    float3 distanceToEdge = probe.extentsAndIntensity.xyz - abs(positionLS);
    float minimumDistance = min(distanceToEdge.x, min(distanceToEdge.y, distanceToEdge.z));
    return saturate(minimumDistance / probe.centerAndBlend.w);
}

half3 SampleLoogaReflectionArray(int family, float3 direction, int slice, float mip)
{
    if (family == LOOGA_REFLECTION_FAMILY_BECKMANN)
        return SAMPLE_TEXTURECUBE_ARRAY_LOD(_LoogaBeckmannReflectionArray, sampler_LinearClamp, direction, slice, mip).rgb;
    if (family == LOOGA_REFLECTION_FAMILY_PHONG)
        return SAMPLE_TEXTURECUBE_ARRAY_LOD(_LoogaPhongReflectionArray, sampler_LinearClamp, direction, slice, mip).rgb;
    return SAMPLE_TEXTURECUBE_ARRAY_LOD(_LoogaGGXReflectionArray, sampler_LinearClamp, direction, slice, mip).rgb;
}

half3 SampleLoogaModelReflectionRadiance(int family, float3 reflectVector, float3 positionWS, float perceptualRoughness, out half customWeight)
{
    customWeight = 0.0h;
    half3 result = 0.0h;
    if (_LoogaModelReflectionsEnabled > 0.5)
    {
        half3 accumulated = 0.0h;
        float totalWeight = 0.0;
        float mip = saturate(perceptualRoughness) * max(_LoogaReflectionMipCount - 1.0, 0.0);
        int count = min(_LoogaReflectionProbeCount, LOOGA_MAX_REFLECTION_PROBES);
        [loop] for (int i = 0; i < count; i++)
        {
            LoogaReflectionProbeGpuData probe = _LoogaReflectionProbeData[i];
            float weight = LoogaReflectionProbeWeight(positionWS, probe);
            if (weight <= 0.0)
                continue;

            float3 sampleDirection = reflectVector;
            if (probe.options.x > 0.5)
            {
                float3 positionLS = LoogaWorldToProbeLocal(positionWS - probe.centerAndBlend.xyz, probe);
                float3 directionLS = LoogaWorldToProbeLocal(reflectVector, probe);
                float3 safeDirection = float3(
                    abs(directionLS.x) > 1e-5 ? directionLS.x : (directionLS.x >= 0.0 ? 1e-5 : -1e-5),
                    abs(directionLS.y) > 1e-5 ? directionLS.y : (directionLS.y >= 0.0 ? 1e-5 : -1e-5),
                    abs(directionLS.z) > 1e-5 ? directionLS.z : (directionLS.z >= 0.0 ? 1e-5 : -1e-5));
                float3 boxFace = float3(
                    safeDirection.x >= 0.0 ? probe.extentsAndIntensity.x : -probe.extentsAndIntensity.x,
                    safeDirection.y >= 0.0 ? probe.extentsAndIntensity.y : -probe.extentsAndIntensity.y,
                    safeDirection.z >= 0.0 ? probe.extentsAndIntensity.z : -probe.extentsAndIntensity.z);
                float3 intersectionDistances = (boxFace - positionLS) / safeDirection;
                float intersectionDistance = min(intersectionDistances.x, min(intersectionDistances.y, intersectionDistances.z));
                float3 intersectionWS = probe.centerAndBlend.xyz + LoogaProbeLocalToWorld(positionLS + directionLS * intersectionDistance, probe);
                sampleDirection = intersectionWS - probe.capturePositionAndSlice.xyz;
            }

            int slice = (int)round(probe.capturePositionAndSlice.w);
            accumulated += SampleLoogaReflectionArray(family, sampleDirection, slice, mip) * probe.extentsAndIntensity.w * weight;
            totalWeight += weight;
        }

        if (totalWeight > 0.0)
        {
            customWeight = saturate(totalWeight);
            result = accumulated / totalWeight;
        }
    }
    return result;
}

float2 SampleLoogaBrdfLut(int family, float NoV, float perceptualRoughness)
{
    float2 uv = saturate(float2(NoV, perceptualRoughness));
    if (family == LOOGA_REFLECTION_FAMILY_BECKMANN)
        return SAMPLE_TEXTURE2D_LOD(_LoogaBeckmannBrdfLut, sampler_LinearClamp, uv, 0).rg;
    if (family == LOOGA_REFLECTION_FAMILY_PHONG)
        return SAMPLE_TEXTURE2D_LOD(_LoogaPhongBrdfLut, sampler_LinearClamp, uv, 0).rg;
    return SAMPLE_TEXTURE2D_LOD(_LoogaGGXBrdfLut, sampler_LinearClamp, uv, 0).rg;
}

half3 EvaluateLoogaModelAwareReflection(int family, float3 f0, float perceptualRoughness, float occlusion, float3 viewDirWS, float3 bentNormalWS, float NoV, float3 posWS, float2 uv)
{
    half3 reflectVector = reflect(-viewDirWS, bentNormalWS);
    half customWeight = 0.0h;
    half3 customRadiance = SampleLoogaModelReflectionRadiance(family, reflectVector, posWS, perceptualRoughness, customWeight);
    half3 fallbackRadiance = GlossyEnvironmentReflection(reflectVector, posWS, perceptualRoughness, 1.0, uv);
    half3 radiance = lerp(fallbackRadiance, customRadiance, customWeight);
    float2 dfg = SampleLoogaBrdfLut(family, NoV, perceptualRoughness);
    float3 customResponse = f0 * dfg.x + dfg.y;

    float roughness = perceptualRoughness * perceptualRoughness;
    float surfaceReduction = 1.0 / (roughness * roughness + 1.0);
    float reflectivity = max(max(f0.r, f0.g), f0.b);
    float grazingTerm = saturate(1.0 - perceptualRoughness + reflectivity);
    float3 fallbackResponse = lerp(f0, grazingTerm.xxx, Pow4(1.0 - NoV) * (1.0 - NoV)) * surfaceReduction;
    return radiance * lerp(fallbackResponse, customResponse, customWeight) * saturate(occlusion);
}
#else
half3 EvaluateLoogaModelAwareReflection(int family, float3 f0, float perceptualRoughness, float occlusion, float3 viewDirWS, float3 bentNormalWS, float NoV, float3 posWS, float2 uv)
{
    return 0.0h;
}
#endif

#endif
