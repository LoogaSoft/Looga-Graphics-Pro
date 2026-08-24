#ifndef LOOGA_LIGHTING_HELPERS_INCLUDED
#define LOOGA_LIGHTING_HELPERS_INCLUDED

#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaScatteringPacking.hlsl"

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaIndirectLighting.hlsl"
#if defined(LOOGA_DEFERRED_GBUFFER_INPUT)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferInput.hlsl"
#else
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferCommon.hlsl"
#endif

#if defined(_DBUFFER)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
#endif
TEXTURE2D_X(_GTBNTexture);

float _GTBNDirectLightStrength;
float _GTBNIndirectLightStrength;
int _LoogaGTBNEnabled;
int _LoogaBentNormalsEnabled;
int _LoogaGTBNDebugMode;
int _LoogaAdvancedMaterialDataEnabled;
int _LoogaHasSSSSProfileTexture;
int _LoogaSubsurfaceScatteringEnabled;
int _LoogaBacklightingEnabled;
float _LoogaBacklightingIntensity;

#ifndef LOOGA_SHADOW_GLOBALS_DECLARED
#define LOOGA_SHADOW_GLOBALS_DECLARED
float _LoogaShadowsEnabled;
#endif

half GetLoogaMainLightDirectOcclusion(half occlusion)
{
    // Looga Shadows owns directional visibility. Broad GTBN accessibility is
    // indirect data and must not replace the main light's shadow visibility.
    return _LoogaShadowsEnabled >= 0.5 ? 1.0h : saturate(occlusion);
}

half GetLoogaGTBNDirectOcclusion(half occlusion)
{
    half enabledStrength = saturate(_GTBNDirectLightStrength) * saturate((half)_LoogaGTBNEnabled);
    return lerp(1.0h, saturate(occlusion), enabledStrength);
}

static const uint LOOGA_MATERIAL_FLAG_ENVIRONMENT_REFLECTIONS_OFF = 16u;
static const uint LOOGA_MATERIAL_FLAG_DUAL_LOBE = 32u;
static const uint LOOGA_MATERIAL_FLAG_ADVANCED_DATA = 64u;
static const uint LOOGA_MATERIAL_FLAG_MARKER = 128u;

uint GetLoogaCommonMaterialFlags()
{
    uint flags = 0u;

    #if defined(LOOGA_DYNAMIC_MATERIAL_OPTIONS)
        if (_SPECULARHIGHLIGHTS_OFF)
            flags |= kMaterialFlagSpecularHighlightsOff;
    #elif defined(_SPECULARHIGHLIGHTS_OFF)
        flags |= kMaterialFlagSpecularHighlightsOff;
    #endif

    #if defined(LOOGA_DYNAMIC_MATERIAL_OPTIONS)
        if (_ENVIRONMENTREFLECTIONS_OFF)
            flags |= LOOGA_MATERIAL_FLAG_ENVIRONMENT_REFLECTIONS_OFF;
    #elif defined(_ENVIRONMENTREFLECTIONS_OFF)
        flags |= LOOGA_MATERIAL_FLAG_ENVIRONMENT_REFLECTIONS_OFF;
    #endif

    #if defined(_SPECULAR_SETUP)
        flags |= kMaterialFlagSpecularSetup;
    #endif

    #if !defined(LOOGA_LITE_MATERIAL)
        flags |= LOOGA_MATERIAL_FLAG_ADVANCED_DATA;
    #endif

    #if defined(LOOGA_DYNAMIC_MATERIAL_OPTIONS)
        if (_RECEIVE_SHADOWS_OFF)
            flags |= kMaterialFlagReceiveShadowsOff;
    #elif defined(_RECEIVE_SHADOWS_OFF)
        flags |= kMaterialFlagReceiveShadowsOff;
    #endif

    #if defined(LIGHTMAP_ON) && defined(_MIXED_LIGHTING_SUBTRACTIVE)
        flags |= kMaterialFlagSubtractiveMixedLighting;
    #endif

    return flags;
}

half PackLoogaMaterialFlags(uint flags)
{
    return PackGBufferMaterialFlags(flags | LOOGA_MATERIAL_FLAG_MARKER);
}

half GetLoogaMetallicReflectivity(half metallic)
{
    return half(1.0) - OneMinusReflectivityMetallic(metallic);
}

