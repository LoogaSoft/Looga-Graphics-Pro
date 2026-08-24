Shader "LoogaSoft/Lit Lite"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [Enum(Specular, 0, Metallic, 1)] _WorkflowMode ("Workflow Mode", Float) = 1.0
        [Enum(Opaque, 0, Transparent, 1)] _Surface ("Surface Type", Float) = 0.0
        _Cull ("Render Face", Float) = 2.0
        [Enum(Mirror, 0, Flip, 1)] _BackfaceNormalMode ("Backface Normals", Float) = 0.0
        [ToggleUI] _AlphaClip ("Alpha Clipping", Float) = 0.0
        _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [ToggleUI] _ReceiveShadows ("Receive Shadows", Float) = 1.0
        [HideInInspector] _Blend ("__blend", Float) = 0.0
        [HideInInspector] _SrcBlend ("__src", Float) = 1.0
        [HideInInspector] _DstBlend ("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha ("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha ("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite ("__zw", Float) = 1.0
        [HideInInspector] _AlphaToMask ("__alphaToMask", Float) = 0.0
        [HideInInspector] _QueueOffset ("Queue Offset", Float) = 0.0
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0

        [Toggle(_USE_MASK_MAP)] _UseMaskMap ("Use Mask Map", Float) = 0.0
        _MaskMap ("Mask (R:Met, G:AO, A:Smooth)", 2D) = "white" {}
        _MetallicGlossMap ("Metallic Map", 2D) = "white" {}
        _SpecColor ("Specular", Color) = (0.2, 0.2, 0.2, 1.0)
        _SpecGlossMap ("Specular Map", 2D) = "white" {}
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _OcclusionMap ("Occlusion Map", 2D) = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1.0
        [Enum(Met Alpha, 0, Alb Alpha, 1)] _SmoothnessTextureChannel ("Smoothness Source", Float) = 0.0
        _BaseSmoothnessScale ("Smoothness", Range(0, 1)) = 0.5
        _EmissionMap("Emission Map", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)

        _OrenNayarSigma ("Oren-Nayar Sigma", Range(0, 90)) = 30.0
        _MinnaertK ("Minnaert k", Range(0, 2)) = 0.7
        _OverwatchWrap ("Overwatch Wrap", Range(0, 0.5)) = 0.08
        _ArkaneBandCount ("Arkane Band Count", Range(1, 8)) = 3.0
        _ArkaneBandFeather ("Arkane Band Feather", Range(0.01, 0.5)) = 0.15
        [Enum(GGX, 0, Beckmann Approx., 1, Phong, 2)] _MinnaertIndirectSpecularModel ("Minnaert Indirect", Float) = 1.0
        [Enum(GGX, 0, Beckmann Approx., 1, Phong, 2)] _OrenNayarIndirectSpecularModel ("Oren-Nayar Indirect", Float) = 0.0

        [Toggle(_USE_SSSS)] _UseSSSS ("Enable SSSS", Float) = 0.0
        _SubsurfaceColor ("Subsurface Color", Color) = (0.85, 0.4, 0.25, 1.0)
        _AmbientScatterStrength ("Ambient Scatter Strength", Range(0.0, 5.0)) = 1.0
        _ScatterWidth ("Scatter Width", Range(0.1, 5.0)) = 2.0
        _ThicknessMap ("Thickness Map (Black=Glow, White=Solid)", 2D) = "black" {}
        _TransmissionStrength ("Transmission Strength", Range(0.0, 5.0)) = 1.0
        _TransmissionShadowSoftness ("Transmission Shadow Softness", Range(0.0, 1.0)) = 0.5
        [Toggle(_USE_BACKLIGHTING)] _UseBacklighting ("Enable Backlighting", Float) = 0.0
        _BacklightRimPower ("Backlight Rim Tightness", Range(1.0, 16.0)) = 4.0
        _BacklightDistortion ("Backlight Distortion", Range(0.0, 1.0)) = 0.2

        [ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0
        [ToggleOff] _EnvironmentReflections("Environment Reflections", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "Queue" = "Geometry" }

        // =========================================================
        // 1. GBUFFER PASS
        // =========================================================
        Pass
        {
            Name "GBuffer"
            Tags { "LightMode" = "UniversalGBuffer" }
            Cull [_Cull]
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
            #pragma shader_feature_local_fragment _ _USE_MASK_MAP _SPECULAR_SETUP
            #pragma dynamic_branch_local_fragment _ _RECEIVE_SHADOWS_OFF
            #pragma dynamic_branch_local_fragment _ _SPECULARHIGHLIGHTS_OFF
            #pragma dynamic_branch_local_fragment _ _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local_fragment _EMISSION
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #pragma require cubearray
            #define LOOGA_DISABLE_MODEL_REFLECTIONS 1
            #define LOOGA_LITE_MATERIAL 1
            #define LOOGA_DYNAMIC_MATERIAL_OPTIONS 1
            #include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaLightingHelpers.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
                float2 dynamicLightmapUV : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 4);
                float2 dynamicLightmapUV : TEXCOORD5;
                float4 probeOcclusion : TEXCOORD6;
            };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap);
            TEXTURE2D(_SpecGlossMap);
            TEXTURE2D(_OcclusionMap);
            TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);
            TEXTURE2D(_EmissionMap);
            float4 _BumpMap_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _AlphaClip;
                float _Cutoff;
                float _BumpScale;
                float _BackfaceNormalMode;
                float4 _SpecColor;
                float _Metallic;
                float _OcclusionStrength;
                float _SmoothnessTextureChannel;
                float _BaseSmoothnessScale;
                float4 _EmissionColor;
            CBUFFER_END

            #define FragmentOutput LoogaGBufferOutput

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.uv = input.uv;
                output.normalWS = normalInput.normalWS;
                output.tangentWS = float4(normalInput.tangentWS, input.tangentOS.w);
                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
                #if defined(DYNAMICLIGHTMAP_ON)
                    output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
                #else
                    output.dynamicLightmapUV = 0.0;
                #endif
                OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);
                return output;
            }

            FragmentOutput Frag(Varyings input, bool isFrontFace : SV_IsFrontFace)
            {
                FragmentOutput outGBuffer;
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                if (_AlphaClip > 0.5) clip(albedo.a - _Cutoff);
                half4 normalSample = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv);

                half metallic = 0.0;
                half occlusion = 1.0;
                half baseSmoothness = 0.5;
                half3 specularF0 = kDielectricSpec.rgb;

                #if defined(_SPECULAR_SETUP)
                    half4 specGlossSample = SAMPLE_TEXTURE2D(_SpecGlossMap, sampler_BaseMap, input.uv);
                    half4 occlusionSample = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_BaseMap, input.uv);
                    specularF0 = specGlossSample.rgb * _SpecColor.rgb;
                    occlusion = lerp(1.0, occlusionSample.g, _OcclusionStrength);
                    baseSmoothness = (_SmoothnessTextureChannel == 1.0) ? (albedo.a * _BaseSmoothnessScale) : (specGlossSample.a * _BaseSmoothnessScale);
                #else
                    #if defined(_USE_MASK_MAP)
                        half4 maskSample = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, input.uv);
                        metallic = maskSample.r;
                        occlusion = maskSample.g;
                        baseSmoothness = maskSample.a * _BaseSmoothnessScale;
                    #else
                        half4 metallicSample = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_BaseMap, input.uv);
                        metallic = metallicSample.r * _Metallic;
                        half4 occlusionSample = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_BaseMap, input.uv);
                        occlusion = lerp(1.0, occlusionSample.g, _OcclusionStrength);
                        baseSmoothness = (_SmoothnessTextureChannel == 1.0) ? (albedo.a * _BaseSmoothnessScale) : (metallicSample.a * _BaseSmoothnessScale);
                    #endif
                #endif

                half3 normalTS = UnpackNormalScale(normalSample, _BumpScale);
                half sign = input.tangentWS.w * GetOddNegativeScale();
                half3 bitangentWS = cross(input.normalWS, input.tangentWS.xyz) * sign;
                half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS);
                half3 normalWS = TransformTangentToWorld(normalTS, tangentToWorld);
                normalWS = NormalizeNormalPerPixel(normalWS);
                normalWS = (!isFrontFace && _BackfaceNormalMode > 0.5) ? -normalWS : normalWS;
                ApplyLoogaDBuffer(input.positionCS, albedo.rgb, normalWS, metallic, specularF0, occlusion, baseSmoothness);

                half3 emission = 0.0;
                #if defined(_EMISSION)
                    emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_BaseMap, input.uv).rgb * _EmissionColor.rgb;
                #endif
                half3 diffuseColor = GetLoogaDiffuseColor(albedo.rgb, metallic, specularF0);
                half perceptualRoughness = 1.0 - baseSmoothness;
                half4 modelParameters = GetDefaultLoogaModelParameters();
                half3 bakedGI;
                half4 shadowMask;
                LOOGA_SAMPLE_BAKED_LIGHTING(input, normalWS, perceptualRoughness, modelParameters, bakedGI, shadowMask);
                half3 ambientDiffuse = EvaluateLoogaBakedDiffuse(diffuseColor, bakedGI, occlusion);

                uint loogaFlags = GetLoogaCommonMaterialFlags();

                outGBuffer.GBuffer3 = half4(emission + ambientDiffuse, 0.0);

                outGBuffer.GBuffer0 = half4(albedo.rgb, PackLoogaMaterialFlags(loogaFlags));
                half3 packedSpecular = PackLoogaGBufferSpecular(metallic, specularF0);
                outGBuffer.GBuffer1 = half4(packedSpecular, occlusion);
                outGBuffer.GBuffer2 = half4(PackGBufferNormal(normalWS), baseSmoothness);
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
            #pragma shader_feature_local_fragment _ _USE_MASK_MAP _SPECULAR_SETUP
            #pragma dynamic_branch_local_fragment _ _RECEIVE_SHADOWS_OFF
            #pragma dynamic_branch_local_fragment _ _SPECULARHIGHLIGHTS_OFF
            #pragma dynamic_branch_local_fragment _ _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local_fragment _EMISSION
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #pragma require cubearray
            #define LOOGA_LITE_MATERIAL 1
            #define LOOGA_DYNAMIC_MATERIAL_OPTIONS 1
            #include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaLightingHelpers.hlsl"
            #include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaMasterLighting.hlsl"

            struct AttributesForward
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
                float2 dynamicLightmapUV : TEXCOORD2;
            };

            struct VaryingsForward
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float3 normalWS : TEXCOORD3;
                float4 tangentWS : TEXCOORD4;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 5);
                float2 dynamicLightmapUV : TEXCOORD6;
                float4 probeOcclusion : TEXCOORD7;
            };

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap);
            TEXTURE2D(_SpecGlossMap);
            TEXTURE2D(_OcclusionMap);
            TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);
            TEXTURE2D(_EmissionMap);
            float4 _BumpMap_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _AlphaClip;
                float _Cutoff;
                float _BumpScale;
                float _BackfaceNormalMode;
                float4 _SpecColor;
                float _Metallic;
                float _OcclusionStrength;
                float _SmoothnessTextureChannel;
                float _BaseSmoothnessScale;
                float4 _EmissionColor;
            CBUFFER_END

            VaryingsForward VertForward(AttributesForward input)
            {
                VaryingsForward output = (VaryingsForward)0;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.uv = input.uv;
                output.normalWS = normalInput.normalWS;
                output.tangentWS = float4(normalInput.tangentWS, input.tangentOS.w);
                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
                #if defined(DYNAMICLIGHTMAP_ON)
                    output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
                #else
                    output.dynamicLightmapUV = 0.0;
                #endif
                OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);
                return output;
            }

            half4 FragForward(VaryingsForward input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                half4 albedoSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                if (_AlphaClip > 0.5) clip(albedoSample.a - _Cutoff);
                half3 albedo = albedoSample.rgb;
                half alpha = albedoSample.a;

                half4 normalSample = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv);
                half3 normalTS = UnpackNormalScale(normalSample, _BumpScale);
                half sign = input.tangentWS.w * GetOddNegativeScale();
                half3 bitangentWS = cross(input.normalWS, input.tangentWS.xyz) * sign;
                half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS);
                half3 normalWS = TransformTangentToWorld(normalTS, tangentToWorld);
                normalWS = NormalizeNormalPerPixel(normalWS);
                normalWS = (!isFrontFace && _BackfaceNormalMode > 0.5) ? -normalWS : normalWS;

                half metallic = 0.0;
                half occlusion = 1.0;
                half baseSmoothness = 0.5;
                half3 specularF0 = kDielectricSpec.rgb;

                #if defined(_SPECULAR_SETUP)
                    half4 specGlossSample = SAMPLE_TEXTURE2D(_SpecGlossMap, sampler_BaseMap, input.uv);
                    half4 occlusionSample = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_BaseMap, input.uv);
                    specularF0 = specGlossSample.rgb * _SpecColor.rgb;
                    occlusion = lerp(1.0, occlusionSample.g, _OcclusionStrength);
                    baseSmoothness = (_SmoothnessTextureChannel == 1.0) ? (albedoSample.a * _BaseSmoothnessScale) : (specGlossSample.a * _BaseSmoothnessScale);
                #else
                    #if defined(_USE_MASK_MAP)
                        half4 maskSample = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, input.uv);
                        metallic = maskSample.r;
                        occlusion = maskSample.g;
                        baseSmoothness = maskSample.a * _BaseSmoothnessScale;
                    #else
                        half4 metallicSample = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_BaseMap, input.uv);
                        metallic = metallicSample.r * _Metallic;
                        half4 occlusionSample = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_BaseMap, input.uv);
                        occlusion = lerp(1.0, occlusionSample.g, _OcclusionStrength);
                        baseSmoothness = (_SmoothnessTextureChannel == 1.0) ? (albedoSample.a * _BaseSmoothnessScale) : (metallicSample.a * _BaseSmoothnessScale);
                    #endif
                #endif

                ApplyLoogaDBuffer(input.positionCS, albedo, normalWS, metallic, specularF0, occlusion, baseSmoothness);

                half perceptualRoughness = 1.0 - baseSmoothness;
                #if defined(_SPECULAR_SETUP)
                    half3 f0 = specularF0;
                #else
                    half3 f0 = lerp(kDielectricSpec.rgb, albedo, metallic);
                #endif
                half3 diffuseColor = GetLoogaDiffuseColor(albedo, metallic, f0);
                half3 directF0 = GetLoogaDirectSpecularF0(f0);

                float3 viewDirWS = SafeNormalize(GetCameraPositionWS() - input.positionWS);
                float NoV = saturate(dot(normalWS, viewDirWS));
                half4 modelParameters = GetDefaultLoogaModelParameters();
                half3 bakedGI;
                half4 shadowMask;
                LOOGA_SAMPLE_BAKED_LIGHTING(input, normalWS, perceptualRoughness, modelParameters, bakedGI, shadowMask);
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);

                Light mainLight = GetMainLight(shadowCoord, input.positionWS, shadowMask);
                ApplyLoogaReceiveShadowOption(mainLight);
                ApplyLoogaScreenSpaceDirectAO(mainLight, screenUV);
                uint meshRenderingLayers = GetMeshRenderingLayer();
                float3 color = EvaluateLoogaAdditionalLight(mainLight, meshRenderingLayers, diffuseColor, directF0, perceptualRoughness, normalWS, 1.0, viewDirWS, NoV, modelParameters, false, false, 0.0, 0.0);

                color += EvaluateLoogaAdditionalLights(diffuseColor, directF0, perceptualRoughness, normalWS, 1.0, viewDirWS, NoV, modelParameters, input.positionWS, screenUV, shadowMask, false, false, 0.0, 0.0);

                color += EvaluateLoogaBakedDiffuse(diffuseColor, bakedGI, occlusion);
                if (LoogaEnvironmentReflectionsEnabled())
                {
                    half indirectOcclusion = GetLoogaMetalIndirectOcclusion(occlusion, metallic);
                    color += EvaluateGlobalLoogaIndirect(f0, perceptualRoughness, indirectOcclusion, viewDirWS, normalWS, normalWS, NoV, input.positionWS, input.uv, modelParameters);
                }
                #if defined(_EMISSION)
                    color += SAMPLE_TEXTURE2D(_EmissionMap, sampler_BaseMap, input.uv).rgb * _EmissionColor.rgb;
                #endif

                return half4(color, alpha);
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
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature EDITOR_VISUALIZATION

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitMetaPass.hlsl"
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }

    CustomEditor "LoogaSoft.Lighting.Editor.LoogaLitLiteShaderGUI"
    Fallback "Universal Render Pipeline/Lit"
}
