#ifndef LOOGA_MODEL_ORENNAYAR_INCLUDED
#define LOOGA_MODEL_ORENNAYAR_INCLUDED

#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaLightingHelpers.hlsl"

// Full Oren-Nayar rough diffuse coefficient form. Looga keeps its standard GGX
// specular lobe separate because the original model describes diffuse response.
float3 EvaluateLighting_OrenNayar(float3 diffuseColor, float3 f0, float perceptualRoughness, float3 normalWS, float occlusion, float3 viewDirectionWS, float NoV, float3 lightDir, float3 lightColor, half4 modelParameters)
{
    float sigma = DecodeLoogaOrenNayarSigma(modelParameters);
    float sigma2 = sigma * sigma;
    float NoL = saturate(dot(normalWS, lightDir));
    float3 H = SafeNormalize(lightDir + viewDirectionWS);
    float NoH = saturate(dot(normalWS, H));
    float VoH = saturate(dot(viewDirectionWS, H));

    float thetaI = acos(NoL);
    float thetaR = acos(NoV);
    float alpha = max(thetaI, thetaR);
    float beta = min(thetaI, thetaR);

    float sinI = sqrt(saturate(1.0 - NoL * NoL));
    float sinR = sqrt(saturate(1.0 - NoV * NoV));
    float3 lightProj = lightDir - normalWS * NoL;
    float3 viewProj = viewDirectionWS - normalWS * NoV;
    float cosPhiDiff = 0.0;

    if (sinI > 1e-4 && sinR > 1e-4)
        cosPhiDiff = dot(normalize(lightProj), normalize(viewProj));

    float C1 = 1.0 - 0.5 * (sigma2 / (sigma2 + 0.33));
    float C2Base = 0.45 * (sigma2 / (sigma2 + 0.09));
    float C2 = C2Base * (cosPhiDiff >= 0.0 ? sin(alpha) : sin(alpha) - pow(2.0 * beta / PI, 3.0));
    float C3 = 0.125 * (sigma2 / (sigma2 + 0.09)) * pow(4.0 * alpha * beta / (PI * PI), 2.0);
    float tanBeta = sin(beta) / max(cos(beta), 1e-4);
    float halfAlphaBeta = (alpha + beta) * 0.5;
    float tanHalfAlphaBeta = sin(halfAlphaBeta) / max(cos(halfAlphaBeta), 1e-4);

    float L1 = C1 + C2 * cosPhiDiff * tanBeta + C3 * (1.0 - abs(cosPhiDiff)) * tanHalfAlphaBeta;
    float3 L2 = 0.17 * diffuseColor * (sigma2 / (sigma2 + 0.13)) * (1.0 - cosPhiDiff * pow(2.0 * beta / PI, 2.0));
    float3 diffuse = (diffuseColor / PI) * L1 + (diffuseColor / PI) * L2;

    float3 specular = EvaluateLoogaBurleyMatchedGGXSpecular(
        f0, perceptualRoughness, NoL, NoV, NoH, VoH);

    return (diffuse + specular) * lightColor * NoL * PI * GetLoogaGTBNDirectOcclusion(occlusion);
}

float3 EvaluateIndirect_OrenNayar(float3 f0, float perceptualRoughness, float occlusion, float3 viewDirWS, float3 normalWS, float3 bentNormalWS, float NoV, float3 posWS, float2 uv, half4 modelParameters)
{
    return EvaluateLoogaSelectableIndirect(DecodeLoogaIndirectSpecularModel(modelParameters), f0, perceptualRoughness, occlusion, viewDirWS, bentNormalWS, NoV, posWS, uv);
}
#endif
