#ifndef LOOGA_MODEL_CONFIGURABLE_INCLUDED
#define LOOGA_MODEL_CONFIGURABLE_INCLUDED

#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaLightingHelpers.hlsl"

float LoogaProfileBurleyDiffuse(
    float NoL, float NoV, float LoH, float perceptualRoughness)
{
    float fd90 = 0.5 + 2.0 * perceptualRoughness * LoH * LoH;
    float lightScatter = 1.0 + (fd90 - 1.0) * SchlickFresnel(NoL);
    float viewScatter = 1.0 + (fd90 - 1.0) * SchlickFresnel(NoV);
    return NoL * lightScatter * viewScatter;
}

float2 LoogaProfileOrenNayarDiffuse(
    float3 normalWS, float3 viewDirectionWS, float3 lightDir,
    float NoL, float NoV)
{
    float sigma = radians(_LoogaProfileOrenNayarSigma);
    float sigma2 = sigma * sigma;
    float thetaI = acos(NoL);
    float thetaR = acos(NoV);
    float alpha = max(thetaI, thetaR);
    float beta = min(thetaI, thetaR);
    float sinI = sqrt(saturate(1.0 - NoL * NoL));
    float sinR = sqrt(saturate(1.0 - NoV * NoV));
    float3 lightProjection = lightDir - normalWS * NoL;
    float3 viewProjection = viewDirectionWS - normalWS * NoV;
    float cosPhiDifference = 0.0;

    if (sinI > 1e-4 && sinR > 1e-4)
        cosPhiDifference = dot(normalize(lightProjection), normalize(viewProjection));

    float c1 = 1.0 - 0.5 * sigma2 / (sigma2 + 0.33);
    float c2Base = 0.45 * sigma2 / (sigma2 + 0.09);
    float c2 = c2Base * (cosPhiDifference >= 0.0
        ? sin(alpha)
        : sin(alpha) - pow(2.0 * beta / PI, 3.0));
    float c3 = 0.125 * sigma2 / (sigma2 + 0.09) *
        pow(4.0 * alpha * beta / (PI * PI), 2.0);
    float tanBeta = sin(beta) / max(cos(beta), 1e-4);
    float halfAlphaBeta = (alpha + beta) * 0.5;
    float tanHalfAlphaBeta =
        sin(halfAlphaBeta) / max(cos(halfAlphaBeta), 1e-4);
    float l1 = c1 + c2 * cosPhiDifference * tanBeta +
        c3 * (1.0 - abs(cosPhiDifference)) * tanHalfAlphaBeta;
    float l2 = 0.17 * sigma2 / (sigma2 + 0.13) *
        (1.0 - cosPhiDifference * pow(2.0 * beta / PI, 2.0));
    return NoL * max(float2(l1, l2), 0.0);
}

float LoogaProfileDiffuseResponse(
    float3 normalWS, float3 viewDirectionWS, float3 lightDir,
    float perceptualRoughness, float NoL, float NoV, float LoH)
{
    if (_LoogaProfileDiffuseModel == LOOGA_DIFFUSE_DISNEY_BURLEY)
        return LoogaProfileBurleyDiffuse(NoL, NoV, LoH, perceptualRoughness);

    if (_LoogaProfileDiffuseModel == LOOGA_DIFFUSE_MINNAERT)
    {
        return pow(max(NoL, 1e-4), _LoogaProfileMinnaertK) *
            pow(max(NoV, 1e-4), _LoogaProfileMinnaertK - 1.0);
    }

    if (_LoogaProfileDiffuseModel == LOOGA_DIFFUSE_OREN_NAYAR)
    {
        return LoogaProfileOrenNayarDiffuse(
            normalWS, viewDirectionWS, lightDir, NoL, NoV).x;
    }

    if (_LoogaProfileDiffuseModel == LOOGA_DIFFUSE_WRAPPED)
    {
        float unclampedNoL = dot(normalWS, lightDir);
        float wrap = _LoogaProfileDiffuseWrap;
        return saturate((unclampedNoL + wrap) /
            ((1.0 + wrap) * (1.0 + wrap)));
    }

    if (_LoogaProfileDiffuseModel == LOOGA_DIFFUSE_BANDED)
    {
        float bandCount = max(_LoogaProfileBandCount, 1.0);
        float scaled = NoL * bandCount;
        float banded = (floor(scaled) + smoothstep(
            0.0, max(_LoogaProfileBandFeather, 0.001), frac(scaled))) /
            bandCount;
        return lerp(banded, NoL, _LoogaProfileBandBlend);
    }

    return NoL;
}

