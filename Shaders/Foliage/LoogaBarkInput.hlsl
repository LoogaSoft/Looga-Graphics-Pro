#ifndef LOOGA_BARK_INPUT_INCLUDED
#define LOOGA_BARK_INPUT_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "LoogaWind.hlsl"
#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaModelParameters.hlsl"
#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaScatteringPacking.hlsl"
TEXTURE2D(_BaseMap);    SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);  SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap);
            TEXTURE2D(_SpecGlossMap);
            TEXTURE2D(_OcclusionMap);
            TEXTURE2D(_MaskMap);    SAMPLER(sampler_MaskMap);
            TEXTURE2D(_ThicknessMap);
            LOOGA_DECLARE_MODEL_PARAMETER_TEXTURES;
CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _AlphaClip;
                float _Cutoff;
                float _BumpScale;
                float _BackfaceNormalMode;
                float4 _SpecColor;
                float _Metallic;
                float _OcclusionStrength;
                float _SmoothnessTextureChannel;
                float _BaseSmoothnessScale;
                float _WindInfluence;
                float _LoogaDeformationLimit;
                float4 _SubsurfaceColor;
                float _AmbientScatterStrength;
                float _ScatterWidth;
                float _TransmissionStrength;
                float _TransmissionShadowSoftness;
                float _BacklightRimPower;
                float _BacklightDistortion;
                LOOGA_MODEL_PARAMETER_CBUFFER_FIELDS;
            CBUFFER_END
struct FoliageAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float2 staticLightmapUV : TEXCOORD1;
    float2 dynamicLightmapUV : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
float3 LoogaFoliageDeform(float3 positionOS, float4x4 model, float4x4 inverseModel, bool previous)
{
    float3 original = positionOS;
    float3 world = mul(model, float4(positionOS, 1)).xyz;
    positionOS = ApplyProceduralWindAtTime(positionOS, world, 0, _WindInfluence,
        previous ? _LastTimeParameters.x : _Time.y,
        previous ? _LoogaPreviousWindDirectionAndSpeed : _LoogaWindDirectionAndSpeed,
        previous ? _LoogaPreviousWindTurbulence : _LoogaWindTurbulence);
    float3 displacement = positionOS - original;
    return original + displacement * min(1, max(_LoogaDeformationLimit, 0) / max(length(displacement), 0.00001));
}
#endif
