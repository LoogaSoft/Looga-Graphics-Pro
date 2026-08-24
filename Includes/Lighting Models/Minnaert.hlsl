#ifndef LOOGA_MODEL_MINNAERT_INCLUDED
#define LOOGA_MODEL_MINNAERT_INCLUDED

#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaLightingHelpers.hlsl"

// Minnaert diffuse law with an explicit material k coefficient. Surface
// roughness remains reserved for the independent specular lobe.
float3 EvaluateLighting_Minnaert(float3 diffuseColor, float3 f0, float perceptualRoughness, float3 normalWS, float occlusion, float3 viewDirectionWS, float NoV, float3 lightDir, float3 lightColor, half4 modelParameters)
{
    float roughness = perceptualRoughness * perceptualRoughness;
    float NoL = saturate(dot(normalWS, lightDir));
    float3 H = SafeNormalize(lightDir + viewDirectionWS);
    float NoH = saturate(dot(normalWS, H));
    float VoH = saturate(dot(viewDirectionWS, H));

    float k = DecodeLoogaMinnaertK(modelParameters);
    float minnaertTerm = pow(max(NoL, 1e-4), k) * pow(max(NoV, 1e-4), k - 1.0);
    float3 diffuse = (diffuseColor / PI) * minnaertTerm;

    float3 specular = EvaluateLoogaDirectBeckmannSpecular(f0, roughness, NoL, NoV, NoH, VoH);

    return (diffuse + specular * NoL) * lightColor * PI * GetLoogaGTBNDirectOcclusion(occlusion);
}

float3 EvaluateIndirect_Minnaert(float3 f0, float perceptualRoughness, float occlusion, float3 viewDirWS, float3 normalWS, float3 bentNormalWS, float NoV, float3 posWS, float2 uv, half4 modelParameters)
{
    float grazingVisibility = pow(max(NoV, 1e-3), lerp(0.0, 0.35, perceptualRoughness));
    return EvaluateLoogaSelectableIndirect(DecodeLoogaIndirectSpecularModel(modelParameters), f0, perceptualRoughness, occlusion * grazingVisibility, viewDirWS, bentNormalWS, NoV, posWS, uv);
}
#endif
