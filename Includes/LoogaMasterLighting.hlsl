#ifndef LOOGA_MASTER_LIGHTING_INCLUDED
#define LOOGA_MASTER_LIGHTING_INCLUDED

#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaModelParameters.hlsl"

#if defined(LOOGA_FIXED_LIGHTING_MODEL)
    #if LOOGA_FIXED_LIGHTING_MODEL == LOOGA_MODEL_CUSTOM
        #include "Packages/com.loogasoft.loogagraphicspro/Includes/Lighting Models/Configurable.hlsl"
    #elif LOOGA_FIXED_LIGHTING_MODEL == LOOGA_MODEL_SOURCE2
        #include "Packages/com.loogasoft.loogagraphicspro/Includes/Lighting Models/Source2.hlsl"
    #elif LOOGA_FIXED_LIGHTING_MODEL == LOOGA_MODEL_MINNAERT
        #include "Packages/com.loogasoft.loogagraphicspro/Includes/Lighting Models/Minnaert.hlsl"
    #elif LOOGA_FIXED_LIGHTING_MODEL == LOOGA_MODEL_OVERWATCH
        #include "Packages/com.loogasoft.loogagraphicspro/Includes/Lighting Models/Overwatch.hlsl"
    #elif LOOGA_FIXED_LIGHTING_MODEL == LOOGA_MODEL_OREN_NAYAR
        #include "Packages/com.loogasoft.loogagraphicspro/Includes/Lighting Models/OrenNayar.hlsl"
    #elif LOOGA_FIXED_LIGHTING_MODEL == LOOGA_MODEL_ARKANE
        #include "Packages/com.loogasoft.loogagraphicspro/Includes/Lighting Models/Arkane.hlsl"
    #else
        #include "Packages/com.loogasoft.loogagraphicspro/Includes/Lighting Models/DisneyBurley.hlsl"
    #endif
#else
    #include "Packages/com.loogasoft.loogagraphicspro/Includes/Lighting Models/DisneyBurley.hlsl"
    #include "Packages/com.loogasoft.loogagraphicspro/Includes/Lighting Models/Source2.hlsl"
    #include "Packages/com.loogasoft.loogagraphicspro/Includes/Lighting Models/Minnaert.hlsl"
    #include "Packages/com.loogasoft.loogagraphicspro/Includes/Lighting Models/OrenNayar.hlsl"
    #include "Packages/com.loogasoft.loogagraphicspro/Includes/Lighting Models/Overwatch.hlsl"
    #include "Packages/com.loogasoft.loogagraphicspro/Includes/Lighting Models/Arkane.hlsl"
    #include "Packages/com.loogasoft.loogagraphicspro/Includes/Lighting Models/Configurable.hlsl"
#endif

