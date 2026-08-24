Shader "LoogaSoft/Glass"
{
    Properties
    {
        [MainTexture] _BaseMap ("Dirt Albedo (RGB) & Opacity (A)", 2D) = "black" {}
        [MainColor] _BaseColor ("Glass Tint Color", Color) = (0.9, 0.95, 1.0, 1.0)
        [Enum(Specular, 0, Metallic, 1)] _WorkflowMode ("Workflow Mode", Float) = 1.0
        [Enum(Opaque, 0, Transparent, 1)] _Surface ("Surface Type", Float) = 1.0
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
        _MaskMap ("Mask Map (R:Metallic, G:AO, A:Smoothness)", 2D) = "white" {}

        _MetallicGlossMap ("Metallic Map", 2D) = "white" {}
        _SpecColor ("Specular", Color) = (0.2, 0.2, 0.2, 1.0)
        _SpecGlossMap ("Specular Map", 2D) = "white" {}
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _OcclusionMap ("Occlusion Map", 2D) = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1.0

        [Enum(Metallic Alpha, 0, Albedo Alpha, 1)] _SmoothnessTextureChannel ("Smoothness Source", Float) = 0.0
        _Smoothness ("Master Smoothness", Range(0.0, 1.0)) = 0.95

        _Distortion ("Refraction Strength", Range(0.0, 0.5)) = 0.05

        [ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0
        [ToggleOff] _EnvironmentReflections("Environment Reflections", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
        ZWrite[_ZWrite]
        AlphaToMask[_AlphaToMask]
        Cull [_Cull]

        // =========================================================
        // 1. FORWARD LIT PASS
        // =========================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #pragma shader_feature_local_fragment _ _USE_MASK_MAP _SPECULAR_SETUP
            #pragma dynamic_branch_local_fragment _ _RECEIVE_SHADOWS_OFF
            #pragma dynamic_branch_local_fragment _ _SPECULARHIGHLIGHTS_OFF
            #pragma dynamic_branch_local_fragment _ _ENVIRONMENTREFLECTIONS_OFF

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            // NEW: Use the global switchboard
            #pragma require cubearray
            #define LOOGA_DYNAMIC_MATERIAL_OPTIONS 1
            #include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaMasterLighting.hlsl"

            struct AttributesGlass
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float4 tangentOS    : TANGENT;
                float2 uv           : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
                float2 dynamicLightmapUV : TEXCOORD2;
            };

            struct VaryingsGlass
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float4 tangentWS    : TEXCOORD2;
                float3 viewDirWS    : TEXCOORD3;
                float4 screenPos    : TEXCOORD4;
                float3 positionWS   : TEXCOORD5;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 6);
                float2 dynamicLightmapUV : TEXCOORD7;
                float4 probeOcclusion : TEXCOORD8;
            };

            TEXTURE2D(_BaseMap);    SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);  SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MaskMap);    SAMPLER(sampler_MaskMap);
            TEXTURE2D(_MetallicGlossMap);
            TEXTURE2D(_SpecGlossMap);
            TEXTURE2D(_OcclusionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _BumpScale;
                float _BackfaceNormalMode;
                float _Distortion;
                float _Smoothness;
                float4 _SpecColor;
                float _Metallic;
                float _OcclusionStrength;
                float _SmoothnessTextureChannel;
            CBUFFER_END

            VaryingsGlass Vert(AttributesGlass input)
            {
                VaryingsGlass output = (VaryingsGlass)0;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.uv = input.uv;
                output.normalWS = normalInput.normalWS;
                output.tangentWS = float4(normalInput.tangentWS, input.tangentOS.w);
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(vertexInput.positionWS);

                output.screenPos = ComputeScreenPos(vertexInput.positionCS);
                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
                #if defined(DYNAMICLIGHTMAP_ON)
                    output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
                #else
                    output.dynamicLightmapUV = 0.0;
                #endif
                OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);
                return output;
            }

            half4 Frag(VaryingsGlass input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                // 1. Texture Sampling
                half4 dirtSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 normalSample = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv);

                half metallic = 0.0;
                half occlusion = 1.0;
                half baseSmoothness = 0.5;

                half3 specularF0 = half3(0.04, 0.04, 0.04);

                #if defined(_SPECULAR_SETUP)
                    half4 specGlossSample = SAMPLE_TEXTURE2D(_SpecGlossMap, sampler_BaseMap, input.uv);
                    half4 occlusionSample = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_BaseMap, input.uv);
                    specularF0 = specGlossSample.rgb * _SpecColor.rgb;
                    occlusion = lerp(1.0, occlusionSample.g, _OcclusionStrength);
                    baseSmoothness = (_SmoothnessTextureChannel == 1.0) ? (dirtSample.a * _Smoothness) : (specGlossSample.a * _Smoothness);
                #else
                    #if defined(_USE_MASK_MAP)
                        half4 maskSample = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, input.uv);
                        metallic = maskSample.r;
                        occlusion = maskSample.g;
                        baseSmoothness = maskSample.a * _Smoothness;
                    #else
                        half4 metallicSample = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_BaseMap, input.uv);
                        metallic = metallicSample.r * _Metallic;

                        half4 occlusionSample = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_BaseMap, input.uv);
                        occlusion = lerp(1.0, occlusionSample.g, _OcclusionStrength);

                        if (_SmoothnessTextureChannel == 1.0)
                            baseSmoothness = dirtSample.a * _Smoothness;
                        else
                            baseSmoothness = metallicSample.a * _Smoothness;
                    #endif
                #endif

                half perceptualRoughness = 1.0 - baseSmoothness;
                half roughness = perceptualRoughness * perceptualRoughness;
                #if defined(_SPECULAR_SETUP)
                    half3 f0 = specularF0;
                #else
                    half3 f0 = lerp(half3(0.04, 0.04, 0.04), dirtSample.rgb, metallic);
                #endif

                // 2. Normal Mapping
                half3 normalTS = UnpackNormalScale(normalSample, _BumpScale);
                half sign = input.tangentWS.w * GetOddNegativeScale();
                half3 bitangentWS = cross(input.normalWS, input.tangentWS.xyz) * sign;
                half3 normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS));
                normalWS = NormalizeNormalPerPixel(normalWS);
                normalWS = (!isFrontFace && _BackfaceNormalMode > 0.5) ? -normalWS : normalWS;

                // 3. Physical Fresnel
                float NoV = saturate(dot(normalWS, input.viewDirWS));
                float3 F = FresnelSchlick(f0, NoV);

                // 4. Refraction
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float2 refractionOffset = normalTS.xy * _Distortion;

                float edgeFade = smoothstep(0.0, 0.1, screenUV.x) * smoothstep(1.0, 0.9, screenUV.x) * smoothstep(0.0, 0.1, screenUV.y) * smoothstep(1.0, 0.9, screenUV.y);
                screenUV += refractionOffset * edgeFade;

                // 5. Calculate Background Transmission
                half3 background = SampleSceneColor(screenUV);
                half3 transmission = background * _BaseColor.rgb * (1.0 - F) * (1.0 - dirtSample.a);

                // 6. Dirt Diffuse & Specular Accumulation (Routed through Global Switch)
                half3 dirtDiffuse = dirtSample.rgb * (1.0 - metallic) * dirtSample.a;
                half3 lightingAccumulation = 0.0;
                half4 modelParameters = GetDefaultLoogaModelParameters();

                #if defined(_SPECULARHIGHLIGHTS_OFF)
                    f0 = half3(0.0, 0.0, 0.0); // Killing f0 prevents specular highlights, but keeps dirt diffuse intact!
                #endif

                half3 bakedGI;
                half4 shadowMask;
                LOOGA_SAMPLE_BAKED_LIGHTING(input, normalWS, perceptualRoughness, modelParameters, bakedGI, shadowMask);
                float2 lightingScreenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord, input.positionWS, shadowMask);
                ApplyLoogaReceiveShadowOption(mainLight);
                ApplyLoogaScreenSpaceDirectAO(mainLight, lightingScreenUV);
                half3 mainRadiance = mainLight.color * mainLight.shadowAttenuation * mainLight.distanceAttenuation;

                lightingAccumulation += EvaluateLoogaAdditionalLight(mainLight, GetMeshRenderingLayer(), dirtDiffuse, f0, perceptualRoughness, normalWS, 1.0, input.viewDirWS, NoV, modelParameters, false, false, 0.0, 0.0);

                lightingAccumulation += EvaluateLoogaAdditionalLights(dirtDiffuse, f0, perceptualRoughness, normalWS, 1.0, input.viewDirWS, NoV, modelParameters, input.positionWS, lightingScreenUV, shadowMask, false, false, 0.0, 0.0);

                // 7. Environment Reflection
                half3 indirectLighting = EvaluateLoogaBakedDiffuse(dirtDiffuse, bakedGI, occlusion);
                #if !defined(_ENVIRONMENTREFLECTIONS_OFF)
                    indirectLighting += EvaluateGlobalLoogaIndirect(f0, perceptualRoughness, occlusion, input.viewDirWS, normalWS, normalWS, NoV, input.positionWS, input.uv, modelParameters);
                #endif

                // 8. Final Composite
                half3 finalColor = transmission + lightingAccumulation + indirectLighting;

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // =========================================================
        // 2. META PASS
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
    }

    CustomEditor "LoogaSoft.Lighting.Editor.LoogaGlassShaderGUI"
    Fallback "Universal Render Pipeline/Lit"
}
