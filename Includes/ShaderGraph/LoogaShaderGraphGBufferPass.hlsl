#ifndef LOOGA_SHADER_GRAPH_GBUFFER_PASS_INCLUDED
#define LOOGA_SHADER_GRAPH_GBUFFER_PASS_INCLUDED

#define LOOGA_DISABLE_MODEL_REFLECTIONS 1
#define LOOGA_SHADER_GRAPH_LIGHTING_PASS 1
#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaLightingHelpers.hlsl"
#include "Packages/com.loogasoft.loogagraphicspro/Includes/ShaderGraph/LoogaShaderGraphCommon.hlsl"

void InitializeLoogaGraphInputData(Varyings input, SurfaceDescription surfaceDescription, out InputData inputData)
{
    inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.positionCS = input.positionCS;

    #ifdef _NORMALMAP
        float crossSign = (input.tangentWS.w > 0.0 ? 1.0 : -1.0) * GetOddNegativeScale();
        float3 bitangent = crossSign * cross(input.normalWS.xyz, input.tangentWS.xyz);
        inputData.tangentToWorld = half3x3(input.tangentWS.xyz, bitangent, input.normalWS.xyz);
        #if _NORMAL_DROPOFF_TS
            inputData.normalWS = TransformTangentToWorld(surfaceDescription.NormalTS, inputData.tangentToWorld);
        #elif _NORMAL_DROPOFF_OS
            inputData.normalWS = TransformObjectToWorldNormal(surfaceDescription.NormalOS);
        #elif _NORMAL_DROPOFF_WS
            inputData.normalWS = surfaceDescription.NormalWS;
        #endif
    #else
        inputData.normalWS = input.normalWS;
    #endif

    inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
}

PackedVaryings vert(Attributes input)
{
    Varyings output = BuildVaryings(input);
    return PackVaryings(output);
}

GBufferFragOutput frag(PackedVaryings packedInput)
{
    Varyings input = UnpackVaryings(packedInput);
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    SurfaceDescription surfaceDescription = BuildSurfaceDescription(input);

    #if defined(_ALPHATEST_ON)
        clip(surfaceDescription.Alpha - surfaceDescription.AlphaClipThreshold);
    #endif

    #if defined(LOD_FADE_CROSSFADE) && USE_UNITY_CROSSFADE
        LODFadeCrossFade(input.positionCS);
    #endif

    InputData inputData;
    InitializeLoogaGraphInputData(input, surfaceDescription, inputData);

    half3 albedo = surfaceDescription.BaseColor;
    half metallic = 0.0h;
    half3 specularF0 = kDielectricSpec.rgb;
    #if defined(_SPECULAR_SETUP)
        specularF0 = surfaceDescription.Specular;
    #else
        metallic = saturate(surfaceDescription.Metallic);
    #endif
    half smoothness = saturate(surfaceDescription.Smoothness);
    half occlusion = saturate(surfaceDescription.Occlusion);

    ApplyLoogaDBuffer(input.positionCS, albedo, inputData.normalWS, metallic, specularF0, occlusion, smoothness);

    half3 f0;
    #if defined(_SPECULAR_SETUP)
        f0 = specularF0;
    #else
        f0 = lerp(kDielectricSpec.rgb, albedo, metallic);
    #endif
    half3 diffuseColor = GetLoogaDiffuseColor(albedo, metallic, f0);
    half perceptualRoughness = 1.0h - smoothness;
    half4 modelParameters = GetLoogaGraphModelParameters(surfaceDescription);
    LoogaGraphBakedInput bakedInput = BuildLoogaGraphBakedInput(input);
    half3 bakedGI;
    half4 shadowMask;
    LOOGA_SAMPLE_BAKED_LIGHTING(bakedInput, inputData.normalWS, perceptualRoughness, modelParameters, bakedGI, shadowMask);

    uint materialFlags = GetLoogaCommonMaterialFlags();
    if (LOOGA_GRAPH_SECONDARY_LOBE_MIX(surfaceDescription) > 0.0001h)
        materialFlags |= LOOGA_MATERIAL_FLAG_DUAL_LOBE;
    GBufferFragOutput output = (GBufferFragOutput)0;
    output.gBuffer0 = half4(albedo, PackLoogaMaterialFlags(materialFlags));
    output.gBuffer1 = half4(PackLoogaGBufferSpecular(metallic, specularF0), occlusion);
    output.gBuffer2 = half4(PackGBufferNormal(inputData.normalWS), smoothness);
    output.color = half4(
        surfaceDescription.Emission + EvaluateLoogaBakedDiffuse(diffuseColor, bakedGI, occlusion),
        saturate(LOOGA_GRAPH_TRANSMISSION(surfaceDescription)));

    #if defined(GBUFFER_FEATURE_DEPTH)
        output.depth = input.positionCS.z;
    #endif
    #if defined(GBUFFER_FEATURE_SHADOWMASK)
        output.shadowMask = shadowMask;
    #endif
    #if defined(GBUFFER_FEATURE_RENDERING_LAYERS)
        output.meshRenderingLayers = EncodeMeshRenderingLayer();
    #endif
    return output;
}

#endif
