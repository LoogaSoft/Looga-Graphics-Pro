#ifndef LOOGA_RUNTIME_VIRTUAL_TEXTURE_INCLUDED
#define LOOGA_RUNTIME_VIRTUAL_TEXTURE_INCLUDED

#define LOOGA_RVT_MAX_CLIPMAPS 4

TEXTURE2D(_LoogaRvtAlbedoAtlas);
SAMPLER(sampler_LoogaRvtAlbedoAtlas);
TEXTURE2D(_LoogaRvtNormalAtlas);
SAMPLER(sampler_LoogaRvtNormalAtlas);
TEXTURE2D(_LoogaRvtHeightAtlas);
SAMPLER(sampler_LoogaRvtHeightAtlas);

int _LoogaRvtEnabled;
int _LoogaRvtClipmapCount;
float4 _LoogaRvtCenterExtents[LOOGA_RVT_MAX_CLIPMAPS];
float4 _LoogaRvtAtlasRects[LOOGA_RVT_MAX_CLIPMAPS];
float4 _LoogaRvtHeightRange;

struct LoogaRuntimeVirtualTextureSample
{
    half3 albedo;
    half3 normalWS;
    half smoothness;
    half metallic;
    half mask;
    half coverage;
    float height;
    int clipmapLevel;
};

half3 LoogaRvtDecodeOctahedralNormal(half2 encoded)
{
    half2 value = encoded * 2.0h - 1.0h;
    half3 normal = half3(value.x, 1.0h - abs(value.x) - abs(value.y), value.y);
    half correction = saturate(-normal.y);
    normal.x += normal.x >= 0.0h ? -correction : correction;
    normal.z += normal.z >= 0.0h ? -correction : correction;
    return normalize(normal);
}

float LoogaRvtDecodeHeight(half2 encoded)
{
    float normalizedHeight = dot(encoded, float2(1.0, 1.0 / 255.0));
    return lerp(_LoogaRvtHeightRange.x, _LoogaRvtHeightRange.y, normalizedHeight);
}

int LoogaRvtSelectClipmap(float2 worldPositionXZ)
{
    UNITY_UNROLL
    for (int level = 0; level < LOOGA_RVT_MAX_CLIPMAPS; level++)
    {
        if (level >= _LoogaRvtClipmapCount)
            break;

        float4 clipmap = _LoogaRvtCenterExtents[level];
        float2 delta = abs(worldPositionXZ - clipmap.xy);
        if (max(delta.x, delta.y) <= clipmap.z)
            return level;
    }

    return -1;
}

float2 LoogaRvtGetAtlasUv(float2 worldPositionXZ, int level)
{
    float4 clipmap = _LoogaRvtCenterExtents[level];
    float4 atlasRect = _LoogaRvtAtlasRects[level];
    float2 localUv = (worldPositionXZ - clipmap.xy) / (clipmap.z * 2.0) + 0.5;
    return atlasRect.xy + saturate(localUv) * atlasRect.zw;
}

LoogaRuntimeVirtualTextureSample LoogaRvtSampleLevel(float3 positionWS, int level)
{
    LoogaRuntimeVirtualTextureSample sample = (LoogaRuntimeVirtualTextureSample)0;
    sample.normalWS = half3(0.0h, 1.0h, 0.0h);
    sample.clipmapLevel = level;
    if (_LoogaRvtEnabled == 0 || level < 0 || level >= _LoogaRvtClipmapCount)
        return sample;

    float2 atlasUv = LoogaRvtGetAtlasUv(positionWS.xz, level);
    half4 albedoCoverage = SAMPLE_TEXTURE2D_LOD(
        _LoogaRvtAlbedoAtlas,
        sampler_LoogaRvtAlbedoAtlas,
        atlasUv,
        0.0);
    half4 normalMaterial = SAMPLE_TEXTURE2D_LOD(
        _LoogaRvtNormalAtlas,
        sampler_LoogaRvtNormalAtlas,
        atlasUv,
        0.0);
    half4 heightMask = SAMPLE_TEXTURE2D_LOD(
        _LoogaRvtHeightAtlas,
        sampler_LoogaRvtHeightAtlas,
        atlasUv,
        0.0);

    sample.albedo = albedoCoverage.rgb;
    sample.normalWS = LoogaRvtDecodeOctahedralNormal(normalMaterial.rg);
    sample.smoothness = normalMaterial.b;
    sample.metallic = normalMaterial.a;
    sample.height = LoogaRvtDecodeHeight(heightMask.rg);
    sample.mask = heightMask.b;
    sample.coverage = min(albedoCoverage.a, heightMask.a);
    return sample;
}

LoogaRuntimeVirtualTextureSample LoogaRvtSample(float3 positionWS)
{
    int level = LoogaRvtSelectClipmap(positionWS.xz);
    LoogaRuntimeVirtualTextureSample fineSample = LoogaRvtSampleLevel(positionWS, level);
    if (level < 0 || level + 1 >= _LoogaRvtClipmapCount)
        return fineSample;

    float4 clipmap = _LoogaRvtCenterExtents[level];
    float2 normalizedDelta = abs(positionWS.xz - clipmap.xy) / clipmap.z;
    float edgeDistance = max(normalizedDelta.x, normalizedDelta.y);
    float blendStart = 1.0 - saturate(_LoogaRvtHeightRange.w);
    float blend = smoothstep(blendStart, 1.0, edgeDistance);
    if (blend <= 0.0)
        return fineSample;

    LoogaRuntimeVirtualTextureSample coarseSample = LoogaRvtSampleLevel(positionWS, level + 1);
    half validBlend = blend * coarseSample.coverage;
    fineSample.albedo = lerp(fineSample.albedo, coarseSample.albedo, validBlend);
    fineSample.normalWS = normalize(lerp(fineSample.normalWS, coarseSample.normalWS, validBlend));
    fineSample.smoothness = lerp(fineSample.smoothness, coarseSample.smoothness, validBlend);
    fineSample.metallic = lerp(fineSample.metallic, coarseSample.metallic, validBlend);
    fineSample.height = lerp(fineSample.height, coarseSample.height, validBlend);
    fineSample.mask = lerp(fineSample.mask, coarseSample.mask, validBlend);
    fineSample.coverage = max(fineSample.coverage, coarseSample.coverage * blend);
    return fineSample;
}

#endif