// ==============================================================================
// MASTER DIRECT LIGHTING EVALUATION
// ==============================================================================
float3 EvaluateGlobalLoogaLighting(float3 diffuseColor, float3 f0, float perceptualRoughness, float3 normalWS, float occlusion, float3 viewDirectionWS, float NoV, float3 lightDir, float3 lightColor, half4 modelParameters)
{
    perceptualRoughness = GetLoogaSafePerceptualRoughness(saturate(perceptualRoughness));

    #if defined(LOOGA_FIXED_LIGHTING_MODEL)
        #if LOOGA_FIXED_LIGHTING_MODEL == LOOGA_MODEL_CUSTOM
            return EvaluateLighting_Configurable(diffuseColor, f0, perceptualRoughness, normalWS, occlusion, viewDirectionWS, NoV, lightDir, lightColor);
        #elif LOOGA_FIXED_LIGHTING_MODEL == LOOGA_MODEL_SOURCE2
            return EvaluateLighting_Source2(diffuseColor, f0, perceptualRoughness, normalWS, occlusion, viewDirectionWS, NoV, lightDir, lightColor);
        #elif LOOGA_FIXED_LIGHTING_MODEL == LOOGA_MODEL_MINNAERT
            return EvaluateLighting_Minnaert(diffuseColor, f0, perceptualRoughness, normalWS, occlusion, viewDirectionWS, NoV, lightDir, lightColor, modelParameters);
        #elif LOOGA_FIXED_LIGHTING_MODEL == LOOGA_MODEL_OVERWATCH
            return EvaluateLighting_Overwatch(diffuseColor, f0, perceptualRoughness, normalWS, occlusion, viewDirectionWS, NoV, lightDir, lightColor, modelParameters);
        #elif LOOGA_FIXED_LIGHTING_MODEL == LOOGA_MODEL_OREN_NAYAR
            return EvaluateLighting_OrenNayar(diffuseColor, f0, perceptualRoughness, normalWS, occlusion, viewDirectionWS, NoV, lightDir, lightColor, modelParameters);
        #elif LOOGA_FIXED_LIGHTING_MODEL == LOOGA_MODEL_ARKANE
            return EvaluateLighting_Arkane(diffuseColor, f0, perceptualRoughness, normalWS, occlusion, viewDirectionWS, NoV, lightDir, lightColor, modelParameters);
        #else
            return EvaluateLighting_DisneyBurley(diffuseColor, f0, perceptualRoughness, normalWS, occlusion, viewDirectionWS, NoV, lightDir, lightColor);
        #endif
    #else
        [branch] switch (_LoogaLightingModel)
        {
        case LOOGA_MODEL_CUSTOM:       return EvaluateLighting_Configurable(diffuseColor, f0, perceptualRoughness, normalWS, occlusion, viewDirectionWS, NoV, lightDir, lightColor);
        case LOOGA_MODEL_SOURCE2:      return EvaluateLighting_Source2(diffuseColor, f0, perceptualRoughness, normalWS, occlusion, viewDirectionWS, NoV, lightDir, lightColor);
        case LOOGA_MODEL_MINNAERT:     return EvaluateLighting_Minnaert(diffuseColor, f0, perceptualRoughness, normalWS, occlusion, viewDirectionWS, NoV, lightDir, lightColor, modelParameters);
        case LOOGA_MODEL_OVERWATCH:    return EvaluateLighting_Overwatch(diffuseColor, f0, perceptualRoughness, normalWS, occlusion, viewDirectionWS, NoV, lightDir, lightColor, modelParameters);
        case LOOGA_MODEL_OREN_NAYAR:   return EvaluateLighting_OrenNayar(diffuseColor, f0, perceptualRoughness, normalWS, occlusion, viewDirectionWS, NoV, lightDir, lightColor, modelParameters);
        case LOOGA_MODEL_ARKANE:       return EvaluateLighting_Arkane(diffuseColor, f0, perceptualRoughness, normalWS, occlusion, viewDirectionWS, NoV, lightDir, lightColor, modelParameters);
        default:                       return EvaluateLighting_DisneyBurley(diffuseColor, f0, perceptualRoughness, normalWS, occlusion, viewDirectionWS, NoV, lightDir, lightColor);
        }
    #endif
}

