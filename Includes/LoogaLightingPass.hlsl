#ifndef LOOGA_LIGHTING_PASS_INCLUDED
#define LOOGA_LIGHTING_PASS_INCLUDED

#define LOOGA_DEFERRED_GBUFFER_INPUT 1
#include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaMasterLighting.hlsl"

TEXTURE2D_X_HALF(_SSSSProfileTexture);
TEXTURE2D_X_HALF(_SSSSProfileExtraTexture);
TEXTURE2D_X_HALF(_LoogaMaterialExtrasTexture);
TEXTURE2D_X_HALF(_LoogaModelParametersTexture);
TEXTURE2D_X_HALF(_LoogaSourceColorTexture);
TYPED_TEXTURE2D_X(uint4, _LoogaRenderingLayersTexture);
TEXTURE2D_X_HALF(_LoogaShadowMaskTexture);
int _LoogaHasRenderingLayersTexture;
int _LoogaHasShadowMaskTexture;
int _LoogaGBufferNormalsAreOct;

half3 DecodeLoogaGBufferNormal(half3 packedNormalWS)
{
    half3 normalWS = packedNormalWS;
    if (_LoogaGBufferNormalsAreOct != 0)
    {
        float2 octNormalWS = Unpack888ToFloat2(packedNormalWS) * 2.0 - 1.0;
        normalWS = half3(UnpackNormalOctQuadEncode(octNormalWS));
    }

    return normalWS;
}

GBufferData UnpackLoogaGBuffers(uint2 pixelCoord)
{
    half4 gBuffer0;
    half4 gBuffer1;
    half4 gBuffer2;
    float gBufferDepth;
    uint renderingLayers;
    half4 shadowMask;
    LoadGBuffers(
        pixelCoord,
        gBuffer0,
        gBuffer1,
        gBuffer2,
        gBufferDepth,
        renderingLayers,
        shadowMask);

    GBufferData gBufferData;
    ZERO_INITIALIZE(GBufferData, gBufferData);
    gBufferData.baseColor = gBuffer0.rgb;
    gBufferData.materialFlags = UnpackGBufferMaterialFlags(gBuffer0.a);
    gBufferData.specularColor = gBuffer1.rgb;
    gBufferData.occlusion = gBuffer1.a;
    gBufferData.normalWS = SafeNormalize(DecodeLoogaGBufferNormal(gBuffer2.rgb));
    gBufferData.smoothness = gBuffer2.a;
    gBufferData.depth = gBufferDepth;
    gBufferData.shadowMask = shadowMask;
    gBufferData.meshRenderingLayers = renderingLayers;
    return gBufferData;
}