half3 GetLoogaDiffuseColor(half3 albedo, half metallic, half3 specularF0)
{
    #if defined(_SPECULAR_SETUP)
        return albedo * (half(1.0) - ReflectivitySpecular(specularF0));
    #else
        return albedo * OneMinusReflectivityMetallic(metallic);
    #endif
}

half3 PackLoogaGBufferSpecular(half metallic, half3 specularF0)
{
    #if defined(_SPECULAR_SETUP)
        return specularF0;
    #else
        return half3(GetLoogaMetallicReflectivity(metallic), 0.0h, 0.0h);
    #endif
}

#define LOOGA_DECL_SV_TARGET(idx) SV_Target##idx
#define LOOGA_DECL_OPT_GBUFFER_TARGET(type, name, idx) type name : LOOGA_DECL_SV_TARGET(GBUFFER_IDX_AFTER(idx))

struct LoogaGBufferOutput
{
    half4 GBuffer0 : SV_Target0;
    half4 GBuffer1 : SV_Target1;
    half4 GBuffer2 : SV_Target2;
    half4 GBuffer3 : SV_Target3;

    #if defined(GBUFFER_FEATURE_DEPTH)
    LOOGA_DECL_OPT_GBUFFER_TARGET(float, depth, GBUFFER_IDX_R_DEPTH);
    #endif

    #if defined(GBUFFER_FEATURE_SHADOWMASK)
    LOOGA_DECL_OPT_GBUFFER_TARGET(half4, shadowMask, GBUFFER_IDX_RGBA_SHADOWMASK);
    #endif

    #if defined(GBUFFER_FEATURE_RENDERING_LAYERS)
    LOOGA_DECL_OPT_GBUFFER_TARGET(uint, meshRenderingLayers, GBUFFER_IDX_R_RENDERING_LAYERS);
    #endif
};

#undef LOOGA_DECL_SV_TARGET
#undef LOOGA_DECL_OPT_GBUFFER_TARGET

void FillLoogaGBufferExtraOutputs(inout LoogaGBufferOutput output, float positionCSZ, half4 shadowMask)
{
    #if defined(GBUFFER_FEATURE_DEPTH)
        output.depth = positionCSZ;
    #endif

    #if defined(GBUFFER_FEATURE_SHADOWMASK)
        output.shadowMask = shadowMask;
    #endif

    #if defined(GBUFFER_FEATURE_RENDERING_LAYERS)
        output.meshRenderingLayers = EncodeMeshRenderingLayer();
    #endif
}

void ApplyLoogaReceiveShadowOption(inout Light light)
{
    #if defined(LOOGA_DYNAMIC_MATERIAL_OPTIONS)
        if (_RECEIVE_SHADOWS_OFF)
            light.shadowAttenuation = 1.0;
    #elif defined(_RECEIVE_SHADOWS_OFF)
        light.shadowAttenuation = 1.0;
    #endif
}

void ApplyLoogaScreenSpaceDirectAO(inout Light light, float2 normalizedScreenSpaceUV)
{
    #if defined(_SCREEN_SPACE_OCCLUSION) && !defined(_SURFACE_TYPE_TRANSPARENT)
        light.color *= GetScreenSpaceAmbientOcclusion(normalizedScreenSpaceUV).directAmbientOcclusion;
    #endif
}

bool LoogaLightMatchesRenderingLayer(Light light, uint meshRenderingLayers)
{
    #if defined(_LIGHT_LAYERS)
        return IsMatchingLightLayer(light.layerMask, meshRenderingLayers);
    #else
        return true;
    #endif
}

void ApplyLoogaDBuffer(float4 positionCS, inout half3 albedo, inout half3 normalWS, inout half metallic, inout half3 specularF0, inout half occlusion, inout half smoothness)
{
    #if defined(_DBUFFER)
        SurfaceData surfaceData = (SurfaceData)0;
        surfaceData.albedo = albedo;
        surfaceData.normalTS = half3(0.0h, 0.0h, 1.0h);
        surfaceData.metallic = metallic;
        surfaceData.specular = specularF0;
        surfaceData.occlusion = occlusion;
        surfaceData.smoothness = smoothness;
        surfaceData.alpha = 1.0h;

        InputData inputData = (InputData)0;
        inputData.normalWS = normalWS;
        ApplyDecalToSurfaceData(positionCS, surfaceData, inputData);

        albedo = surfaceData.albedo;
        normalWS = inputData.normalWS;
        metallic = surfaceData.metallic;
        specularF0 = surfaceData.specular;
        occlusion = surfaceData.occlusion;
        smoothness = surfaceData.smoothness;
    #endif
}