float3 LoogaProfilePhongSpecular(
    float3 f0, float perceptualRoughness, float NoL, float NoV,
    float NoH, float VoH)
{
    float exponent = exp2(lerp(11.0, 1.0, perceptualRoughness));
    float distribution = (exponent + 2.0) * pow(NoH, exponent) / (2.0 * PI);
    float3 fresnel = FresnelSchlick(f0, VoH);
    return fresnel * distribution / max(4.0 * NoL * NoV, 1e-5);
}

float3 LoogaProfileDirectSpecular(
    float3 f0, float perceptualRoughness, float NoL, float NoV,
    float NoH, float VoH)
{
    if (_LoogaProfileDirectSpecularModel == LOOGA_SPECULAR_BECKMANN)
    {
        float roughness = perceptualRoughness * perceptualRoughness;
        return EvaluateLoogaDirectBeckmannSpecular(
            f0, roughness, NoL, NoV, NoH, VoH);
    }

    if (_LoogaProfileDirectSpecularModel == LOOGA_SPECULAR_PHONG)
    {
        return LoogaProfilePhongSpecular(
            f0, perceptualRoughness, NoL, NoV, NoH, VoH);
    }

    return EvaluateLoogaBurleyMatchedGGXSpecular(
        f0, perceptualRoughness, NoL, NoV, NoH, VoH);
}

float3 EvaluateLighting_Configurable(
    float3 diffuseColor, float3 f0, float perceptualRoughness,
    float3 normalWS, float occlusion, float3 viewDirectionWS, float NoV,
    float3 lightDir, float3 lightColor)
{
    float directRoughness = GetLoogaSafePerceptualRoughness(saturate(
        perceptualRoughness * _LoogaProfileDirectRoughnessScale +
        _LoogaProfileDirectRoughnessBias));
    float NoL = saturate(dot(normalWS, lightDir));
    float3 halfVector = SafeNormalize(lightDir + viewDirectionWS);
    float NoH = saturate(dot(normalWS, halfVector));
    float LoH = saturate(dot(lightDir, halfVector));
    float VoH = saturate(dot(viewDirectionWS, halfVector));
    float diffuseResponse = LoogaProfileDiffuseResponse(
        normalWS, viewDirectionWS, lightDir, directRoughness,
        NoL, NoV, LoH);
    float3 diffuse = diffuseColor / PI * diffuseResponse *
        _LoogaProfileDiffuseStrength;
    if (_LoogaProfileDiffuseModel == LOOGA_DIFFUSE_OREN_NAYAR)
    {
        float orenNayarInterreflection = LoogaProfileOrenNayarDiffuse(
            normalWS, viewDirectionWS, lightDir, NoL, NoV).y;
        diffuse += diffuseColor * diffuseColor / PI *
            orenNayarInterreflection * _LoogaProfileDiffuseStrength;
    }

    float3 primarySpecular = LoogaProfileDirectSpecular(
        f0, directRoughness, NoL, NoV, NoH, VoH);
    float linearRoughness = directRoughness * directRoughness;
    float broadLinearRoughness = saturate(linearRoughness +
        _LoogaProfileSecondaryRoughnessSpread * (1.0 - linearRoughness));
    float3 secondarySpecular = LoogaProfileDirectSpecular(
        f0, sqrt(broadLinearRoughness), NoL, NoV, NoH, VoH);
    float3 specular = lerp(primarySpecular, secondarySpecular,
        _LoogaProfileSecondarySpecularWeight);
    float specularLevel = max(specular.r, max(specular.g, specular.b));
    float shapedLevel = lerp(_LoogaProfileHighlightShapeFloor, 1.0,
        smoothstep(_LoogaProfileHighlightShapeStart,
            _LoogaProfileHighlightShapeEnd, specularLevel));
    specular *= lerp(1.0, shapedLevel, _LoogaProfileHighlightShapeStrength);
    specular *= NoL * _LoogaProfileDirectSpecularStrength;

    return (diffuse + specular) * lightColor * PI *
        GetLoogaGTBNDirectOcclusion(occlusion);
}