float3 EvaluateLoogaAdditionalLight(Light light, uint meshRenderingLayers, float3 diffuseColor, float3 directF0, float perceptualRoughness, float3 normalWS, float occlusion, float3 viewDirectionWS, float NoV, half4 modelParameters, bool forceReceiveShadowsOff, bool useSecondaryLobe, float secondaryRoughness, float lobeMix)
{
    #if defined(_LIGHT_LAYERS)
        if (!IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
            return 0.0;
    #endif

    if (forceReceiveShadowsOff)
        light.shadowAttenuation = 1.0;
    else
        ApplyLoogaReceiveShadowOption(light);

    float3 radiance = light.color * light.shadowAttenuation * light.distanceAttenuation;
    float3 color = EvaluateGlobalLoogaLighting(diffuseColor, directF0, perceptualRoughness, normalWS, occlusion, viewDirectionWS, NoV, light.direction, radiance, modelParameters);

    if (useSecondaryLobe)
        color += EvaluateSecondaryGGXLobe(directF0, secondaryRoughness, normalWS, light.direction, viewDirectionWS, NoV, radiance, lobeMix);

    return color;
}

float3 EvaluateLoogaAdditionalLights(float3 diffuseColor, float3 directF0, float perceptualRoughness, float3 normalWS, float occlusion, float3 viewDirectionWS, float NoV, half4 modelParameters, InputData inputData, half4 shadowMask, AmbientOcclusionFactor aoFactor, uint meshRenderingLayers, bool forceReceiveShadowsOff, bool useSecondaryLobe, float secondaryRoughness, float lobeMix)
{
    float3 color = 0.0;

    #if USE_CLUSTER_LIGHT_LOOP
        [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
        {
            CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
            Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
            color += EvaluateLoogaAdditionalLight(light, meshRenderingLayers, diffuseColor, directF0, perceptualRoughness, normalWS, occlusion, viewDirectionWS, NoV, modelParameters, forceReceiveShadowsOff, useSecondaryLobe, secondaryRoughness, lobeMix);
        }
    #endif

    uint pixelLightCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
        color += EvaluateLoogaAdditionalLight(light, meshRenderingLayers, diffuseColor, directF0, perceptualRoughness, normalWS, occlusion, viewDirectionWS, NoV, modelParameters, forceReceiveShadowsOff, useSecondaryLobe, secondaryRoughness, lobeMix);
    LIGHT_LOOP_END

    return color;
}

float3 EvaluateLoogaAdditionalLights(float3 diffuseColor, float3 directF0, float perceptualRoughness, float3 normalWS, float occlusion, float3 viewDirectionWS, float NoV, half4 modelParameters, float3 positionWS, float2 normalizedScreenSpaceUV, half4 shadowMask, bool forceReceiveShadowsOff, bool useSecondaryLobe, float secondaryRoughness, float lobeMix)
{
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalWS = normalWS;
    inputData.viewDirectionWS = viewDirectionWS;
    inputData.normalizedScreenSpaceUV = normalizedScreenSpaceUV;
    AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(normalizedScreenSpaceUV);

    return EvaluateLoogaAdditionalLights(diffuseColor, directF0, perceptualRoughness, normalWS, occlusion, viewDirectionWS, NoV, modelParameters, inputData, shadowMask, aoFactor, GetMeshRenderingLayer(), forceReceiveShadowsOff, useSecondaryLobe, secondaryRoughness, lobeMix);
}

float3 EvaluateLoogaAdditionalBacklight(Light light, uint meshRenderingLayers, float3 scatteringColor,
    float scatterWidth, float ambientScatterStrength, float transmissionShadowSoftness,
    float rimPower, float distortion, float3 normalWS, float3 viewDirectionWS,
    float transmissionMask, bool forceReceiveShadowsOff)
{
    #if defined(_LIGHT_LAYERS)
        if (!IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
            return 0.0;
    #endif

    if (forceReceiveShadowsOff)
        light.shadowAttenuation = 1.0;
    else
        ApplyLoogaReceiveShadowOption(light);

    return EvaluateTransmission(scatteringColor, scatterWidth, ambientScatterStrength,
        transmissionShadowSoftness, rimPower, distortion, light.direction, viewDirectionWS,
        normalWS, light.color * light.distanceAttenuation, light.shadowAttenuation, transmissionMask);
}

float3 EvaluateLoogaAdditionalBacklights(InputData inputData, half4 shadowMask,
    AmbientOcclusionFactor aoFactor, uint meshRenderingLayers, float3 scatteringColor,
    float scatterWidth, float ambientScatterStrength, float transmissionShadowSoftness,
    float rimPower, float distortion, float3 normalWS, float3 viewDirectionWS,
    float transmissionMask, bool forceReceiveShadowsOff)
{
    if (_LoogaBacklightingEnabled == 0 || transmissionMask <= 0.0)
        return 0.0;

    float3 color = 0.0;
    #if USE_CLUSTER_LIGHT_LOOP
        [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
        {
            CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
            Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
            color += EvaluateLoogaAdditionalBacklight(light, meshRenderingLayers, scatteringColor,
                scatterWidth, ambientScatterStrength, transmissionShadowSoftness, rimPower,
                distortion, normalWS, viewDirectionWS, transmissionMask, forceReceiveShadowsOff);
        }
    #endif

    uint pixelLightCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
        color += EvaluateLoogaAdditionalBacklight(light, meshRenderingLayers, scatteringColor,
            scatterWidth, ambientScatterStrength, transmissionShadowSoftness, rimPower,
            distortion, normalWS, viewDirectionWS, transmissionMask, forceReceiveShadowsOff);
    LIGHT_LOOP_END
    return color;
}

float3 EvaluateLoogaAdditionalBacklights(float3 positionWS, float2 normalizedScreenSpaceUV,
    half4 shadowMask, float3 scatteringColor, float scatterWidth, float ambientScatterStrength,
    float transmissionShadowSoftness, float rimPower, float distortion, float3 normalWS,
    float3 viewDirectionWS, float transmissionMask, bool forceReceiveShadowsOff)
{
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalWS = normalWS;
    inputData.viewDirectionWS = viewDirectionWS;
    inputData.normalizedScreenSpaceUV = normalizedScreenSpaceUV;
    AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(normalizedScreenSpaceUV);
    return EvaluateLoogaAdditionalBacklights(inputData, shadowMask, aoFactor,
        GetMeshRenderingLayer(), scatteringColor, scatterWidth, ambientScatterStrength,
        transmissionShadowSoftness, rimPower, distortion, normalWS, viewDirectionWS,
        transmissionMask, forceReceiveShadowsOff);
}

// ==============================================================================
// MASTER INDIRECT LIGHTING EVALUATION
// ==============================================================================
float3 EvaluateGlobalLoogaIndirect(float3 f0, float perceptualRoughness, float occlusion, float3 viewDirWS, float3 normalWS, float3 bentNormalWS, float NoV, float3 posWS, float2 uv, half4 modelParameters)
{
    perceptualRoughness = GetLoogaSafePerceptualRoughness(saturate(perceptualRoughness));

    #if defined(LOOGA_FIXED_LIGHTING_MODEL)
        #if LOOGA_FIXED_LIGHTING_MODEL == LOOGA_MODEL_CUSTOM
            return EvaluateIndirect_Configurable(f0, perceptualRoughness, occlusion, viewDirWS, normalWS, bentNormalWS, NoV, posWS, uv);
        #elif LOOGA_FIXED_LIGHTING_MODEL == LOOGA_MODEL_SOURCE2
            return EvaluateIndirect_Source2(f0, perceptualRoughness, occlusion, viewDirWS, normalWS, bentNormalWS, NoV, posWS, uv);
        #elif LOOGA_FIXED_LIGHTING_MODEL == LOOGA_MODEL_MINNAERT
            return EvaluateIndirect_Minnaert(f0, perceptualRoughness, occlusion, viewDirWS, normalWS, bentNormalWS, NoV, posWS, uv, modelParameters);
        #elif LOOGA_FIXED_LIGHTING_MODEL == LOOGA_MODEL_OVERWATCH
            return EvaluateIndirect_Overwatch(f0, perceptualRoughness, occlusion, viewDirWS, normalWS, bentNormalWS, NoV, posWS, uv);
        #elif LOOGA_FIXED_LIGHTING_MODEL == LOOGA_MODEL_OREN_NAYAR
            return EvaluateIndirect_OrenNayar(f0, perceptualRoughness, occlusion, viewDirWS, normalWS, bentNormalWS, NoV, posWS, uv, modelParameters);
        #elif LOOGA_FIXED_LIGHTING_MODEL == LOOGA_MODEL_ARKANE
            return EvaluateIndirect_Arkane(f0, perceptualRoughness, occlusion, viewDirWS, normalWS, bentNormalWS, NoV, posWS, uv);
        #else
            return EvaluateIndirect_DisneyBurley(f0, perceptualRoughness, occlusion, viewDirWS, normalWS, bentNormalWS, NoV, posWS, uv);
        #endif
    #else
        [branch] switch (_LoogaLightingModel)
        {
        case LOOGA_MODEL_CUSTOM:       return EvaluateIndirect_Configurable(f0, perceptualRoughness, occlusion, viewDirWS, normalWS, bentNormalWS, NoV, posWS, uv);
        case LOOGA_MODEL_SOURCE2:      return EvaluateIndirect_Source2(f0, perceptualRoughness, occlusion, viewDirWS, normalWS, bentNormalWS, NoV, posWS, uv);
        case LOOGA_MODEL_MINNAERT:     return EvaluateIndirect_Minnaert(f0, perceptualRoughness, occlusion, viewDirWS, normalWS, bentNormalWS, NoV, posWS, uv, modelParameters);
        case LOOGA_MODEL_OVERWATCH:    return EvaluateIndirect_Overwatch(f0, perceptualRoughness, occlusion, viewDirWS, normalWS, bentNormalWS, NoV, posWS, uv);
        case LOOGA_MODEL_OREN_NAYAR:   return EvaluateIndirect_OrenNayar(f0, perceptualRoughness, occlusion, viewDirWS, normalWS, bentNormalWS, NoV, posWS, uv, modelParameters);
        case LOOGA_MODEL_ARKANE:       return EvaluateIndirect_Arkane(f0, perceptualRoughness, occlusion, viewDirWS, normalWS, bentNormalWS, NoV, posWS, uv);
        default:                       return EvaluateIndirect_DisneyBurley(f0, perceptualRoughness, occlusion, viewDirWS, normalWS, bentNormalWS, NoV, posWS, uv);
        }
    #endif
}

#endif