half3 GetLoogaDirectSpecularF0(half3 f0)
{
    #if defined(LOOGA_DYNAMIC_MATERIAL_OPTIONS)
        return _SPECULARHIGHLIGHTS_OFF ? half3(0.0h, 0.0h, 0.0h) : f0;
    #elif defined(_SPECULARHIGHLIGHTS_OFF)
        return half3(0.0h, 0.0h, 0.0h);
    #else
        return f0;
    #endif
}

bool LoogaEnvironmentReflectionsEnabled()
{
    #if defined(LOOGA_DYNAMIC_MATERIAL_OPTIONS)
        return !_ENVIRONMENTREFLECTIONS_OFF;
    #elif defined(_ENVIRONMENTREFLECTIONS_OFF)
        return false;
    #else
        return true;
    #endif
}

float SchlickFresnel(float input)
{
    float v = saturate(1.0 - input);
    return v * v * v * v * v;
}

float3 FresnelSchlick(float3 f0, float cosTheta)
{
    float specularEnabled = step(1e-5, max(f0.r, max(f0.g, f0.b)));
    return (f0 + (1.0 - f0) * SchlickFresnel(cosTheta)) * specularEnabled;
}

float3 FresnelSchlickRoughness(float3 f0, float cosTheta, float perceptualRoughness)
{
    return f0 + (max(1.0 - perceptualRoughness, f0) - f0) * SchlickFresnel(cosTheta);
}

float3 Fresnel(float3 f0, float cosTheta, float perceptualRoughness)
{
    return FresnelSchlickRoughness(f0, cosTheta, perceptualRoughness);
}

float NDF(float roughness, float NoH)
{
    roughness = max(roughness, 0.002);
    float a2 = roughness * roughness;
    float NoH2 = NoH * NoH;
    float c = (NoH2 * (a2 - 1.0)) + 1.0;
    return a2 / max(PI * c * c, 1e-7);
}

float GSF(float NoL, float NoV, float roughness)
{
    roughness = max(roughness, 0.002);
    float k = ((roughness * 1.0) * (roughness * 1.0)) / 8.0;
    float l = NoL / (NoL * (1.0 - k) + k);
    float v = NoV / (NoV * (1.0 - k) + k);
    return max(l * v, 1e-7);
}

float3 EvaluateLoogaDirectGGXSpecular(float3 f0, float roughness, float NoL, float NoV, float NoH, float VoH)
{
    float3 ndf = NDF(roughness, NoH);
    float3 fresnel = FresnelSchlick(f0, VoH);
    float gsf = GSF(NoL, NoV, roughness);
    return (fresnel * ndf * gsf) / max((4.0 * NoL * NoV), 1e-7);
}

float LoogaBurleySmithGGX(float NoX, float alphaG)
{
    float alphaG2 = alphaG * alphaG;
    return 1.0 / max(NoX + sqrt(alphaG2 + NoX * NoX - alphaG2 * NoX * NoX), 1e-7);
}

float GetLoogaBurleyAlphaG(float perceptualRoughness)
{
    float alphaG = 0.5 + saturate(perceptualRoughness) * 0.5;
    return alphaG * alphaG;
}

float3 EvaluateLoogaBurleyMatchedGGXSpecular(float3 f0, float perceptualRoughness, float NoL, float NoV, float NoH, float VoH)
{
    float alpha = perceptualRoughness * perceptualRoughness;
    float alphaG = GetLoogaBurleyAlphaG(perceptualRoughness);
    float3 fresnel = FresnelSchlick(f0, VoH);
    float distribution = NDF(alpha, NoH);
    float geometry = LoogaBurleySmithGGX(NoL, alphaG) * LoogaBurleySmithGGX(NoV, alphaG);
    return fresnel * distribution * geometry;
}

float3 EvaluateLoogaDirectBeckmannSpecular(float3 f0, float roughness, float NoL, float NoV, float NoH, float VoH)
{
    float alpha = max(roughness, 0.045);
    float alpha2 = alpha * alpha;
    float NoH2 = max(NoH * NoH, 1e-5);
    float distribution = exp((NoH2 - 1.0) / max(alpha2 * NoH2, 1e-5)) / max(PI * alpha2 * NoH2 * NoH2, 1e-5);
    float3 fresnel = FresnelSchlick(f0, VoH);
    float perceptualRoughness = sqrt(saturate(roughness));
    float alphaG = GetLoogaBurleyAlphaG(perceptualRoughness);
    float geometry = LoogaBurleySmithGGX(NoL, alphaG) * LoogaBurleySmithGGX(NoV, alphaG);
    return fresnel * distribution * geometry;
}

