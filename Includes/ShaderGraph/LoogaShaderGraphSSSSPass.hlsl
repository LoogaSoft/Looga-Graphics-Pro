#ifndef LOOGA_SHADER_GRAPH_SSSS_PASS_INCLUDED
#define LOOGA_SHADER_GRAPH_SSSS_PASS_INCLUDED

#include "Packages/com.loogasoft.loogagraphicspro/Includes/ShaderGraph/LoogaShaderGraphCommon.hlsl"

PackedVaryings vert(Attributes input)
{
    Varyings output = BuildVaryings(input);
    return PackVaryings(output);
}

struct LoogaGraphSSSSOutput
{
    half4 profile : SV_Target0;
    half4 profileExtra : SV_Target1;
};

LoogaGraphSSSSOutput frag(PackedVaryings packedInput)
{
    Varyings input = UnpackVaryings(packedInput);
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    SurfaceDescription surfaceDescription = BuildSurfaceDescription(input);

    #if defined(_ALPHATEST_ON)
        clip(surfaceDescription.Alpha - surfaceDescription.AlphaClipThreshold);
    #endif
    clip(max(LOOGA_GRAPH_SCATTER_WIDTH(surfaceDescription),
        LOOGA_GRAPH_TRANSMISSION(surfaceDescription)) - 0.0001h);

    LoogaGraphSSSSOutput output;
    output.profile = half4(
        LOOGA_GRAPH_SUBSURFACE_COLOR(surfaceDescription),
        saturate(LOOGA_GRAPH_SCATTER_WIDTH(surfaceDescription) / 5.0h));
    output.profileExtra = half4(
        saturate(LOOGA_GRAPH_AMBIENT_SCATTER(surfaceDescription) / 5.0h),
        saturate(LOOGA_GRAPH_TRANSMISSION_SHADOW_SOFTNESS(surfaceDescription)),
        PackLoogaBacklightShape(LOOGA_GRAPH_BACKLIGHT_RIM_POWER(surfaceDescription),
            LOOGA_GRAPH_BACKLIGHT_DISTORTION(surfaceDescription)),
        LOOGA_GRAPH_SCATTER_WIDTH(surfaceDescription) > 0.0001h ? 1.0h : 0.0h);
    return output;
}

#endif
