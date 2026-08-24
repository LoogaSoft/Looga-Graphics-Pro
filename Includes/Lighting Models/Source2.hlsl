#ifndef LOOGA_MODEL_SOURCE2_INCLUDED
#define LOOGA_MODEL_SOURCE2_INCLUDED

#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaLightingHelpers.hlsl"

// Source 2-inspired PBR path. Public Source 2 material behavior is approximated
// here with Lambert diffuse, GGX specular, and bent-normal specular occlusion.
float GetS2SpecularOcclusion(float NoV, float occlusion, float perceptualRoughness, float3 reflectVector, float3 bentNormalWS)
{
    float roughness = perceptualRoughness * perceptualRoughness;
    float visibility = saturate(pow(abs(NoV + occlusion), exp2(-16.0 * roughness - 1.0)) - 1.0 + occlusion);
    float bentNormalOcclusion = saturate(dot(reflectVector, bentNormalWS));
    return lerp(bentNormalOcclusion, visibility, perceptualRoughness);
}

float3 EvaluateLighting_Source2(float3 diffuseColor, float3 f0, float perceptualRoughness, float3 normalWS, float occlusion, float3 viewDirectionWS, float NoV, float3 lightDir, float3 lightColor)
{
    float NoL = saturate(dot(normalWS, lightDir));
    float3 H = SafeNormalize(lightDir + viewDirectionWS);
    float NoH = saturate(dot(normalWS, H));
    float VoH = saturate(dot(viewDirectionWS, H));

    float3 diffuse = diffuseColor / PI;
    float3 specular = EvaluateLoogaBurleyMatchedGGXSpecular(
        f0, perceptualRoughness, NoL, NoV, NoH, VoH);

    return (diffuse + specular) * lightColor * NoL * PI * GetLoogaGTBNDirectOcclusion(occlusion);
}

float3 EvaluateIndirect_Source2(float3 f0, float perceptualRoughness, float occlusion, float3 viewDirWS, float3 normalWS, float3 bentNormalWS, float NoV, float3 posWS, float2 uv)
{
    half3 reflectVector = reflect(-viewDirWS, bentNormalWS);
    float specOcc = GetS2SpecularOcclusion(NoV, occlusion, perceptualRoughness, reflectVector, bentNormalWS);
    return EvaluateLoogaGGXIndirect(f0, perceptualRoughness, 1.0, viewDirWS, bentNormalWS, NoV, posWS, uv, 5.0, specOcc);
}
#endif