float LoogaPerceptualRoughnessFromPhongExponent(float exponent)
{
    float alpha = sqrt(2.0 / max(exponent + 2.0, 3.0));
    return sqrt(saturate(alpha));
}

float3 EvaluateLoogaGGXIndirect(float3 f0, float samplePerceptualRoughness, float occlusion, float3 viewDirWS, float3 bentNormalWS, float NoV, float3 posWS, float2 uv, float fresnelPower, float specularOcclusion)
{
    float3 result = 0.0;
    if (_LoogaModelReflectionsEnabled > 0.5)
    {
        result = EvaluateLoogaModelAwareReflection(LOOGA_REFLECTION_FAMILY_GGX, f0, samplePerceptualRoughness, occlusion, viewDirWS, bentNormalWS, NoV, posWS, uv) * saturate(specularOcclusion);
    }
    else
    {
        samplePerceptualRoughness = saturate(samplePerceptualRoughness);
        half3 reflectVector = reflect(-viewDirWS, bentNormalWS);
        half3 environmentRadiance = GlossyEnvironmentReflection(reflectVector, posWS, samplePerceptualRoughness, 1.0, uv);
        float roughness = samplePerceptualRoughness * samplePerceptualRoughness;
        float surfaceReduction = 1.0 / (roughness * roughness + 1.0);
        float reflectivity = max(max(f0.r, f0.g), f0.b);
        float grazingTerm = saturate(1.0 - samplePerceptualRoughness + reflectivity);
        float fresnel = pow(saturate(1.0 - NoV), fresnelPower);
        float3 environmentFresnel = lerp(f0, grazingTerm.xxx, fresnel);
        result = environmentRadiance * environmentFresnel * surfaceReduction * saturate(occlusion * specularOcclusion);
    }
    return result;
}

float3 EvaluateLoogaPhongIndirect(float3 f0, float exponent, float occlusion, float3 viewDirWS, float3 bentNormalWS, float NoV, float3 posWS, float2 uv, float strength)
{
    float3 result = 0.0;
    if (_LoogaModelReflectionsEnabled > 0.5)
    {
        float customEnvRoughness = LoogaPerceptualRoughnessFromPhongExponent(exponent);
        result = EvaluateLoogaModelAwareReflection(LOOGA_REFLECTION_FAMILY_PHONG, f0, customEnvRoughness, occlusion, viewDirWS, bentNormalWS, NoV, posWS, uv) * max(strength, 0.0);
    }
    else
    {
        half3 reflectVector = reflect(-viewDirWS, bentNormalWS);
        float envRoughness = LoogaPerceptualRoughnessFromPhongExponent(exponent);
        half3 environmentRadiance = GlossyEnvironmentReflection(reflectVector, posWS, envRoughness, 1.0, uv);
        float3 fresnel = FresnelSchlick(f0, NoV);
        result = environmentRadiance * fresnel * saturate(occlusion) * max(strength, 0.0);
    }
    return result;
}

float3 EvaluateLoogaBeckmannIndirect(float3 f0, float perceptualRoughness, float occlusion, float3 viewDirWS, float3 bentNormalWS, float NoV, float3 posWS, float2 uv)
{
    float3 result = 0.0;
    if (_LoogaModelReflectionsEnabled > 0.5)
    {
        result = EvaluateLoogaModelAwareReflection(LOOGA_REFLECTION_FAMILY_BECKMANN, f0, perceptualRoughness, occlusion, viewDirWS, bentNormalWS, NoV, posWS, uv);
    }
    else
    {
        // URP reflection probes are GGX-prefiltered. A modest remap best matches the
        // narrower Beckmann tail without requiring a second probe convolution set.
        float beckmannProbeRoughness = saturate(perceptualRoughness * 0.88 + 0.015);
        result = EvaluateLoogaGGXIndirect(f0, beckmannProbeRoughness, occlusion, viewDirWS, bentNormalWS, NoV, posWS, uv, 5.0, 1.0);
    }
    return result;
}

