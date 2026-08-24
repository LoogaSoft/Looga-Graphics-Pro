#ifndef LOOGA_SHADER_GRAPH_COMMON_INCLUDED
#define LOOGA_SHADER_GRAPH_COMMON_INCLUDED

#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaModelParameters.hlsl"
#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaScatteringPacking.hlsl"

#if defined(SURFACEDESCRIPTION_LOOGAMINNAERTK)
    #define LOOGA_GRAPH_MINNAERT_K(s) (s.LoogaMinnaertK)
#else
    #define LOOGA_GRAPH_MINNAERT_K(s) (0.7h)
#endif
#if defined(SURFACEDESCRIPTION_LOOGAMINNAERTINDIRECTMODEL)
    #define LOOGA_GRAPH_MINNAERT_INDIRECT_MODEL(s) (s.LoogaMinnaertIndirectModel)
#else
    #define LOOGA_GRAPH_MINNAERT_INDIRECT_MODEL(s) (1.0h)
#endif
#if defined(SURFACEDESCRIPTION_LOOGAOVERWATCHWRAP)
    #define LOOGA_GRAPH_OVERWATCH_WRAP(s) (s.LoogaOverwatchWrap)
#else
    #define LOOGA_GRAPH_OVERWATCH_WRAP(s) (0.08h)
#endif
#if defined(SURFACEDESCRIPTION_LOOGAORENNAYARSIGMA)
    #define LOOGA_GRAPH_OREN_NAYAR_SIGMA(s) (s.LoogaOrenNayarSigma)
#else
    #define LOOGA_GRAPH_OREN_NAYAR_SIGMA(s) (30.0h)
#endif
#if defined(SURFACEDESCRIPTION_LOOGAORENNAYARINDIRECTMODEL)
    #define LOOGA_GRAPH_OREN_NAYAR_INDIRECT_MODEL(s) (s.LoogaOrenNayarIndirectModel)
#else
    #define LOOGA_GRAPH_OREN_NAYAR_INDIRECT_MODEL(s) (0.0h)
#endif
#if defined(SURFACEDESCRIPTION_LOOGAARKANEBANDCOUNT)
    #define LOOGA_GRAPH_ARKANE_BAND_COUNT(s) (s.LoogaArkaneBandCount)
#else
    #define LOOGA_GRAPH_ARKANE_BAND_COUNT(s) (3.0h)
#endif
#if defined(SURFACEDESCRIPTION_LOOGAARKANEBANDFEATHER)
    #define LOOGA_GRAPH_ARKANE_BAND_FEATHER(s) (s.LoogaArkaneBandFeather)
#else
    #define LOOGA_GRAPH_ARKANE_BAND_FEATHER(s) (0.15h)
#endif
#if defined(SURFACEDESCRIPTION_LOOGASECONDARYSMOOTHNESS)
    #define LOOGA_GRAPH_SECONDARY_SMOOTHNESS(s) (s.LoogaSecondarySmoothness)
#else
    #define LOOGA_GRAPH_SECONDARY_SMOOTHNESS(s) (0.5h)
#endif
#if defined(SURFACEDESCRIPTION_LOOGASECONDARYLOBEMIX)
    #define LOOGA_GRAPH_SECONDARY_LOBE_MIX(s) (s.LoogaSecondaryLobeMix)
#else
    #define LOOGA_GRAPH_SECONDARY_LOBE_MIX(s) (0.0h)
#endif
#if defined(SURFACEDESCRIPTION_LOOGASUBSURFACECOLOR)
    #define LOOGA_GRAPH_SUBSURFACE_COLOR(s) (s.LoogaSubsurfaceColor)
#else
    #define LOOGA_GRAPH_SUBSURFACE_COLOR(s) (half3(1.0h, 0.5h, 0.4h))
#endif
#if defined(SURFACEDESCRIPTION_LOOGASCATTERWIDTH)
    #define LOOGA_GRAPH_SCATTER_WIDTH(s) (s.LoogaScatterWidth)
#else
    #define LOOGA_GRAPH_SCATTER_WIDTH(s) (0.0h)
#endif
#if defined(SURFACEDESCRIPTION_LOOGAAMBIENTSCATTER)
    #define LOOGA_GRAPH_AMBIENT_SCATTER(s) (s.LoogaAmbientScatter)
#else
    #define LOOGA_GRAPH_AMBIENT_SCATTER(s) (0.2h)
#endif
#if defined(SURFACEDESCRIPTION_LOOGATRANSMISSION)
    #define LOOGA_GRAPH_TRANSMISSION(s) (s.LoogaTransmission)
#else
    #define LOOGA_GRAPH_TRANSMISSION(s) (0.0h)
#endif
#if defined(SURFACEDESCRIPTION_LOOGATRANSMISSIONSHADOWSOFTNESS)
    #define LOOGA_GRAPH_TRANSMISSION_SHADOW_SOFTNESS(s) (s.LoogaTransmissionShadowSoftness)
#else
    #define LOOGA_GRAPH_TRANSMISSION_SHADOW_SOFTNESS(s) (0.5h)
#endif
#if defined(SURFACEDESCRIPTION_LOOGABACKLIGHTRIMPOWER)
    #define LOOGA_GRAPH_BACKLIGHT_RIM_POWER(s) (s.LoogaBacklightRimPower)
#else
    #define LOOGA_GRAPH_BACKLIGHT_RIM_POWER(s) (4.0h)
#endif
#if defined(SURFACEDESCRIPTION_LOOGABACKLIGHTDISTORTION)
    #define LOOGA_GRAPH_BACKLIGHT_DISTORTION(s) (s.LoogaBacklightDistortion)
#else
    #define LOOGA_GRAPH_BACKLIGHT_DISTORTION(s) (0.2h)
#endif

half4 GetLoogaGraphModelParameters(SurfaceDescription surfaceDescription)
{
    return EncodeLoogaModelParameters(
        LOOGA_GRAPH_OREN_NAYAR_SIGMA(surfaceDescription),
        LOOGA_GRAPH_MINNAERT_K(surfaceDescription),
        LOOGA_GRAPH_OVERWATCH_WRAP(surfaceDescription),
        LOOGA_GRAPH_ARKANE_BAND_COUNT(surfaceDescription),
        LOOGA_GRAPH_ARKANE_BAND_FEATHER(surfaceDescription),
        LOOGA_GRAPH_MINNAERT_INDIRECT_MODEL(surfaceDescription),
        LOOGA_GRAPH_OREN_NAYAR_INDIRECT_MODEL(surfaceDescription));
}

#if defined(LOOGA_SHADER_GRAPH_LIGHTING_PASS)
    struct LoogaGraphBakedInput
    {
        float4 positionCS;
        float3 positionWS;
        half3 normalWS;
        float2 staticLightmapUV;
        float2 dynamicLightmapUV;
        half3 vertexSH;
        half4 probeOcclusion;
    };

    LoogaGraphBakedInput BuildLoogaGraphBakedInput(Varyings input)
    {
        LoogaGraphBakedInput output = (LoogaGraphBakedInput)0;
        output.positionCS = input.positionCS;
        output.positionWS = input.positionWS;
        output.normalWS = input.normalWS;
        output.staticLightmapUV = input.staticLightmapUV;
        output.dynamicLightmapUV = input.dynamicLightmapUV;
        output.vertexSH = input.sh;
        output.probeOcclusion = input.probeOcclusion;
        return output;
    }
#endif

#endif
