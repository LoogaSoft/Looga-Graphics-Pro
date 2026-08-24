#ifndef LOOGA_FOLIAGE_CORE_INCLUDED
#define LOOGA_FOLIAGE_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "LoogaWind.hlsl"
#include "LoogaNoise.hlsl"
#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaModelParameters.hlsl"
#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaScatteringPacking.hlsl"

// --- MAIN PASS STRUCTS ---
struct FoliageAttributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float4 tangentOS    : TANGENT;
    float2 uv           : TEXCOORD0;
    float2 staticLightmapUV : TEXCOORD1;
    float2 dynamicLightmapUV : TEXCOORD2;
};

struct FoliageVaryings
{
    float4 positionCS   : SV_POSITION;
    float2 uv           : TEXCOORD0;
    float3 normalWS     : TEXCOORD1;
    float4 tangentWS    : TEXCOORD3;
    float3 positionWS   : TEXCOORD4;
    float  windGust     : TEXCOORD5; // Used by grass for wind tinting
    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 6);
    float2 dynamicLightmapUV : TEXCOORD7;
    float4 probeOcclusion : TEXCOORD8;
};

// --- PROFILE PASS STRUCTS ---
struct AttributesProfile
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
};

struct VaryingsProfile
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
};

// --- SHARED VARIABLES ---
TEXTURE2D(_BaseMap);    SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap);    SAMPLER(sampler_BumpMap);
TEXTURE2D(_ThicknessMap); // NEW: Shared Thickness Map
LOOGA_DECLARE_MODEL_PARAMETER_TEXTURES;

CBUFFER_START(UnityPerMaterial)
    // Shared
    float _AlphaClip;
    float _Cutoff;
    float _BumpScale;
    float _BackfaceNormalMode;
    float _Smoothness;
    float4 _SubsurfaceColor;
    float _AmbientScatterStrength;
    float _ScatterWidth;
    float _TransmissionStrength;
    float _BacklightRimPower;
    float _BacklightDistortion;
    float _TransmissionShadowSoftness;
    float _WindInfluence;

    // Color Variation
    float _GlobalGridScale;
    float2 _GlobalHueVar;
    float2 _GlobalSatVar;
    float2 _GlobalLumVar;

    float _LocalNoiseScale;
    int _LocalNoiseType;
    float2 _LocalHueVar;
    float2 _LocalSatVar;
    float2 _LocalLumVar;

    // Grass Specific (Ignored by Foliage)
    float4 _WindTint;
    float _WindTintStrength;
    float _InteractionBend;
    LOOGA_MODEL_PARAMETER_CBUFFER_FIELDS;
CBUFFER_END

// --- GRASS INTERACTION MATH ---
float4 _GrassInteractors[64]; // xyz = Position, w = Push Radius
int _GrassInteractorCount;

float3 ApplyGrassInteraction(float3 positionWS, float3 positionOS, float bendStrength)
{
    // Ensure the root of the grass stays rooted, only the top bends
    float heightMask = saturate(positionOS.y * 0.5);
    float3 totalPushWS = float3(0, 0, 0);

    // Loop through all active players/entities in the area
    int count = min(_GrassInteractorCount, 64);
    for (int i = 0; i < count; i++)
    {
        float3 effectorPos = _GrassInteractors[i].xyz;
        float radius = _GrassInteractors[i].w;

        // Calculate distance strictly on the XZ plane so we form an invisible cylinder of influence
        float3 dirWS = positionWS - effectorPos;
        dirWS.y = 0;
        float dist = length(dirWS);

        if (dist < radius)
        {
            // Smoothstep falloff so the grass curves smoothly down instead of snapping
            float falloff = 1.0 - saturate(dist / max(radius, 0.01));
            falloff = falloff * falloff * (3.0 - 2.0 * falloff);

            // Push outwards, but also force the vector slightly downwards (-1.0) so it squashes into the mud
            float3 pushDir = normalize(dirWS + float3(0, -1.0, 0));

            totalPushWS += pushDir * falloff * bendStrength * heightMask;
        }
    }

    return totalPushWS;
}

// --- SHARED COLOR MATH ---
half3 GetVariedColor(half3 baseColor, float3 positionWS)
{
    float3 globalRandom = Hash33(floor(positionWS * _GlobalGridScale));
    float3 globalVar = float3(
        lerp(_GlobalHueVar.x, _GlobalHueVar.y, globalRandom.x),
        lerp(_GlobalSatVar.x, _GlobalSatVar.y, globalRandom.y),
        lerp(_GlobalLumVar.x, _GlobalLumVar.y, globalRandom.z)
    );

    float localNoise = GetLoogaNoise(positionWS, _LocalNoiseScale, _LocalNoiseType);
    float3 localVar = float3(
        lerp(_LocalHueVar.x, _LocalHueVar.y, localNoise),
        lerp(_LocalSatVar.x, _LocalSatVar.y, localNoise),
        lerp(_LocalLumVar.x, _LocalLumVar.y, localNoise)
    );

    return ApplyHSVVariation(baseColor, globalVar + localVar);
}

#endif
