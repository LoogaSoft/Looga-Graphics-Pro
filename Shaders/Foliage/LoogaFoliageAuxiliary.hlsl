#ifndef LOOGA_FOLIAGE_AUXILIARY_INCLUDED
#define LOOGA_FOLIAGE_AUXILIARY_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"
struct LoogaFoliageAuxOutput
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float4 tangentWS : TEXCOORD2;
    float4 currentCS : TEXCOORD3;
    float4 previousCS : TEXCOORD4;
    float3 positionWS : TEXCOORD5;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
LoogaFoliageAuxOutput LoogaFoliageAuxVertex(FoliageAttributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    LoogaFoliageAuxOutput output = (LoogaFoliageAuxOutput)0;
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    float3 current = LoogaFoliageDeform(input.positionOS.xyz, UNITY_MATRIX_M, UNITY_MATRIX_I_M, false);
    float3 previous = LoogaFoliageDeform(input.positionOS.xyz, UNITY_PREV_MATRIX_M, UNITY_PREV_MATRIX_I_M, true);
    float3 world = TransformObjectToWorld(current);
    output.positionWS = world;
    output.positionCS = TransformWorldToHClip(world);
    output.currentCS = mul(_NonJitteredViewProjMatrix, float4(world, 1));
    output.previousCS = mul(_PrevViewProjMatrix, mul(UNITY_PREV_MATRIX_M, float4(previous, 1)));
    output.uv = input.uv;
    VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS, input.tangentOS);
    output.normalWS = normal.normalWS;
    output.tangentWS = float4(normal.tangentWS, input.tangentOS.w * GetOddNegativeScale());
    return output;
}
void LoogaFoliageAuxClip(LoogaFoliageAuxOutput input)
{
    float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
#if defined(LOOGA_BARK_GEOMETRY)
    alpha *= _BaseColor.a;
#endif
    if (_AlphaClip > 0.5) clip(alpha - _Cutoff);
#if defined(LOD_FADE_CROSSFADE)
    LODFadeCrossFade(input.positionCS);
#endif
}
half4 LoogaFoliageAuxDepth(LoogaFoliageAuxOutput input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    LoogaFoliageAuxClip(input);
    return 0;
}
half4 LoogaFoliageAuxNormal(LoogaFoliageAuxOutput input, bool front : SV_IsFrontFace) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    LoogaFoliageAuxClip(input);
    float3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
    float3 normal = normalize(input.normalWS);
    float3 tangent = normalize(input.tangentWS.xyz);
    float3 bitangent = cross(normal, tangent) * input.tangentWS.w;
    normal = normalize(mul(normalTS, float3x3(tangent, bitangent, normal)));
    if (!front && _BackfaceNormalMode > 0.5) normal = -normal;
#if !defined(LOOGA_BARK_GEOMETRY)
    half3 albedo = 0;
    half smoothness = 0, metallic = 0;
    half3 groundNormal = normal;
    LoogaFoliageGround(input.positionWS, albedo, groundNormal, smoothness, metallic);
    normal = groundNormal;
#endif
#if defined(_GBUFFER_NORMALS_OCT)
    return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(normal) * 0.5 + 0.5)), 0);
#else
    return half4(normal, 0);
#endif
}
half4 LoogaFoliageAuxMotion(LoogaFoliageAuxOutput input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    LoogaFoliageAuxClip(input);
    if (unity_MotionVectorsParams.y == 0) return 0;
    return half4(CalcNdcMotionVectorFromCsPositions(input.currentCS, input.previousCS), 0, 0);
}
float3 _LightDirection;
float3 _LightPosition;
LoogaFoliageAuxOutput LoogaFoliageAuxShadow(FoliageAttributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    LoogaFoliageAuxOutput output = LoogaFoliageAuxVertex(input);
    float3 local = LoogaFoliageDeform(input.positionOS.xyz, UNITY_MATRIX_M, UNITY_MATRIX_I_M, false);
    float3 world = TransformObjectToWorld(local);
    float3 direction = _LightDirection;
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    direction = normalize(_LightPosition - world);
#endif
    output.positionCS = TransformWorldToHClip(ApplyShadowBias(world, output.normalWS, direction));
#if UNITY_REVERSED_Z
    output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
#else
    output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
#endif
    return output;
}
struct LoogaFoliageExtrasOutput
{
    half4 materialExtras : SV_Target0;
    half4 modelParameters : SV_Target1;
};
LoogaFoliageExtrasOutput LoogaFoliageAuxExtras(LoogaFoliageAuxOutput input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    LoogaFoliageAuxClip(input);
    LoogaFoliageExtrasOutput output;
    output.materialExtras = 0;
    output.modelParameters = LOOGA_SAMPLE_MODEL_PARAMETERS(input.uv);
    return output;
}
LoogaFoliageAuxOutput LoogaFoliageMetaVertex(FoliageAttributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    LoogaFoliageAuxOutput output = (LoogaFoliageAuxOutput)0;
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    output.positionCS = UnityMetaVertexPosition(input.positionOS.xyz, input.staticLightmapUV, input.dynamicLightmapUV);
    output.uv = input.uv;
    return output;
}
half4 LoogaFoliageMetaFragment(LoogaFoliageAuxOutput input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    LoogaFoliageAuxClip(input);
    MetaInput meta = (MetaInput)0;
    meta.Albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
#if defined(LOOGA_BARK_GEOMETRY)
    meta.Albedo *= _BaseColor.rgb;
#endif
    return UnityMetaFragment(meta);
}
#endif
