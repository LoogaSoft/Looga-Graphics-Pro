#ifndef LOOGA_NOISE_INCLUDED
#define LOOGA_NOISE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

// 1. Fast 1D Hash from 3D Position
float Hash31(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

// 2. Fast 3D Hash (Returns 3 independent random values between 0.0 and 1.0)
float3 Hash33(float3 p)
{
    p = frac(p * float3(0.1031, 0.1030, 0.0973));
    p += dot(p, p.yxz + 33.33);
    return frac((p.xxy + p.yxx) * p.zyx);
}

// 3. 3D Value Noise (Smooth, organic noise)
float ValueNoise3D(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);

    return lerp(
        lerp(lerp(Hash31(i + float3(0, 0, 0)), Hash31(i + float3(1, 0, 0)), u.x),
             lerp(Hash31(i + float3(0, 1, 0)), Hash31(i + float3(1, 1, 0)), u.x), u.y),
        lerp(lerp(Hash31(i + float3(0, 0, 1)), Hash31(i + float3(1, 0, 1)), u.x),
             lerp(Hash31(i + float3(0, 1, 1)), Hash31(i + float3(1, 1, 1)), u.x), u.y), u.z);
}

// 4. Wavy Noise
float WavyNoise3D(float3 p)
{
    return (sin(p.x) + sin(p.y) + sin(p.z)) * 0.3333 + 0.5;
}

float GetLoogaNoise(float3 positionWS, float scale, int type)
{
    float3 p = positionWS * scale;
    if (type == 0) return Hash31(floor(p)); 
    if (type == 1) return ValueNoise3D(p);  
    return WavyNoise3D(p);                  
}

float3 ApplyHSVVariation(float3 baseColor, float3 variation)
{
    float3 hsv = RgbToHsv(baseColor);
    hsv.x = frac(hsv.x + variation.x);     // Hue wraps around 0-1 natively
    hsv.y = saturate(hsv.y + variation.y); // Saturation clamps
    hsv.z = saturate(hsv.z + variation.z); // Luminance/Value clamps
    return HsvToRgb(hsv);
}

#endif