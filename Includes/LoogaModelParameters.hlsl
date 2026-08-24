#ifndef LOOGA_MODEL_PARAMETERS_INCLUDED
#define LOOGA_MODEL_PARAMETERS_INCLUDED

#define LOOGA_MODEL_DISNEY_BURLEY 0
#define LOOGA_MODEL_SOURCE2 1
// Value 2 is intentionally unused so existing serialized model IDs remain stable.
#define LOOGA_MODEL_MINNAERT 3
#define LOOGA_MODEL_OVERWATCH 4
#define LOOGA_MODEL_OREN_NAYAR 5
#define LOOGA_MODEL_ARKANE 6
#define LOOGA_MODEL_CUSTOM 100

#define LOOGA_DIFFUSE_LAMBERT 0
#define LOOGA_DIFFUSE_DISNEY_BURLEY 1
#define LOOGA_DIFFUSE_MINNAERT 2
#define LOOGA_DIFFUSE_OREN_NAYAR 3
#define LOOGA_DIFFUSE_WRAPPED 4
#define LOOGA_DIFFUSE_BANDED 5

#define LOOGA_SPECULAR_GGX 0
#define LOOGA_SPECULAR_BECKMANN 1
#define LOOGA_SPECULAR_PHONG 2

#define LOOGA_SPECULAR_OCCLUSION_STANDARD 0
#define LOOGA_SPECULAR_OCCLUSION_SOURCE2 1

#define LOOGA_INDIRECT_SPECULAR_GGX 0
#define LOOGA_INDIRECT_SPECULAR_BECKMANN 1
#define LOOGA_INDIRECT_SPECULAR_PHONG 2

int _LoogaLightingModel;
int _LoogaProfileDiffuseModel;
int _LoogaProfileDirectSpecularModel;
int _LoogaProfileIndirectSpecularModel;
int _LoogaProfileSpecularOcclusionModel;
float _LoogaProfileDiffuseStrength;
float _LoogaProfileDirectSpecularStrength;
float _LoogaProfileIndirectSpecularStrength;
float _LoogaProfileDirectRoughnessScale;
float _LoogaProfileDirectRoughnessBias;
float _LoogaProfileIndirectRoughnessScale;
float _LoogaProfileIndirectRoughnessBias;
float _LoogaProfileIndirectFresnelPower;
float _LoogaProfileMinnaertK;
float _LoogaProfileOrenNayarSigma;
float _LoogaProfileDiffuseWrap;
float _LoogaProfileBandCount;
float _LoogaProfileBandFeather;
float _LoogaProfileBandBlend;
float _LoogaProfileSecondarySpecularWeight;
float _LoogaProfileSecondaryRoughnessSpread;
float _LoogaProfileHighlightShapeStrength;
float _LoogaProfileHighlightShapeFloor;
float _LoogaProfileHighlightShapeStart;
float _LoogaProfileHighlightShapeEnd;
float _LoogaProfileGrazingOcclusionStrength;
float _LoogaProfileEdgeOcclusionStrength;
float _LoogaProfileEdgeOcclusionStart;
float _LoogaProfileEdgeOcclusionEnd;

#define LOOGA_MODEL_PARAMETER_CBUFFER_FIELDS \
    float _OrenNayarSigma; \
    float _MinnaertK; \
    float _OverwatchWrap; \
    float _ArkaneBandCount; \
    float _ArkaneBandFeather; \
    float _MinnaertIndirectSpecularModel; \
    float _OrenNayarIndirectSpecularModel;

#define LOOGA_DECLARE_MODEL_PARAMETER_TEXTURES

half4 EncodeLoogaModelParameters(
    half orenNayarSigma,
    half minnaertK,
    half overwatchWrap,
    half arkaneBandCount,
    half arkaneBandFeather,
    half minnaertIndirectSpecularModel,
    half orenNayarIndirectSpecularModel)
{
    [branch] switch (_LoogaLightingModel)
    {
        case LOOGA_MODEL_MINNAERT:
            return half4(saturate(minnaertK * 0.5h), saturate(minnaertIndirectSpecularModel * 0.5h), 0.0h, 0.0h);
        case LOOGA_MODEL_OVERWATCH:
            return half4(saturate(overwatchWrap * 2.0h), 0.0h, 0.0h, 0.0h);
        case LOOGA_MODEL_OREN_NAYAR:
            return half4(saturate(orenNayarSigma / 90.0h), saturate(orenNayarIndirectSpecularModel * 0.5h), 0.0h, 0.0h);
        case LOOGA_MODEL_ARKANE:
            return half4(saturate((arkaneBandCount - 1.0h) / 7.0h), saturate(arkaneBandFeather * 2.0h), 0.0h, 0.0h);
        default:
            return 0.0h;
    }
}

#define LOOGA_SAMPLE_MODEL_PARAMETERS(uv) EncodeLoogaModelParameters( \
    _OrenNayarSigma, _MinnaertK, _OverwatchWrap, _ArkaneBandCount, _ArkaneBandFeather, \
    _MinnaertIndirectSpecularModel, _OrenNayarIndirectSpecularModel)

half4 GetDefaultLoogaModelParameters()
{
    [branch] switch (_LoogaLightingModel)
    {
        case LOOGA_MODEL_MINNAERT:
            return half4(0.35h, 0.5h, 0.0h, 0.0h);
        case LOOGA_MODEL_OVERWATCH:
            return half4(0.16h, 0.0h, 0.0h, 0.0h);
        case LOOGA_MODEL_OREN_NAYAR:
            return half4(1.0h / 3.0h, 0.0h, 0.0h, 0.0h);
        case LOOGA_MODEL_ARKANE:
            return half4(2.0h / 7.0h, 0.3h, 0.0h, 0.0h);
        default:
            return 0.0h;
    }
}

half DecodeLoogaMinnaertK(half4 parameters) { return parameters.r * 2.0h; }
half DecodeLoogaOverwatchWrap(half4 parameters) { return parameters.r * 0.5h; }
half DecodeLoogaOrenNayarSigma(half4 parameters) { return radians(parameters.r * 90.0h); }
half DecodeLoogaArkaneBandCount(half4 parameters) { return lerp(1.0h, 8.0h, parameters.r); }
half DecodeLoogaArkaneBandFeather(half4 parameters) { return parameters.g * 0.5h; }
int DecodeLoogaIndirectSpecularModel(half4 parameters) { return (int)round(parameters.g * 2.0h); }

#endif
