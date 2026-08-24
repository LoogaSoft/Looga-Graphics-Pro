#ifndef LOOGA_SHADER_GRAPH_FORWARD_PASS_INCLUDED
#define LOOGA_SHADER_GRAPH_FORWARD_PASS_INCLUDED

#define LOOGA_SHADER_GRAPH_LIGHTING_PASS 1
#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaMasterLighting.hlsl"
#include "Packages/com.loogasoft.loogagraphicspro/Includes/ShaderGraph/LoogaShaderGraphCommon.hlsl"

void InitializeLoogaGraphForwardInput(Varyings input, SurfaceDescription surfaceDescription, out InputData inputData)
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
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
    inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
}

PackedVaryings vert(Attributes input)
{
    Varyings output = BuildVaryings(input);
    return PackVaryings(output);
}

void frag(PackedVaryings packedInput, out half4 outColor : SV_Target0
    #ifdef _WRITE_RENDERING_LAYERS
        , out uint outRenderingLayers : SV_Target1
    #endif
)
{
    Varyings input = UnpackVaryings(packedInput);
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    SurfaceDescription surfaceDescription = BuildSurfaceDescription(input);

    #if defined(_ALPHATEST_ON)
        half alpha = AlphaDiscard(surfaceDescription.Alpha, surfaceDescription.AlphaClipThreshold);
    #elif defined(_SURFACE_TYPE_TRANSPARENT)
        half alpha = surfaceDescription.Alpha;
    #else
        half alpha = 1.0h;
    #endif

    #if defined(LOD_FADE_CROSSFADE) && USE_UNITY_CROSSFADE
        LODFadeCrossFade(input.positionCS);
    #endif

    InputData inputData;
    InitializeLoogaGraphForwardInput(input, surfaceDescription, inputData);
    half3 albedo = AlphaModulate(surfaceDescription.BaseColor, alpha);
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
    half3 directF0 = GetLoogaDirectSpecularF0(f0);
    half perceptualRoughness = 1.0h - smoothness;
    half4 modelParameters = GetLoogaGraphModelParameters(surfaceDescription);
    float3 viewDirectionWS = inputData.viewDirectionWS;
    float NoV = saturate(dot(inputData.normalWS, viewDirectionWS));

    LoogaGraphBakedInput bakedInput = BuildLoogaGraphBakedInput(input);
    half3 bakedGI;
    half4 shadowMask;
    LOOGA_SAMPLE_BAKED_LIGHTING(bakedInput, inputData.normalWS, perceptualRoughness, modelParameters, bakedGI, shadowMask);

    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
    Light mainLight = GetMainLight(shadowCoord, input.positionWS, shadowMask);
    ApplyLoogaReceiveShadowOption(mainLight);
    ApplyLoogaScreenSpaceDirectAO(mainLight, inputData.normalizedScreenSpaceUV);
    uint meshRenderingLayers = GetMeshRenderingLayer();
    float3 color = EvaluateLoogaAdditionalLight(
        mainLight, meshRenderingLayers, diffuseColor, directF0, perceptualRoughness,
        inputData.normalWS, 1.0h, viewDirectionWS, NoV, modelParameters, false,
        LOOGA_GRAPH_SECONDARY_LOBE_MIX(surfaceDescription) > 0.0001h,
        1.0h - saturate(LOOGA_GRAPH_SECONDARY_SMOOTHNESS(surfaceDescription)),
        saturate(LOOGA_GRAPH_SECONDARY_LOBE_MIX(surfaceDescription)));

    color += EvaluateLoogaAdditionalBacklight(mainLight, meshRenderingLayers,
        LOOGA_GRAPH_SUBSURFACE_COLOR(surfaceDescription),
        LOOGA_GRAPH_SCATTER_WIDTH(surfaceDescription),
        LOOGA_GRAPH_AMBIENT_SCATTER(surfaceDescription),
        LOOGA_GRAPH_TRANSMISSION_SHADOW_SOFTNESS(surfaceDescription),
        LOOGA_GRAPH_BACKLIGHT_RIM_POWER(surfaceDescription),
        LOOGA_GRAPH_BACKLIGHT_DISTORTION(surfaceDescription), inputData.normalWS,
        viewDirectionWS, LOOGA_GRAPH_TRANSMISSION(surfaceDescription), false);

    color += EvaluateLoogaAdditionalLights(
        diffuseColor, directF0, perceptualRoughness, inputData.normalWS, 1.0h,
        viewDirectionWS, NoV, modelParameters, input.positionWS,
        inputData.normalizedScreenSpaceUV, shadowMask, false,
        LOOGA_GRAPH_SECONDARY_LOBE_MIX(surfaceDescription) > 0.0001h,
        1.0h - saturate(LOOGA_GRAPH_SECONDARY_SMOOTHNESS(surfaceDescription)),
        saturate(LOOGA_GRAPH_SECONDARY_LOBE_MIX(surfaceDescription)));
    color += EvaluateLoogaAdditionalBacklights(inputData, shadowMask,
        GetScreenSpaceAmbientOcclusion(inputData.normalizedScreenSpaceUV), meshRenderingLayers,
        LOOGA_GRAPH_SUBSURFACE_COLOR(surfaceDescription),
        LOOGA_GRAPH_SCATTER_WIDTH(surfaceDescription),
        LOOGA_GRAPH_AMBIENT_SCATTER(surfaceDescription),
        LOOGA_GRAPH_TRANSMISSION_SHADOW_SOFTNESS(surfaceDescription),
        LOOGA_GRAPH_BACKLIGHT_RIM_POWER(surfaceDescription),
        LOOGA_GRAPH_BACKLIGHT_DISTORTION(surfaceDescription), inputData.normalWS,
        viewDirectionWS, LOOGA_GRAPH_TRANSMISSION(surfaceDescription), false);

    color += EvaluateLoogaBakedDiffuse(diffuseColor, bakedGI, occlusion);
    if (LoogaEnvironmentReflectionsEnabled())
    {
        half indirectOcclusion = GetLoogaMetalIndirectOcclusion(occlusion, metallic);
        color += EvaluateGlobalLoogaIndirect(f0, perceptualRoughness, indirectOcclusion,
            viewDirectionWS, inputData.normalWS, inputData.normalWS, NoV,
            input.positionWS, inputData.normalizedScreenSpaceUV, modelParameters);
    }
    color += surfaceDescription.Emission;
    color = MixFog(color, inputData.fogCoord);

    #if defined(_SURFACE_TYPE_TRANSPARENT)
        const bool isTransparent = true;
    #else
        const bool isTransparent = false;
    #endif
    outColor = half4(color, OutputAlpha(alpha, isTransparent));

    #ifdef _WRITE_RENDERING_LAYERS
        outRenderingLayers = EncodeMeshRenderingLayer();
    #endif
}

#endif
