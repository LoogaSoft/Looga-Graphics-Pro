#ifndef LOOGA_MODEL_ARKANE_INCLUDED
#define LOOGA_MODEL_ARKANE_INCLUDED

#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaLightingHelpers.hlsl"

// Arkane-inspired banded diffuse response. This intentionally captures a
// stylized art target; it is not a published physical BRDF.
float3 EvaluateLighting_Arkane(float3 diffuseColor, float3 f0, float perceptualRoughness, float3 normalWS, float occlusion, float3 viewDirectionWS, float NoV, float3 lightDir, float3 lightColor, half4 modelParameters)
{
    float NoL_Unclamped = dot(normalWS, lightDir);
    float NoL = saturate(NoL_Unclamped);
    float3 H = SafeNormalize(lightDir + viewDirectionWS);
    float NoH = saturate(dot(normalWS, H));
    float VoH = saturate(dot(viewDirectionWS, H));

    float bands = DecodeLoogaArkaneBandCount(modelParameters);
    float feather = max(DecodeLoogaArkaneBandFeather(modelParameters), 0.001);
    float bandScale = NoL * bands;
    float bandedNoL = (floor(bandScale) + smoothstep(0.0, feather, frac(bandScale))) / bands;
    bandedNoL = lerp(bandedNoL, NoL, 0.1);

    float3 diffuse = (diffuseColor / PI) * bandedNoL;
    float3 rawSpecular = EvaluateLoogaBurleyMatchedGGXSpecular(
        f0, perceptualRoughness, NoL, NoV, NoH, VoH);
    float specularLevel = max(max(rawSpecular.r, rawSpecular.g), rawSpecular.b);
    float specularShape = lerp(0.72, 1.0, smoothstep(0.015, 0.18, specularLevel));
    float3 specular = rawSpecular * specularShape;

    float3 finalDirectLight = diffuse + (specular * NoL);

    return finalDirectLight * lightColor * PI * GetLoogaGTBNDirectOcclusion(occlusion);
}

float3 EvaluateIndirect_Arkane(float3 f0, float perceptualRoughness, float occlusion, float3 viewDirWS, float3 normalWS, float3 bentNormalWS, float NoV, float3 posWS, float2 uv)
{
    float envRoughness = saturate(perceptualRoughness * 1.08 + 0.02);
    float edgeControl = lerp(0.9, 1.0, smoothstep(0.2, 0.75, NoV));
    return EvaluateLoogaGGXIndirect(f0, envRoughness, occlusion, viewDirWS, bentNormalWS, NoV, posWS, uv, 4.0, edgeControl);
}
#endif
