#ifndef LOOGA_STREAMING_VIRTUAL_TEXTURE_INCLUDED
#define LOOGA_STREAMING_VIRTUAL_TEXTURE_INCLUDED
TEXTURE2D(_LoogaSvtPageTable);
TEXTURE2D_ARRAY(_LoogaSvtAlbedo);
SAMPLER(sampler_LoogaSvtAlbedo);
TEXTURE2D_ARRAY(_LoogaSvtNormal);
SAMPLER(sampler_LoogaSvtNormal);
TEXTURE2D_ARRAY(_LoogaSvtMask);
SAMPLER(sampler_LoogaSvtMask);

struct LoogaSvtSurface
{
    float3 albedo;
    float3 normalTS;
    float4 mask;
};

float2 LoogaSvtUV(float2 uv, float4 layout)
{
    return layout.w > 0.5 ? frac(uv) : clamp(uv, 0, 0.999999);
}

float LoogaSvtLod(float2 uv, float4 layout, float derivativeScale)
{
    float2 dx = ddx(uv) * layout.x * derivativeScale;
    float2 dy = ddy(uv) * layout.x * derivativeScale;
    return clamp(0.5 * log2(max(max(dot(dx, dx), dot(dy, dy)), 1e-8)), 0, layout.z - 1);
}

uint LoogaSvtFeedback(float2 uv, float4 layout, uint id, float derivativeScale)
{
    if (id == 0) return 0;
    uint mip = (uint)floor(LoogaSvtLod(uv, layout, derivativeScale));
    uint size = max(1u, (uint)layout.x >> mip);
    uint side = max(1u, size / (uint)layout.y);
    uint2 page = min((uint2)(LoogaSvtUV(uv, layout) * size / layout.y), side - 1);
    return (id << 20) | (mip << 16) | (page.y << 8) | page.x;
}

LoogaSvtSurface LoogaSvtSampleMip(float2 uv, float4 layout, uint mip)
{
    uint baseSide = max(1u, (uint)layout.x / (uint)layout.y);
    uv = LoogaSvtUV(uv, layout);
    float slot = 0;
    uint size = 1;
    uint2 page = 0;
    for (uint level = mip; level < (uint)layout.z; level++)
    {
        uint row = 0;
        for (uint i = 0; i < level; i++)
        {
            row += max(1u, baseSide >> i);
        }
        size = max(1u, (uint)layout.x >> level);
        uint side = max(1u, size / (uint)layout.y);
        page = min((uint2)(uv * size / layout.y), side - 1);
        slot = LOAD_TEXTURE2D(_LoogaSvtPageTable, uint2(page.x, page.y + row)).r;
        if (slot >= 1) break;
    }
    float2 tileUV = (uv * size - page * layout.y + 4.0) / (layout.y + 8.0);
    float slice = max(0, slot - 1);
    LoogaSvtSurface result;
    result.albedo = SAMPLE_TEXTURE2D_ARRAY_LOD(_LoogaSvtAlbedo, sampler_LoogaSvtAlbedo, tileUV, slice, 0).rgb;
    result.normalTS = normalize(SAMPLE_TEXTURE2D_ARRAY_LOD(_LoogaSvtNormal, sampler_LoogaSvtNormal, tileUV, slice, 0).xyz * 2 - 1);
    result.mask = SAMPLE_TEXTURE2D_ARRAY_LOD(_LoogaSvtMask, sampler_LoogaSvtMask, tileUV, slice, 0);
    return result;
}

LoogaSvtSurface LoogaSvtSample(float2 uv, float4 layout, uint id)
{
    if (id == 0)
    {
        LoogaSvtSurface fallback;
        fallback.albedo = 1;
        fallback.normalTS = float3(0, 0, 1);
        fallback.mask = float4(0, 1, 0, 0.5);
        return fallback;
    }
    float lod = LoogaSvtLod(uv, layout, 1);
    LoogaSvtSurface a = LoogaSvtSampleMip(uv, layout, (uint)floor(lod));
    LoogaSvtSurface b = LoogaSvtSampleMip(uv, layout, min((uint)floor(lod) + 1, (uint)layout.z - 1));
    a.albedo = lerp(a.albedo, b.albedo, frac(lod));
    a.normalTS = normalize(lerp(a.normalTS, b.normalTS, frac(lod)));
    a.mask = lerp(a.mask, b.mask, frac(lod));
    return a;
}
#endif