half4 LoogaDeferredLightingFrag(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float2 uv = input.texcoord;

    float rawDepth = LOAD_TEXTURE2D_X(_CameraDepthTexture, input.positionCS.xy).x;
    #if UNITY_REVERSED_Z
        float depth = rawDepth;
    #else
        float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
    #endif

    half sourceAlpha = SAMPLE_TEXTURE2D_X_LOD(_LoogaSourceColorTexture, sampler_LinearClamp, uv, 0).a;
    #if UNITY_REVERSED_Z
        half sceneAlpha = rawDepth > 0.000001 ? 1.0h : 0.0h;
    #else
        half sceneAlpha = rawDepth < 0.999999 ? 1.0h : 0.0h;
    #endif
    half outputAlpha = max(sourceAlpha, sceneAlpha);

    float3 positionWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
    GBufferData gBufferData = UnpackLoogaGBuffers(input.positionCS.xy);
    half4 gbuffer3 = SAMPLE_TEXTURE2D_X_LOD(_LoogaSourceColorTexture, sampler_LinearClamp, uv, 0);
    uint meshRenderingLayers = 0xFFFFFFFFu;
    #if defined(_LIGHT_LAYERS)
        if (_LoogaHasRenderingLayersTexture != 0)
            meshRenderingLayers = LOAD_TEXTURE2D_X(_LoogaRenderingLayersTexture, input.positionCS.xy).x;
    #endif
    half4 shadowMask = _LoogaHasShadowMaskTexture != 0
        ? LOAD_TEXTURE2D_X(_LoogaShadowMaskTexture, input.positionCS.xy)
        : half4(1.0h, 1.0h, 1.0h, 1.0h);

    half3 albedo = gBufferData.baseColor;
    uint materialFlags = gBufferData.materialFlags;
    bool isLoogaMaterial = (materialFlags & LOOGA_MATERIAL_FLAG_MARKER) != 0;

    bool isSpecularWorkflow = (materialFlags & kMaterialFlagSpecularSetup) != 0;
    bool specularHighlightsOff = (materialFlags & kMaterialFlagSpecularHighlightsOff) != 0;
    bool environmentReflectionsOff = isLoogaMaterial && (materialFlags & LOOGA_MATERIAL_FLAG_ENVIRONMENT_REFLECTIONS_OFF) != 0;
    bool hasAdvancedMaterialData = isLoogaMaterial && _LoogaAdvancedMaterialDataEnabled != 0 &&
        (materialFlags & LOOGA_MATERIAL_FLAG_ADVANCED_DATA) != 0;
    bool isDualLobe = hasAdvancedMaterialData && (materialFlags & LOOGA_MATERIAL_FLAG_DUAL_LOBE) != 0;
    bool receiveShadowsOff = (materialFlags & kMaterialFlagReceiveShadowsOff) != 0;
    half secondaryRoughness = 0.0;
    half lobeMix = 0.0;
    half4 modelParameters = GetDefaultLoogaModelParameters();

    if (hasAdvancedMaterialData)
        modelParameters = SAMPLE_TEXTURE2D_X_LOD(_LoogaModelParametersTexture, sampler_PointClamp, uv, 0);

    if (isDualLobe)
    {
        half4 materialExtras = SAMPLE_TEXTURE2D_X_LOD(_LoogaMaterialExtrasTexture, sampler_LinearClamp, uv, 0);
        secondaryRoughness = materialExtras.r;
        lobeMix = materialExtras.g;
    }

    half3 diffuseColor;
    half3 f0;
    half metallic = 0.0;

    if (isSpecularWorkflow)
    {
        f0 = gBufferData.specularColor;
        half reflectivity = ReflectivitySpecular(f0);
        diffuseColor = albedo * (1.0 - reflectivity);
    }
    else
    {
        half reflectivity = gBufferData.specularColor.r;
        metallic = saturate(MetallicFromReflectivity(reflectivity));
        f0 = lerp(kDielectricSpec.rgb, albedo, metallic);
        diffuseColor = albedo * (1.0 - reflectivity);
    }

    half materialOcclusion = gBufferData.occlusion;
    half gtbnOcclusion = 1.0;
    half3 bakedGIAndEmission = gbuffer3.rgb;
    half transmissionMask = gbuffer3.a;

    half4 ssssProfile = 0.0h;
    half4 ssssProfileExtra = 0.0h;
    if (_LoogaHasSSSSProfileTexture != 0)
    {
        ssssProfile = SAMPLE_TEXTURE2D_X_LOD(_SSSSProfileTexture, sampler_LinearClamp, uv, 0);
        ssssProfileExtra = SAMPLE_TEXTURE2D_X_LOD(_SSSSProfileExtraTexture, sampler_LinearClamp, uv, 0);
    }
    bool hasBacklighting = _LoogaBacklightingEnabled != 0 && transmissionMask > 0.0001h && ssssProfile.a > 0.001h;
    half3 ssssColor = ssssProfile.rgb;
    float ssssWidth = ssssProfile.a * 5.0; // Unpack from 0-1
    half ambientScatterStrength = ssssProfileExtra.r * 5.0; // Unpack from 0-1
    half transmissionShadowSoftness = ssssProfileExtra.g;
    half backlightRimPower;
    half backlightDistortion;
    UnpackLoogaBacklightShape(ssssProfileExtra.b, backlightRimPower, backlightDistortion);

    half3 normalWS = SafeNormalize(gBufferData.normalWS);
    half3 bentNormalWS = normalWS;

    if (_LoogaGTBNEnabled != 0)
    {
        half4 gtbnData = SAMPLE_TEXTURE2D_X_LOD(_GTBNTexture, sampler_PointClamp, uv, 0);
        gtbnOcclusion = saturate(gtbnData.a);
        half3 packedGTBN = gtbnData.rgb * 2.0 - 1.0;
        half3 gtbnBentNormalWS = SafeNormalize(packedGTBN);
        gtbnBentNormalWS = dot(gtbnBentNormalWS, normalWS) > 0.0 ? gtbnBentNormalWS : normalWS;

        if (_LoogaBentNormalsEnabled != 0)
        {
            half bentNormalStrength = saturate((1.0 - gtbnOcclusion) * 4.0);
            bentNormalWS = SafeNormalize(lerp(normalWS, gtbnBentNormalWS, bentNormalStrength));
        }

        if (_LoogaGTBNDebugMode == 1)
            return half4(half3(gtbnData.a, gtbnData.a, gtbnData.a), outputAlpha);

        if (_LoogaGTBNDebugMode == 2)
            return half4(half3(1.0 - gtbnData.a, 1.0 - gtbnData.a, 1.0 - gtbnData.a), outputAlpha);

        if (_LoogaGTBNDebugMode == 3)
            return half4(gtbnBentNormalWS * 0.5h + 0.5h, outputAlpha);

        if (_LoogaGTBNDebugMode == 4)
            return half4(saturate(abs(bentNormalWS - normalWS) * 4.0), outputAlpha);

        if (_LoogaGTBNDebugMode == 5)
            return half4(half3(materialOcclusion, materialOcclusion, materialOcclusion), outputAlpha);

        if (_LoogaGTBNDebugMode == 6)
        {
            half combinedOcclusionDebug = saturate(materialOcclusion * gtbnOcclusion);
            return half4(half3(combinedOcclusionDebug, combinedOcclusionDebug, combinedOcclusionDebug), outputAlpha);
        }
    }

    AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(uv);
    half indirectSurfaceOcclusion = min(materialOcclusion, aoFactor.indirectAmbientOcclusion);
    half combinedOcclusion = saturate(indirectSurfaceOcclusion * gtbnOcclusion);
    // GetMainLight/GetAdditionalLight already apply URP's direct SSAO. The model
    // input only carries GTBN so direct ambient occlusion is not evaluated twice.
    half directOcclusion = gtbnOcclusion;

    half smoothness = gBufferData.smoothness;
    half perceptualRoughness = 1.0 - smoothness;

    half3 viewDirectionWS = SafeNormalize(GetCameraPositionWS() - positionWS);
    float NoV = saturate(dot(normalWS, viewDirectionWS));

    float3 finalColor = 0;
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);

    // Evaluate Main Light
    InputData lightInput = (InputData)0;
    lightInput.positionWS = positionWS;
    lightInput.normalWS = normalWS;
    lightInput.viewDirectionWS = viewDirectionWS;
    lightInput.normalizedScreenSpaceUV = uv;
    lightInput.shadowCoord = shadowCoord;

    Light mainLight = GetMainLight(lightInput, shadowMask, aoFactor);
    if (receiveShadowsOff)
        mainLight.shadowAttenuation = 1.0;
    float3 mainRadiance = mainLight.color * mainLight.shadowAttenuation * mainLight.distanceAttenuation;
    half3 directF0 = specularHighlightsOff ? half3(0.0, 0.0, 0.0) : f0;

    // NEW: Call the master global switch function
    #if defined(_LIGHT_LAYERS)
        bool mainLightMatchesLayers = IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers);
    #else
        bool mainLightMatchesLayers = true;
    #endif

    if (mainLightMatchesLayers)
        finalColor += EvaluateGlobalLoogaLighting(diffuseColor, directF0, perceptualRoughness, normalWS, GetLoogaMainLightDirectOcclusion(directOcclusion), viewDirectionWS, NoV, mainLight.direction, mainRadiance, modelParameters);

    // Add Transmission for the Sun
    if (mainLightMatchesLayers && hasBacklighting)
    {
        finalColor += EvaluateTransmission(ssssColor, ssssWidth, ambientScatterStrength,
            transmissionShadowSoftness, backlightRimPower, backlightDistortion,
            mainLight.direction, viewDirectionWS, normalWS,
            mainLight.color * mainLight.distanceAttenuation, mainLight.shadowAttenuation,
            transmissionMask);
    }
    if (mainLightMatchesLayers && isDualLobe)
    {
        finalColor += EvaluateSecondaryGGXLobe(directF0, secondaryRoughness, normalWS, mainLight.direction, viewDirectionWS, NoV, mainRadiance, lobeMix);
    }

    finalColor += EvaluateLoogaAdditionalLights(diffuseColor, directF0, perceptualRoughness, normalWS, directOcclusion, viewDirectionWS, NoV, modelParameters, lightInput, shadowMask, aoFactor, meshRenderingLayers, receiveShadowsOff, isDualLobe, secondaryRoughness, lobeMix);
    if (hasBacklighting)
    {
        finalColor += EvaluateLoogaAdditionalBacklights(lightInput, shadowMask, aoFactor,
            meshRenderingLayers, ssssColor, ssssWidth, ambientScatterStrength,
            transmissionShadowSoftness, backlightRimPower, backlightDistortion,
            normalWS, viewDirectionWS, transmissionMask, receiveShadowsOff);
    }

    // NEW: Call the master indirect switch function
    half indirectOcclusion = GetLoogaMetalIndirectOcclusion(combinedOcclusion, metallic);
    if (isLoogaMaterial && !environmentReflectionsOff)
    {
        finalColor += EvaluateGlobalLoogaIndirect(f0, perceptualRoughness, indirectOcclusion, viewDirectionWS, normalWS, bentNormalWS, NoV, positionWS, uv, modelParameters);
    }
    half bakedLightingOcclusion = 1.0;
    #if defined(_SCREEN_SPACE_OCCLUSION)
        // GBuffer3 already contains material AO. Match URP ClusterDeferred by
        // applying only the extra correction needed when SSAO is more occluded.
        bakedLightingOcclusion = aoFactor.indirectAmbientOcclusion < materialOcclusion
            ? aoFactor.indirectAmbientOcclusion * rcp(max(materialOcclusion, HALF_MIN))
            : 1.0;
    #endif
    if (_LoogaGTBNEnabled != 0)
        bakedLightingOcclusion *= lerp(1.0, gtbnOcclusion, _GTBNIndirectLightStrength);
    finalColor += bakedGIAndEmission * bakedLightingOcclusion;

    return half4(finalColor, outputAlpha);
}

half4 LoogaDeferredStencilClearFrag(Varyings input) : SV_Target
{
    return 0;
}
#endif
