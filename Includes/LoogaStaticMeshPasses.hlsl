#ifndef LOOGA_STATIC_MESH_PASSES_INCLUDED
#define LOOGA_STATIC_MESH_PASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

struct LoogaStaticAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float2 staticLightmapUV : TEXCOORD1;
    float2 dynamicLightmapUV : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct LoogaStaticVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float4 tangentWS : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

float2 LoogaStaticUV(float2 uv)
{
#if defined(LOOGA_STATIC_RAW_UV)
    return uv;
#else
    return TRANSFORM_TEX(uv, _BaseMap);
#endif
}

LoogaStaticVaryings LoogaStaticVertex(LoogaStaticAttributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    LoogaStaticVaryings output = (LoogaStaticVaryings)0;
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.uv = LoogaStaticUV(input.uv);
    VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS, input.tangentOS);
    output.normalWS = normals.normalWS;
    output.tangentWS = float4(normals.tangentWS, input.tangentOS.w * GetOddNegativeScale());
    return output;
}

void LoogaStaticClip(LoogaStaticVaryings input)
{
    if (_AlphaClip > 0.5)
    {
        clip(SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)).a * _BaseColor.a - _Cutoff);
    }
#if defined(LOD_FADE_CROSSFADE)
    LODFadeCrossFade(input.positionCS);
#endif
}

half LoogaStaticDepth(LoogaStaticVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    LoogaStaticClip(input);
    return input.positionCS.z;
}

half4 LoogaStaticNormals(LoogaStaticVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    LoogaStaticClip(input);
    float3 tangentNormal = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
    float3 bitangent = cross(input.normalWS, input.tangentWS.xyz) * input.tangentWS.w;
    float3 normal = normalize(mul(tangentNormal, float3x3(input.tangentWS.xyz, bitangent, input.normalWS)));
#if defined(_GBUFFER_NORMALS_OCT)
    return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(normal) * 0.5 + 0.5)), 0);
#else
    return half4(normal, 0);
#endif
}

float3 _LightDirection;
float3 _LightPosition;
LoogaStaticVaryings LoogaStaticShadow(LoogaStaticAttributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    LoogaStaticVaryings output = LoogaStaticVertex(input);
    float3 position = TransformObjectToWorld(input.positionOS.xyz);
    float3 direction = _LightDirection;
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    direction = normalize(_LightPosition - position);
#endif
    output.positionCS = TransformWorldToHClip(ApplyShadowBias(position, output.normalWS, direction));
#if UNITY_REVERSED_Z
    output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
#else
    output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
#endif
    return output;
}

LoogaStaticVaryings LoogaStaticMetaVertex(LoogaStaticAttributes input)
{
    LoogaStaticVaryings output = LoogaStaticVertex(input);
    output.positionCS = UnityMetaVertexPosition(input.positionOS.xyz, input.staticLightmapUV, input.dynamicLightmapUV);
    return output;
}

half4 LoogaStaticMetaFragment(LoogaStaticVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    LoogaStaticClip(input);
    MetaInput meta = (MetaInput)0;
    meta.Albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
#if defined(_EMISSION)
    meta.Emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb;
#endif
    return UnityMetaFragment(meta);
}
#endif