float3 EvaluateLoogaSelectableIndirect(int model, float3 f0, float perceptualRoughness, float occlusion, float3 viewDirWS, float3 bentNormalWS, float NoV, float3 posWS, float2 uv)
{
    float3 result = 0.0;
    if (model == 1)
    {
        result = EvaluateLoogaBeckmannIndirect(f0, perceptualRoughness, occlusion, viewDirWS, bentNormalWS, NoV, posWS, uv);
    }
    else if (model == 2)
    {
        float exponent = exp2(lerp(11.0, 1.0, saturate(perceptualRoughness)));
        result = EvaluateLoogaPhongIndirect(f0, exponent, occlusion, viewDirWS, bentNormalWS, NoV, posWS, uv, 1.0);
    }
    else
    {
        result = EvaluateLoogaGGXIndirect(f0, perceptualRoughness, occlusion, viewDirWS, bentNormalWS, NoV, posWS, uv, 5.0, 1.0);
    }
    return result;
}

float3 EvaluateSecondaryGGXLobe(float3 f0, float secondaryRoughness, float3 normalWS, float3 lightDir, float3 viewDir, float NoV, float3 radiance, float lobeMix)
{
    float NoL = saturate(dot(normalWS, lightDir));
    if (NoL <= 0.0) return 0.0;

    float3 H = SafeNormalize(lightDir + viewDir);
    float NoH = saturate(dot(normalWS, H));
    float VoH = saturate(dot(viewDir, H));

    secondaryRoughness = max(saturate(secondaryRoughness), 0.08);
    float roughness2 = secondaryRoughness * secondaryRoughness;
    float3 specular = EvaluateLoogaDirectGGXSpecular(f0, roughness2, NoL, NoV, NoH, VoH);
    return specular * radiance * NoL * PI * lobeMix;
}

float3 EvaluateLoogaAmbientProbe(float3 normalWS)
{
    #if defined(EVALUATE_SH_VERTEX) || defined(EVALUATE_SH_MIXED)
        half3 ambient = EvaluateAmbientProbeSRGB(normalWS);
    #else
        half3 ambient = SampleSHPixel(half3(0.0, 0.0, 0.0), normalWS);
    #endif

    return ambient;
}

float3 EvaluateLoogaAmbientDiffuse(float3 diffuseColor, float3 normalWS, float occlusion)
{
    half3 ambient = EvaluateLoogaAmbientProbe(normalWS);
    return diffuseColor * ambient * occlusion;
}

float3 FinalizeLoogaBakedGI(float3 bakedGI, float3 positionWS, half3 normalWS, half4 shadowMask)
{
    Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS), positionWS, shadowMask);
    MixRealtimeAndBakedGI(mainLight, normalWS, bakedGI, shadowMask);
    return bakedGI;
}

#if defined(_SCREEN_SPACE_IRRADIANCE)
    #define LOOGA_SAMPLE_BAKED_LIGHTING(input, normalWS, perceptualRoughness, modelParameters, bakedGI, shadowMask) { \
        shadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h); \
        bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy); \
        bakedGI = FinalizeLoogaBakedGI(bakedGI, input.positionWS, normalWS, shadowMask); \
    }
#elif defined(DYNAMICLIGHTMAP_ON)
    #define LOOGA_SAMPLE_BAKED_LIGHTING(input, normalWS, perceptualRoughness, modelParameters, bakedGI, shadowMask) { \
        shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV); \
        bakedGI = SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV, input.vertexSH, normalWS); \
        bakedGI += SampleLoogaAuxiliaryLightmaps(input.staticLightmapUV, normalWS, GetWorldSpaceNormalizeViewDir(input.positionWS), perceptualRoughness, modelParameters); \
        bakedGI = FinalizeLoogaBakedGI(bakedGI, input.positionWS, normalWS, shadowMask); \
    }
#elif defined(LIGHTMAP_ON) && defined(DIRLIGHTMAP_COMBINED)
    #define LOOGA_SAMPLE_BAKED_LIGHTING(input, normalWS, perceptualRoughness, modelParameters, bakedGI, shadowMask) { \
        shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV); \
        bakedGI = SampleLoogaDirectionalLightmap(input.staticLightmapUV, normalWS, input.normalWS, GetWorldSpaceNormalizeViewDir(input.positionWS), perceptualRoughness, modelParameters); \
        bakedGI += SampleLoogaAuxiliaryLightmaps(input.staticLightmapUV, normalWS, GetWorldSpaceNormalizeViewDir(input.positionWS), perceptualRoughness, modelParameters); \
        bakedGI = FinalizeLoogaBakedGI(bakedGI, input.positionWS, normalWS, shadowMask); \
    }
#elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    #define LOOGA_SAMPLE_BAKED_LIGHTING(input, normalWS, perceptualRoughness, modelParameters, bakedGI, shadowMask) { \
        shadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h); \
        bakedGI = SAMPLE_GI(input.vertexSH, GetAbsolutePositionWS(input.positionWS), normalWS, GetWorldSpaceNormalizeViewDir(input.positionWS), input.positionCS.xy, input.probeOcclusion, shadowMask); \
        half loogaVolumeWeight; \
        half3 loogaVolumeGI = SampleLoogaRadianceProbeVolume(input.positionWS, normalWS, GetWorldSpaceNormalizeViewDir(input.positionWS), perceptualRoughness, modelParameters, loogaVolumeWeight); \
        bakedGI = lerp(bakedGI, loogaVolumeGI, loogaVolumeWeight); \
        bakedGI = FinalizeLoogaBakedGI(bakedGI, input.positionWS, normalWS, shadowMask); \
    }
#elif defined(LIGHTMAP_ON)
    #define LOOGA_SAMPLE_BAKED_LIGHTING(input, normalWS, perceptualRoughness, modelParameters, bakedGI, shadowMask) { \
        shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV); \
        bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, normalWS); \
        bakedGI += SampleLoogaAuxiliaryLightmaps(input.staticLightmapUV, normalWS, GetWorldSpaceNormalizeViewDir(input.positionWS), perceptualRoughness, modelParameters); \
        bakedGI = FinalizeLoogaBakedGI(bakedGI, input.positionWS, normalWS, shadowMask); \
    }
#else
    #define LOOGA_SAMPLE_BAKED_LIGHTING(input, normalWS, perceptualRoughness, modelParameters, bakedGI, shadowMask) { \
        shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV); \
        bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, normalWS); \
        half loogaVolumeWeight; \
        half3 loogaVolumeGI = SampleLoogaRadianceProbeVolume(input.positionWS, normalWS, GetWorldSpaceNormalizeViewDir(input.positionWS), perceptualRoughness, modelParameters, loogaVolumeWeight); \
        bakedGI = lerp(bakedGI, loogaVolumeGI, loogaVolumeWeight); \
        bakedGI = FinalizeLoogaBakedGI(bakedGI, input.positionWS, normalWS, shadowMask); \
    }
#endif

float3 EvaluateLoogaBakedDiffuse(float3 diffuseColor, half3 bakedGI, float occlusion)
{
    return diffuseColor * bakedGI * occlusion;
}

float GetLoogaSafePerceptualRoughness(float perceptualRoughness)
{
    return max(perceptualRoughness, 0.08);
}

float GetLoogaMetalIndirectOcclusion(float occlusion, float metallic)
{
    return saturate(occlusion);
}

float3 EvaluateTransmission(float3 scatteringColor, float scatterWidth, float ambientScatterStrength,
    float transmissionShadowSoftness, float rimPower, float distortion, float3 lightDir,
    float3 viewDirWS, float3 normalWS, float3 unshadowedLightRadiance,
    float shadowAttenuation, float transmissionMask)
{
    if (_LoogaBacklightingEnabled == 0 || transmissionMask <= 0.0)
        return 0.0;

    float3 N = SafeNormalize(normalWS);
    float3 L = SafeNormalize(lightDir);
    float3 V = SafeNormalize(viewDirWS);
    float backFacing = saturate(-dot(N, L));
    if (backFacing <= 0.0)
        return 0.0;

    float3 bentLight = SafeNormalize(L + N * saturate(distortion));
    float forwardPhase = saturate(dot(V, -bentLight));
    float scatterPower = lerp(10.0, 1.25, saturate(scatterWidth / 5.0));
    float directionalGlow = pow(forwardPhase, scatterPower);
    float edge = pow(saturate(1.0 - abs(dot(N, V))), max(rimPower, 1.0));
    float broadBacklight = pow(backFacing, lerp(2.0, 0.5, saturate(ambientScatterStrength / 5.0)));
    float transmissionProfile = broadBacklight * directionalGlow * lerp(0.35, 1.0, edge);
    float transmissionIntensity = transmissionProfile * max(scatterWidth, 0.1) * 0.5;
    float falloffExponent = lerp(8.0, 1.0, saturate(transmissionShadowSoftness));
    float softShadow = pow(saturate(shadowAttenuation), falloffExponent);
    return scatteringColor * unshadowedLightRadiance * transmissionIntensity * softShadow *
        transmissionMask * max(_LoogaBacklightingIntensity, 0.0);
}

#endif
