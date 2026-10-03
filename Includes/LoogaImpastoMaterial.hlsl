#ifndef LOOGA_IMPASTO_MATERIAL_INCLUDED
#define LOOGA_IMPASTO_MATERIAL_INCLUDED

// Material impasto layer. Include after the UnityPerMaterial buffer that declares LOOGA_IMPASTO_CBUFFER_FIELDS.
// Strokes are anchored to the object, so they move with it, and keep their world size on scaled objects.
// Deep impasto (_LOOGA_IMPASTO_PARALLAX) marches the paint height so the paint occludes itself and shifts the
// material's textures, which a normal map alone cannot do at grazing angles.

#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaImpastoCommon.hlsl"

TEXTURE2D(_ImpastoStrokeMap);

#define LOOGA_IMPASTO_PARALLAX_STEPS 12

float3 LoogaImpastoObjectScale()
{
    float4x4 objectToWorld = GetObjectToWorldMatrix();
    return float3(
        length(objectToWorld._m00_m10_m20),
        length(objectToWorld._m01_m11_m21),
        length(objectToWorld._m02_m12_m22));
}

// Object-space position at world scale, in stroke tiles.
float3 LoogaImpastoStrokePosition(float3 positionWS)
{
    return TransformWorldToObject(positionWS) * LoogaImpastoObjectScale() / max(_ImpastoTileSize, 0.01);
}

float LoogaImpastoMaterialLod(float3 strokePosition)
{
    float3 dx = ddx(strokePosition);
    float3 dy = ddy(strokePosition);
    float texels = _ImpastoStrokeMap_TexelSize.z;
    float footprint = max(dot(dx, dx), dot(dy, dy)) * texels * texels;
    return max(0.5 * log2(max(footprint, 1e-8)), 0.0);
}

// Gradients of the mesh UV along the surface (cotangent frame), so a surface offset converts to a UV offset
// without relying on mesh tangents. Rows are the gradients of u and v.
float2x3 LoogaImpastoUvGradients(float3 positionWS, float2 uv, float3 normalWS)
{
    float3 dp1 = ddx(positionWS);
    float3 dp2 = ddy(positionWS);
    float2 duv1 = ddx(uv);
    float2 duv2 = ddy(uv);
    float3 dp2perp = cross(dp2, normalWS);
    float3 dp1perp = cross(normalWS, dp1);
    float determinant = dot(dp1, dp2perp);
    if (abs(determinant) < 1e-12)
        return (float2x3)0;
    float3 gradientU = (dp2perp * duv1.x + dp1perp * duv2.x) / determinant;
    float3 gradientV = (dp2perp * duv1.y + dp1perp * duv2.y) / determinant;
    return float2x3(gradientU, gradientV);
}

// Paint height at a point, from the projection along the surface's main object axis.
float LoogaImpastoHeight(float3 positionWS, int axis, float lod)
{
    float3 strokePosition = LoogaImpastoStrokePosition(positionWS);
    float2 uv = axis == 0 ? strokePosition.zy : (axis == 1 ? strokePosition.xz : strokePosition.xy);
    return LoogaImpastoProjection(TEXTURE2D_ARGS(_ImpastoStrokeMap, sampler_LinearRepeat), uv, lod,
        _ImpastoCellsPerTile, _ImpastoRotation, _ImpastoOverlap).w;
}

// World offset along the surface to where the view ray meets the paint. The paint top lies on the surface and
// its thinnest parts _ImpastoParallaxDepth below it.
float3 LoogaImpastoParallaxOffset(float3 positionWS, float3 geometricNormalWS, float lod)
{
    float3 viewWS = normalize(GetCameraPositionWS() - positionWS);
    float viewDotNormal = dot(viewWS, geometricNormalWS);
    if (viewDotNormal <= 0.05 || _ImpastoParallaxDepth <= 0.0)
        return 0.0;

    // Offset along the surface per unit of paint depth.
    float3 slope = -(viewWS - geometricNormalWS * viewDotNormal) / viewDotNormal * _ImpastoParallaxDepth;
    float3 normalOS = abs(TransformWorldToObjectNormal(geometricNormalWS));
    int axis = normalOS.x > normalOS.y ? (normalOS.x > normalOS.z ? 0 : 2) : (normalOS.y > normalOS.z ? 1 : 2);

    float layerStep = 1.0 / LOOGA_IMPASTO_PARALLAX_STEPS;
    float layer = 1.0;
    float3 offset = 0.0;
    float height = LoogaImpastoHeight(positionWS, axis, lod);
    float previousLayer = layer;
    float previousHeight = height;
    float3 previousOffset = offset;
    [loop]
    for (int step = 0; step < LOOGA_IMPASTO_PARALLAX_STEPS && height < layer; step++)
    {
        previousLayer = layer;
        previousHeight = height;
        previousOffset = offset;
        layer -= layerStep;
        offset = slope * (1.0 - layer);
        height = LoogaImpastoHeight(positionWS + offset, axis, lod);
    }

    // Linear refinement between the last two layers.
    float after = height - layer;
    float before = previousHeight - previousLayer;
    float weight = after / max(after - before, 1e-5);
    return lerp(offset, previousOffset, saturate(weight));
}

// Paints the material: the paint replaces fine detail of the material normal, keeps the large shape of the
// geometric normal, darkens cavities and adds gloss to ridges.
void LoogaApplyImpasto(float3 positionWS, float3 geometricNormalWS, float lod, inout half3 normalWS,
    inout half occlusion, inout half smoothness)
{
    float3 geometricOS = TransformWorldToObjectNormal(geometricNormalWS);
    float3 baseWS = normalize(lerp(float3(normalWS), geometricNormalWS, _ImpastoDetailReplacement));
    float4 painted = LoogaImpastoTriplanar(
        TEXTURE2D_ARGS(_ImpastoStrokeMap, sampler_LinearRepeat),
        LoogaImpastoStrokePosition(positionWS),
        TransformWorldToObjectNormal(baseWS),
        LoogaImpastoAxisWeights(geometricOS, _ImpastoSharpness),
        lod,
        _ImpastoStrength,
        _ImpastoCellsPerTile,
        _ImpastoRotation,
        _ImpastoOverlap);
    normalWS = half3(TransformObjectToWorldNormal(painted.xyz));
    occlusion *= half(LoogaImpastoCavity(painted.w, _ImpastoCavity));
    smoothness = half(LoogaImpastoSmoothness(smoothness, painted.w, _ImpastoRidgeGloss));
}

#endif
