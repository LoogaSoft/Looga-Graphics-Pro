#ifndef LOOGA_SHADER_GRAPH_MATERIAL_EXTRAS_PASS_INCLUDED
#define LOOGA_SHADER_GRAPH_MATERIAL_EXTRAS_PASS_INCLUDED

#include "Packages/com.loogasoft.loogagraphicspro/Includes/ShaderGraph/LoogaShaderGraphCommon.hlsl"

PackedVaryings vert(Attributes input)
{
    Varyings output = BuildVaryings(input);
    return PackVaryings(output);
}

struct LoogaGraphMaterialExtrasOutput
{
    half4 materialExtras : SV_Target0;
    half4 modelParameters : SV_Target1;
};

LoogaGraphMaterialExtrasOutput frag(PackedVaryings packedInput)
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

    LoogaGraphMaterialExtrasOutput output;
    output.materialExtras = half4(
        1.0h - saturate(LOOGA_GRAPH_SECONDARY_SMOOTHNESS(surfaceDescription)),
        saturate(LOOGA_GRAPH_SECONDARY_LOBE_MIX(surfaceDescription)), 0.0h, 0.0h);
    output.modelParameters = GetLoogaGraphModelParameters(surfaceDescription);
    return output;
}

#endif
