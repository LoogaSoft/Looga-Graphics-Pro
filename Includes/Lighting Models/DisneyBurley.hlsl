#ifndef LOOGA_MODEL_DISNEYBURLEY_INCLUDED
#define LOOGA_MODEL_DISNEYBURLEY_INCLUDED

#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaLightingHelpers.hlsl"

// Disney's empirical diffuse and GGX specular model from
// "Physically Based Shading at Disney".
float FD90(float roughness, float LoH) { return 0.5 + (2.0 * roughness * LoH * LoH); }

float3 EvaluateDisneySpecular(float3 f0, float perceptualRoughness, float NoL, float NoV, float NoH, float VoH)
{
    return EvaluateLoogaBurleyMatchedGGXSpecular(
        f0, perceptualRoughness, NoL, NoV, NoH, VoH);
}

float3 EvaluateLighting_DisneyBurley(float3 diffuseColor, float3 f0, float perceptualRoughness, float3 normalWS, float occlusion, float3 viewDirectionWS, float NoV, float3 lightDir, float3 lightColor)
{
    float NoL = saturate(dot(normalWS, lightDir));
    float3 H = SafeNormalize(lightDir + viewDirectionWS);
    float NoH = saturate(dot(normalWS, H));
    float LoH = saturate(dot(lightDir, H));
    float VoH = saturate(dot(viewDirectionWS, H));

    float3 diffuse = (diffuseColor / PI) * (1.0 + (FD90(perceptualRoughness, LoH) - 1.0) * SchlickFresnel(NoL)) * (1.0 + (FD90(perceptualRoughness, LoH) - 1.0) * SchlickFresnel(NoV));
    float3 specular = EvaluateDisneySpecular(f0, perceptualRoughness, NoL, NoV, NoH, VoH);

    return (diffuse + specular) * lightColor * NoL * PI * GetLoogaGTBNDirectOcclusion(occlusion);
}

float3 EvaluateIndirect_DisneyBurley(float3 f0, float perceptualRoughness, float occlusion, float3 viewDirWS, float3 normalWS, float3 bentNormalWS, float NoV, float3 posWS, float2 uv)
{
    return EvaluateLoogaGGXIndirect(f0, perceptualRoughness, occlusion, viewDirWS, bentNormalWS, NoV, posWS, uv, 5.0, 1.0);
}
#endif
