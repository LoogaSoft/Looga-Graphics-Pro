#ifndef LOOGA_IMPASTO_COMMON_INCLUDED
#define LOOGA_IMPASTO_COMMON_INCLUDED

// Stroke sampling shared by the Looga Impasto renderer feature and the material impasto layer.
// Stroke textures pack the tangent-space normal XY in RG ([0, 1]) and the paint height in B.

// Stencil bit that marks pixels a material already painted, written by the LoogaImpastoMask pass. The global
// pass leaves them alone. URP keeps bits 0 to 3 for users.
#define LOOGA_IMPASTO_STENCIL_BIT 8u

// Material impasto settings, declared in each shader's UnityPerMaterial buffer so every pass shares the layout.
#define LOOGA_IMPASTO_CBUFFER_FIELDS \
    float _ImpastoTileSize; \
    float _ImpastoStrength; \
    float _ImpastoDetailReplacement; \
    float _ImpastoSharpness; \
    float _ImpastoCellsPerTile; \
    float _ImpastoRotation; \
    float _ImpastoOverlap; \
    float _ImpastoCavity; \
    float _ImpastoRidgeGloss; \
    float _ImpastoParallaxDepth; \
    float4 _ImpastoStrokeMap_TexelSize

float2 LoogaImpastoHash2(int2 cell)
{
    uint2 bits = asuint(cell);
    uint hash = bits.x * 0x8DA6B343u ^ bits.y * 0xD8163841u;
    hash ^= hash >> 15;
    hash *= 0x2C1B3C6Du;
    hash ^= hash >> 12;
    uint second = hash * 0x297A2D39u;
    second ^= second >> 15;
    return float2(hash & 0xFFFF, second & 0xFFFF) / 65535.0;
}

// Triangle grid of the hex tiling (Mikkelsen, "Practical Real-Time Hex-Tiling"): barycentric weights of
// the three cells around a point.
void LoogaImpastoTriangleGrid(float2 uv, out float3 weights, out int2 cell0, out int2 cell1, out int2 cell2)
{
    uv *= 3.46410161;
    float2 skewed = float2(uv.x, -0.57735027 * uv.x + 1.15470054 * uv.y);
    int2 base = (int2)floor(skewed);
    float3 temp = float3(frac(skewed), 0.0);
    temp.z = 1.0 - temp.x - temp.y;
    float s = step(0.0, -temp.z);
    float s2 = 2.0 * s - 1.0;
    weights = float3(-temp.z * s2, s - temp.y * s2, s - temp.x * s2);
    cell0 = base + int2(s, s);
    cell1 = base + int2(s, 1.0 - s);
    cell2 = base + int2(1.0 - s, s);
}

// One planar projection of the strokes, in units of stroke tiles: xy tangent-space slope, z normal Z, w height.
// Each hex cell rotates and shifts the strokes at random, and overlapping cells keep the thicker paint, like
// strokes laid over strokes, instead of cross-fading into a repeating pattern.
// rotation: 0..1 share of a full turn each cell may rotate. overlap: 0..1 sharpness of the thicker-paint test.
float4 LoogaImpastoProjection(TEXTURE2D_PARAM(strokes, strokesSampler), float2 uv, float lod,
    float cellsPerTile, float rotation, float overlap)
{
    float3 weights;
    int2 cells[3];
    LoogaImpastoTriangleGrid(uv * cellsPerTile, weights, cells[0], cells[1], cells[2]);
    float2 slopes[3];
    float heights[3];
    [unroll]
    for (int i = 0; i < 3; i++)
    {
        float2 random = LoogaImpastoHash2(cells[i]);
        float angle = (random.x * 2.0 - 1.0) * PI * rotation;
        float sine, cosine;
        sincos(angle, sine, cosine);
        float2x2 turn = float2x2(cosine, -sine, sine, cosine);
        float4 texel = SAMPLE_TEXTURE2D_LOD(strokes, strokesSampler, mul(turn, uv) + random * 7.31, lod);
        // The strokes were rotated, so their slope turns back by the transposed rotation.
        slopes[i] = mul(transpose(turn), texel.rg * 2.0 - 1.0);
        heights[i] = texel.b;
    }

    float transition = lerp(0.6, 0.03, overlap);
    float3 thickness = float3(heights[0], heights[1], heights[2]) + weights;
    float3 blend = max(thickness - (max(thickness.x, max(thickness.y, thickness.z)) - transition), 0.0);
    blend /= max(blend.x + blend.y + blend.z, 0.0001);
    float2 slope = slopes[0] * blend.x + slopes[1] * blend.y + slopes[2] * blend.z;
    float height = heights[0] * blend.x + heights[1] * blend.y + heights[2] * blend.z;
    return float4(slope, sqrt(saturate(1.0 - dot(slope, slope))), height);
}

// Projection weights of the three axes for a geometric normal.
float3 LoogaImpastoAxisWeights(float3 geometricNormal, float sharpness)
{
    float3 weights = pow(abs(geometricNormal), sharpness);
    return weights / max(weights.x + weights.y + weights.z, 0.0001);
}

// Triplanar strokes laid onto baseNormal with a whiteout blend (Golus, "Normal Mapping for a Triplanar Shader").
// position, baseNormal and axis weights share one space; position is in stroke tiles. Returns the painted
// normal in xyz and the paint height in w.
float4 LoogaImpastoTriplanar(TEXTURE2D_PARAM(strokes, strokesSampler), float3 position, float3 baseNormal,
    float3 axisWeights, float lod, float strength, float cellsPerTile, float rotation, float overlap)
{
    float3 normal = 0.0;
    float height = 0.0;
    [branch] if (axisWeights.x > 0.01)
    {
        float4 s = LoogaImpastoProjection(TEXTURE2D_ARGS(strokes, strokesSampler), position.zy, lod, cellsPerTile,
            rotation, overlap);
        float2 slope = s.xy * strength;
        normal += float3(s.z * baseNormal.x, slope.y + baseNormal.y, slope.x + baseNormal.z) * axisWeights.x;
        height += s.w * axisWeights.x;
    }
    [branch] if (axisWeights.y > 0.01)
    {
        float4 s = LoogaImpastoProjection(TEXTURE2D_ARGS(strokes, strokesSampler), position.xz, lod, cellsPerTile,
            rotation, overlap);
        float2 slope = s.xy * strength;
        normal += float3(slope.x + baseNormal.x, s.z * baseNormal.y, slope.y + baseNormal.z) * axisWeights.y;
        height += s.w * axisWeights.y;
    }
    [branch] if (axisWeights.z > 0.01)
    {
        float4 s = LoogaImpastoProjection(TEXTURE2D_ARGS(strokes, strokesSampler), position.xy, lod, cellsPerTile,
            rotation, overlap);
        float2 slope = s.xy * strength;
        normal += float3(slope.x + baseNormal.x, slope.y + baseNormal.y, s.z * baseNormal.z) * axisWeights.z;
        height += s.w * axisWeights.z;
    }
    return float4(normalize(normal), height);
}

// Occlusion of thin paint between strokes.
float LoogaImpastoCavity(float height, float strength)
{
    return lerp(1.0, smoothstep(0.0, 0.45, height), strength);
}

// Smoothness with gloss added on ridges of thick, wet paint and removed in cavities.
float LoogaImpastoSmoothness(float smoothness, float height, float ridgeGloss)
{
    return saturate(smoothness + ridgeGloss * (height - 0.5));
}

#endif