float LoogaProfileSource2SpecularOcclusion(
    float NoV, float occlusion, float perceptualRoughness,
    float3 reflectVector, float3 bentNormalWS)
{
    float roughness = perceptualRoughness * perceptualRoughness;
    float visibility = saturate(pow(abs(NoV + occlusion),
        exp2(-16.0 * roughness - 1.0)) - 1.0 + occlusion);
    float bentNormalOcclusion = saturate(dot(reflectVector, bentNormalWS));
    return lerp(bentNormalOcclusion, visibility, perceptualRoughness);
}

float3 EvaluateIndirect_Configurable(
    float3 f0, float perceptualRoughness, float occlusion,
    float3 viewDirWS, float3 normalWS, float3 bentNormalWS, float NoV,
    float3 posWS, float2 uv)
{
    float indirectRoughness = GetLoogaSafePerceptualRoughness(saturate(
        perceptualRoughness * _LoogaProfileIndirectRoughnessScale +
        _LoogaProfileIndirectRoughnessBias));
    float grazingVisibility = pow(max(NoV, 1e-3),
        _LoogaProfileGrazingOcclusionStrength * indirectRoughness);
    float adjustedOcclusion = occlusion * grazingVisibility;
    float edgeVisibility = 1.0 - _LoogaProfileEdgeOcclusionStrength *
        (1.0 - smoothstep(_LoogaProfileEdgeOcclusionStart,
            _LoogaProfileEdgeOcclusionEnd, NoV));
    float specularOcclusion = edgeVisibility;

    if (_LoogaProfileSpecularOcclusionModel ==
        LOOGA_SPECULAR_OCCLUSION_SOURCE2)
    {
        float3 reflectVector = reflect(-viewDirWS, bentNormalWS);
        specularOcclusion *= LoogaProfileSource2SpecularOcclusion(
            NoV, adjustedOcclusion, indirectRoughness,
            reflectVector, bentNormalWS);
        adjustedOcclusion = 1.0;
    }

    float3 result;
    if (_LoogaProfileIndirectSpecularModel == LOOGA_SPECULAR_BECKMANN)
    {
        result = EvaluateLoogaBeckmannIndirect(
            f0, indirectRoughness, adjustedOcclusion, viewDirWS,
            bentNormalWS, NoV, posWS, uv) * specularOcclusion;
    }
    else if (_LoogaProfileIndirectSpecularModel == LOOGA_SPECULAR_PHONG)
    {
        float exponent = exp2(lerp(11.0, 1.0, indirectRoughness));
        result = EvaluateLoogaPhongIndirect(
            f0, exponent, adjustedOcclusion, viewDirWS, bentNormalWS,
            NoV, posWS, uv, specularOcclusion);
    }
    else
    {
        result = EvaluateLoogaGGXIndirect(
            f0, indirectRoughness, adjustedOcclusion, viewDirWS,
            bentNormalWS, NoV, posWS, uv,
            _LoogaProfileIndirectFresnelPower, specularOcclusion);
    }

    return result * _LoogaProfileIndirectSpecularStrength;
}

#endif
