Shader "LoogaSoft/Grass"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo & Alpha", 2D) = "white" {}
        [Enum(Opaque, 0, Transparent, 1)] _Surface ("Surface Type", Float) = 0.0
        _Cull ("Render Face", Float) = 0.0
        [Enum(Mirror, 0, Flip, 1)] _BackfaceNormalMode ("Backface Normals", Float) = 1.0
        [ToggleUI] _AlphaClip ("Alpha Clipping", Float) = 1.0
        [ToggleUI] _ReceiveShadows ("Receive Shadows", Float) = 1.0
        [HideInInspector] _Blend ("__blend", Float) = 0.0
        [HideInInspector] _SrcBlend ("__src", Float) = 1.0
        [HideInInspector] _DstBlend ("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha ("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha ("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite ("__zw", Float) = 1.0
        [HideInInspector] _AlphaToMask ("__alphaToMask", Float) = 0.0
        [HideInInspector] _QueueOffset ("Queue Offset", Float) = 0.0
        _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.1

        _OrenNayarSigma ("Oren-Nayar Sigma", Range(0, 90)) = 30.0
        _MinnaertK ("Minnaert k", Range(0, 2)) = 0.7
        _OverwatchWrap ("Overwatch Wrap", Range(0, 0.5)) = 0.08
        _ArkaneBandCount ("Arkane Band Count", Range(1, 8)) = 3.0
        _ArkaneBandFeather ("Arkane Band Feather", Range(0.01, 0.5)) = 0.15
        [Enum(GGX, 0, Beckmann Approx., 1, Phong, 2)] _MinnaertIndirectSpecularModel ("Minnaert Indirect", Float) = 1.0
        [Enum(GGX, 0, Beckmann Approx., 1, Phong, 2)] _OrenNayarIndirectSpecularModel ("Oren-Nayar Indirect", Float) = 0.0

        [Toggle(_USE_SSSS)] _UseSSSS ("Enable SSSS", Float) = 1.0
        _SubsurfaceColor ("Subsurface Color", Color) = (0.6, 0.8, 0.2, 1.0)
        _AmbientScatterStrength ("Ambient Scatter Strength", Range(0.0, 5.0)) = 1.0
        _ScatterWidth ("Scatter Width", Range(0.1, 5.0)) = 1.5
        _ThicknessMap ("Thickness Map (Black=Glow, White=Solid)", 2D) = "black" {}
        _TransmissionStrength ("Transmission Strength", Range(0.0, 5.0)) = 1.0
        _TransmissionShadowSoftness ("Transmission Shadow Softness", Range(0.0, 1.0)) = 0.5
        [Toggle(_USE_BACKLIGHTING)] _UseBacklighting ("Enable Backlighting", Float) = 1.0
        _BacklightRimPower ("Backlight Rim Tightness", Range(1.0, 16.0)) = 4.0
        _BacklightDistortion ("Backlight Distortion", Range(0.0, 1.0)) = 0.2

        _WindInfluence ("Wind Influence", Range(0.0, 1.0)) = 1.0
        _WindTint ("Wind Gust Tint", Color) = (1.2, 1.2, 0.8, 1.0)
        _WindTintStrength ("Wind Tint Strength", Range(0, 1)) = 0.5

        _InteractionBend ("Interaction Bend Strength", Range(0.0, 5.0)) = 1.0

        _GlobalGridScale ("Global Grid Scale", Float) = 0.1
        _GlobalHueVar ("Global Hue Var", Vector) = (0, 0, 0, 0)
        _GlobalSatVar ("Global Sat Var", Vector) = (0, 0, 0, 0)
        _GlobalLumVar ("Global Lum Var", Vector) = (0, 0, 0, 0)

        _LocalNoiseScale ("Local Noise Scale", Float) = 1.0
        [Enum(Blocky, 0, Smooth, 1, Wavy, 2)] _LocalNoiseType ("Local Noise Type", Int) = 1
        _LocalHueVar ("Local Hue Var", Vector) = (0, 0, 0, 0)
        _LocalSatVar ("Local Sat Var", Vector) = (0, 0, 0, 0)
        _LocalLumVar ("Local Lum Var", Vector) = (0, 0, 0, 0)

        [ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0
        [ToggleOff] _EnvironmentReflections("Environment Reflections", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "Queue" = "AlphaTest" }
        Cull [_Cull]

        // =========================================================
        // 1. GBUFFER PASS
        // =========================================================
        Pass
        {
            Name "GBuffer"
            Tags { "LightMode" = "UniversalGBuffer" }
            Stencil
            {
                Ref 96
                Comp Always
                Pass Replace
                WriteMask 96
            }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            //#pragma shader_feature_local _SPECULARHIGHLIGHTS_OFF
            //#pragma shader_feature_local _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local_fragment _USE_BACKLIGHTING
            #pragma dynamic_branch_local_fragment _ _RECEIVE_SHADOWS_OFF
            #pragma dynamic_branch_local_fragment _ _SPECULARHIGHLIGHTS_OFF
            #pragma dynamic_branch_local_fragment _ _ENVIRONMENTREFLECTIONS_OFF
            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #pragma multi_compile_fragment _ _RENDER_PASS_ENABLED
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #include "LoogaFoliageCore.hlsl"
            #pragma require cubearray
            #define LOOGA_DISABLE_MODEL_REFLECTIONS 1
            #define LOOGA_DYNAMIC_MATERIAL_OPTIONS 1
            #include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaLightingHelpers.hlsl"

            FoliageVaryings Vert(FoliageAttributes input)
            {
                FoliageVaryings output = (FoliageVaryings)0;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                float3 interactionPushWS = ApplyGrassInteraction(positionWS, input.positionOS.xyz, _InteractionBend);
                float3 interactionPushOS = mul(GetWorldToObjectMatrix(), float4(interactionPushWS, 0.0)).xyz;
                input.positionOS.xyz += interactionPushOS;

                positionWS = TransformObjectToWorld(input.positionOS.xyz);
                input.positionOS.xyz = ApplyProceduralWind(input.positionOS.xyz, positionWS, 1.0, _WindInfluence);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);

                output.windGust = CalculateWindGust(output.positionWS);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = vertexInput.positionCS;
                output.uv = input.uv;
                output.normalWS = normalInput.normalWS;
                output.tangentWS = float4(normalInput.tangentWS, input.tangentOS.w);
                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
                #if defined(DYNAMICLIGHTMAP_ON)
                    output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
                #else
                    output.dynamicLightmapUV = 0.0;
                #endif
                OUTPUT_SH4(output.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(output.positionWS), output.vertexSH, output.probeOcclusion);

                return output;
            }

            #define FragmentOutput LoogaGBufferOutput

            FragmentOutput Frag(FoliageVaryings input, bool isFrontFace : SV_IsFrontFace)
            {
                FragmentOutput outGBuffer;

                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                if (_AlphaClip > 0.5) clip(albedo.a - _Cutoff);

                half3 finalAlbedo = GetVariedColor(albedo.rgb, input.positionWS);
                half3 windTintedColor = finalAlbedo * _WindTint.rgb;
                finalAlbedo = lerp(finalAlbedo, windTintedColor, input.windGust * _WindTintStrength);

                half4 normalSample = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv);
                half3 normalTS = UnpackNormalScale(normalSample, _BumpScale);

                half sign = input.tangentWS.w * GetOddNegativeScale();
                half3 bitangentWS = cross(input.normalWS, input.tangentWS.xyz) * sign;
                half3 normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS));
                normalWS = NormalizeNormalPerPixel(normalWS);
                normalWS = (!isFrontFace && _BackfaceNormalMode > 0.5) ? -normalWS : normalWS;

                half metallic = 0.0h;
                half3 specularF0 = kDielectricSpec.rgb;
                half occlusion = 1.0h;
                half smoothness = _Smoothness;
                ApplyLoogaDBuffer(input.positionCS, finalAlbedo, normalWS, metallic, specularF0, occlusion, smoothness);
                half3 diffuseColor = GetLoogaDiffuseColor(finalAlbedo, metallic, specularF0);
                half perceptualRoughness = 1.0 - smoothness;
                half4 modelParameters = LOOGA_SAMPLE_MODEL_PARAMETERS(input.uv);
                half3 bakedGI;
                half4 shadowMask;
                LOOGA_SAMPLE_BAKED_LIGHTING(input, normalWS, perceptualRoughness, modelParameters, bakedGI, shadowMask);
                half3 ambientDiffuse = EvaluateLoogaBakedDiffuse(diffuseColor, bakedGI, occlusion);

                #if defined(_USE_BACKLIGHTING)
                    half thickness = SAMPLE_TEXTURE2D(_ThicknessMap, sampler_BaseMap, input.uv).r;
                    half transmissionMask = (1.0 - thickness) * _TransmissionStrength;
                    outGBuffer.GBuffer0 = half4(finalAlbedo, PackLoogaMaterialFlags(GetLoogaCommonMaterialFlags()));
                    outGBuffer.GBuffer3 = half4(ambientDiffuse, transmissionMask);
                #else
                    outGBuffer.GBuffer0 = half4(finalAlbedo, PackLoogaMaterialFlags(GetLoogaCommonMaterialFlags()));
                    outGBuffer.GBuffer3 = half4(ambientDiffuse, 0);
                #endif

                outGBuffer.GBuffer1 = half4(PackLoogaGBufferSpecular(metallic, specularF0), occlusion);
                outGBuffer.GBuffer2 = half4(PackGBufferNormal(normalWS), smoothness);
                FillLoogaGBufferExtraOutputs(outGBuffer, input.positionCS.z, shadowMask);

                return outGBuffer;
            }
            ENDHLSL
        }

        // =========================================================
        // 2. FORWARD LIT PASS
        // =========================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
            ZWrite[_ZWrite]
            AlphaToMask[_AlphaToMask]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertForward
            #pragma fragment FragForward
            #pragma shader_feature_local_fragment _USE_BACKLIGHTING
            #pragma dynamic_branch_local_fragment _ _RECEIVE_SHADOWS_OFF
            #pragma dynamic_branch_local_fragment _ _SPECULARHIGHLIGHTS_OFF
            #pragma dynamic_branch_local_fragment _ _ENVIRONMENTREFLECTIONS_OFF
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #include "LoogaFoliageCore.hlsl"
            #pragma require cubearray
            #define LOOGA_DYNAMIC_MATERIAL_OPTIONS 1
            #include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaLightingHelpers.hlsl"
            #include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaMasterLighting.hlsl"

            FoliageVaryings VertForward(FoliageAttributes input)
            {
                FoliageVaryings output = (FoliageVaryings)0;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                float3 interactionPushWS = ApplyGrassInteraction(positionWS, input.positionOS.xyz, _InteractionBend);
                float3 interactionPushOS = mul(GetWorldToObjectMatrix(), float4(interactionPushWS, 0.0)).xyz;
                input.positionOS.xyz += interactionPushOS;

                positionWS = TransformObjectToWorld(input.positionOS.xyz);
                input.positionOS.xyz = ApplyProceduralWind(input.positionOS.xyz, positionWS, 1.0, _WindInfluence);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);

                output.windGust = CalculateWindGust(output.positionWS);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = vertexInput.positionCS;
                output.uv = input.uv;
                output.normalWS = normalInput.normalWS;
                output.tangentWS = float4(normalInput.tangentWS, input.tangentOS.w);
                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
                #if defined(DYNAMICLIGHTMAP_ON)
                    output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
                #else
                    output.dynamicLightmapUV = 0.0;
                #endif
                OUTPUT_SH4(output.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(output.positionWS), output.vertexSH, output.probeOcclusion);

                return output;
            }

            half4 FragForward(FoliageVaryings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                half4 albedoSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                if (_AlphaClip > 0.5) clip(albedoSample.a - _Cutoff);

                half3 finalAlbedo = GetVariedColor(albedoSample.rgb, input.positionWS);
                half3 windTintedColor = finalAlbedo * _WindTint.rgb;
                finalAlbedo = lerp(finalAlbedo, windTintedColor, input.windGust * _WindTintStrength);

                half4 normalSample = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv);
                half3 normalTS = UnpackNormalScale(normalSample, _BumpScale);

                half sign = input.tangentWS.w * GetOddNegativeScale();
                half3 bitangentWS = cross(input.normalWS, input.tangentWS.xyz) * sign;
                half3 normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS));
                normalWS = NormalizeNormalPerPixel(normalWS);
                normalWS = (!isFrontFace && _BackfaceNormalMode > 0.5) ? -normalWS : normalWS;

                half metallic = 0.0h;
                half3 f0 = kDielectricSpec.rgb;
                half occlusion = 1.0h;
                half smoothness = _Smoothness;
                ApplyLoogaDBuffer(input.positionCS, finalAlbedo, normalWS, metallic, f0, occlusion, smoothness);
                half perceptualRoughness = 1.0 - smoothness;
                half3 diffuseColor = GetLoogaDiffuseColor(finalAlbedo, metallic, f0);

                float3 viewDirWS = SafeNormalize(GetCameraPositionWS() - input.positionWS);
                float NoV = saturate(dot(normalWS, viewDirWS));
                half4 modelParameters = LOOGA_SAMPLE_MODEL_PARAMETERS(input.uv);
                half3 bakedGI;
                half4 shadowMask;
                LOOGA_SAMPLE_BAKED_LIGHTING(input, normalWS, perceptualRoughness, modelParameters, bakedGI, shadowMask);
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);

                Light mainLight = GetMainLight(shadowCoord, input.positionWS, shadowMask);
                ApplyLoogaReceiveShadowOption(mainLight);
                ApplyLoogaScreenSpaceDirectAO(mainLight, screenUV);
                uint meshRenderingLayers = GetMeshRenderingLayer();
                float3 color = EvaluateLoogaAdditionalLight(mainLight, meshRenderingLayers, diffuseColor, GetLoogaDirectSpecularF0(f0), perceptualRoughness, normalWS, 1.0, viewDirWS, NoV, modelParameters, false, false, 0.0, 0.0);

                #if defined(_USE_BACKLIGHTING)
                    half thickness = SAMPLE_TEXTURE2D(_ThicknessMap, sampler_BaseMap, input.uv).r;
                    half transmissionMask = (1.0 - thickness) * _TransmissionStrength;
                    color += EvaluateLoogaAdditionalBacklight(mainLight, meshRenderingLayers,
                        _SubsurfaceColor.rgb, _ScatterWidth, _AmbientScatterStrength,
                        _TransmissionShadowSoftness, _BacklightRimPower,
                        _BacklightDistortion, normalWS, viewDirWS, transmissionMask, false);
                    color += EvaluateLoogaAdditionalBacklights(input.positionWS, screenUV,
                        shadowMask, _SubsurfaceColor.rgb, _ScatterWidth,
                        _AmbientScatterStrength, _TransmissionShadowSoftness,
                        _BacklightRimPower, _BacklightDistortion, normalWS, viewDirWS,
                        transmissionMask, false);
                #endif

                color += EvaluateLoogaAdditionalLights(diffuseColor, GetLoogaDirectSpecularF0(f0), perceptualRoughness, normalWS, 1.0, viewDirWS, NoV, modelParameters, input.positionWS, screenUV, shadowMask, false, false, 0.0, 0.0);

                color += EvaluateLoogaBakedDiffuse(diffuseColor, bakedGI, occlusion);
                if (LoogaEnvironmentReflectionsEnabled())
                    color += EvaluateGlobalLoogaIndirect(f0, perceptualRoughness, occlusion, viewDirWS, normalWS, normalWS, NoV, input.positionWS, input.uv, modelParameters);

                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // =========================================================
        // 3. SSSS PROFILE PASS
        // =========================================================
        Pass
        {
            Name "SSSSProfile"
            Tags { "LightMode" = "SSSSProfile" }

            ZWrite Off
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex VertProfile
            #pragma fragment FragProfile
            #pragma shader_feature_local_fragment _USE_SSSS
            #pragma shader_feature_local_fragment _USE_BACKLIGHTING

            #include "LoogaFoliageCore.hlsl"

            VaryingsProfile VertProfile(AttributesProfile input)
            {
                VaryingsProfile output = (VaryingsProfile)0;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                input.positionOS.xyz = ApplyProceduralWind(input.positionOS.xyz, positionWS, 1.0, _WindInfluence);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            struct ProfileOutput
            {
                half4 profile : SV_Target0;
                half4 profileExtra : SV_Target1;
            };

            ProfileOutput FragProfile(VaryingsProfile input)
            {
                #if !defined(_USE_SSSS) && !defined(_USE_BACKLIGHTING)
                    discard;
                #endif

                if (_AlphaClip > 0.5) clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);

                half3 finalSSSS = GetVariedColor(_SubsurfaceColor.rgb, input.positionWS);
                ProfileOutput output;
                output.profile = half4(finalSSSS, _ScatterWidth / 5.0);
                #if defined(_USE_SSSS)
                    const half diffusionEnabled = 1.0h;
                #else
                    const half diffusionEnabled = 0.0h;
                #endif
                output.profileExtra = half4(_AmbientScatterStrength / 5.0,
                    _TransmissionShadowSoftness,
                    PackLoogaBacklightShape(_BacklightRimPower, _BacklightDistortion),
                    diffusionEnabled);
                return output;
            }
            ENDHLSL
        }

        // =========================================================
        // 4. META PASS
        // =========================================================
        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM
            #pragma vertex UniversalVertexMeta
            #pragma fragment UniversalFragmentMetaLit
            #pragma shader_feature EDITOR_VISUALIZATION

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitMetaPass.hlsl"
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
        UsePass "Hidden/LoogaSoft/Foliage Model Parameters/Grass Material Extras"
    }

    CustomEditor "LoogaSoft.Lighting.Editor.LoogaGrassShaderGUI"
    Fallback "Universal Render Pipeline/Lit"
}
