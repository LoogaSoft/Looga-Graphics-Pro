#ifndef LOOGA_MODEL_OVERWATCH_INCLUDED
#define LOOGA_MODEL_OVERWATCH_INCLUDED

#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaLightingHelpers.hlsl"

// Overwatch-inspired stylized wrap diffuse. This is an approximation of the
// broad art-direction target rather than a public, exact Overwatch BRDF.
float3 EvaluateLighting_Overwatch(float3 diffuseColor, float3 f0, float perceptualRoughness, float3 normalWS, float occlusion, float3 viewDirectionWS, float NoV, float3 lightDir, float3 lightColor, half4 modelParameters)
{
    float roughness = perceptualRoughness * perceptualRoughness;
    float wrap = DecodeLoogaOverwatchWrap(modelParameters);
    float NoL_Unclamped = dot(normalWS, lightDir);
    float NoL_Wrapped = saturate((NoL_Unclamped + wrap) / ((1.0 + wrap) * (1.0 + wrap)));
    float3 diffuse = (diffuseColor / PI) * NoL_Wrapped;

    float NoL = saturate(NoL_Unclamped);
    float3 H = SafeNormalize(lightDir + viewDirectionWS);
    float NoH = saturate(dot(normalWS, H));
    float VoH = saturate(dot(viewDirectionWS, H));

    float3 primarySpecular = EvaluateLoogaBurleyMatchedGGXSpecular(
        f0, perceptualRoughness, NoL, NoV, NoH, VoH);
    float broadRoughness = saturate(roughness + 0.12 * (1.0 - roughness));
    float broadPerceptualRoughness = sqrt(broadRoughness);
    float3 broadSpecular = EvaluateLoogaBurleyMatchedGGXSpecular(
        f0, broadPerceptualRoughness, NoL, NoV, NoH, VoH);
    float3 specular = lerp(primarySpecular, broadSpecular, 0.2);

    float3 finalDirectLight = diffuse + (specular * NoL);

    return finalDirectLight * lightColor * PI * GetLoogaGTBNDirectOcclusion(occlusion);
}

float3 EvaluateIndirect_Overwatch(float3 f0, float perceptualRoughness, float occlusion, float3 viewDirWS, float3 normalWS, float3 bentNormalWS, float NoV, float3 posWS, float2 uv)
{
    float envRoughness = saturate(perceptualRoughness * 1.05 + 0.015);
    return EvaluateLoogaGGXIndirect(f0, envRoughness, occlusion, viewDirWS, bentNormalWS, NoV, posWS, uv, 5.0, 1.0);
}
#endif
