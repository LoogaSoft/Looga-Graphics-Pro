Shader "LoogaSoft/Crystal"
{
    Properties
    {
        [MainColor] _BaseColor ("Outer Shell Tint", Color) = (0.2, 0.8, 0.4, 0.5)
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
        _BumpMap ("Surface Normal", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0
        _Smoothness ("Outer Smoothness", Range(0.0, 1.0)) = 0.95

        [NoScaleOffset] _InnerMap ("Inner Cloud/Fractal Texture", 2D) = "white" {}
        [HDR] _InnerColor ("Core Glow Color", Color) = (0.5, 1.0, 0.3, 1.0)
        _ParallaxDepth ("Parallax Depth", Range(-1.0, 1.0)) = -0.3

        _ThicknessInfluence ("Geometric Edge Influence", Range(0.0, 1.0)) = 1.0
        _EdgeSharpness ("Edge Detection Sharpness", Range(1.0, 50.0)) = 10.0
        _CoreDensity ("Camera Fresnel Density", Range(0.1, 5.0)) = 2.0

        _Distortion ("Refraction Strength", Range(0.0, 0.5)) = 0.1

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
        // 1. FORWARD LIT PASS (Transparents only run in Forward)
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
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #pragma dynamic_branch_local_fragment _ _SPECULARHIGHLIGHTS_OFF
            #pragma dynamic_branch_local_fragment _ _ENVIRONMENTREFLECTIONS_OFF
            #pragma dynamic_branch_local_fragment _ _RECEIVE_SHADOWS_OFF

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            // NEW: Use the global switchboard
            #pragma require cubearray
            #define LOOGA_DYNAMIC_MATERIAL_OPTIONS 1
            #include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaMasterLighting.hlsl"

            struct AttributesCrystal
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float4 tangentOS    : TANGENT;
                float2 uv           : TEXCOORD0;
            };

            struct VaryingsCrystal
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float4 tangentWS    : TEXCOORD2;
                float3 viewDirWS    : TEXCOORD3;
                float3 viewDirTS    : TEXCOORD4;
                float4 screenPos    : TEXCOORD5;
                float3 positionWS   : TEXCOORD6;
            };

            TEXTURE2D(_BumpMap);  SAMPLER(sampler_BumpMap);
            TEXTURE2D(_InnerMap);   SAMPLER(sampler_InnerMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _InnerColor;
                float _BumpScale;
                float _BackfaceNormalMode;
                float _ParallaxDepth;
                float _ThicknessInfluence;
                float _EdgeSharpness;
                float _CoreDensity;
                float _Distortion;
                float _Smoothness;
            CBUFFER_END

            VaryingsCrystal Vert(AttributesCrystal input)
            {
                VaryingsCrystal output = (VaryingsCrystal)0;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.uv = input.uv;
                output.normalWS = normalInput.normalWS;
                output.tangentWS = float4(normalInput.tangentWS, input.tangentOS.w);
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(vertexInput.positionWS);

                half sign = input.tangentOS.w * GetOddNegativeScale();
                half3 bitangentWS = cross(normalInput.normalWS, normalInput.tangentWS) * sign;
                half3x3 worldToTangent = half3x3(normalInput.tangentWS, bitangentWS, normalInput.normalWS);
                output.viewDirTS = mul(worldToTangent, output.viewDirWS);

                output.screenPos = ComputeScreenPos(vertexInput.positionCS);
                return output;
            }

            half4 Frag(VaryingsCrystal input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                // 1. Outer Surface Normal
                half4 normalSample = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv);
                half3 normalTS = UnpackNormalScale(normalSample, _BumpScale);

                half sign = input.tangentWS.w * GetOddNegativeScale();
                half3 bitangentWS = cross(input.normalWS, input.tangentWS.xyz) * sign;
                half3 normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS));
                normalWS = NormalizeNormalPerPixel(normalWS);
                normalWS = (!isFrontFace && _BackfaceNormalMode > 0.5) ? -normalWS : normalWS;

                // 2. Multi-Layer Volumetric Core (Parallax)
                float2 parallaxVector = (input.viewDirTS.xy - normalTS.xy * 0.3) * _ParallaxDepth;
                float2 depthUV1 = input.uv - parallaxVector * 0.5;
                float2 depthUV2 = input.uv - parallaxVector * 1.0;

                half innerCloud1 = SAMPLE_TEXTURE2D(_InnerMap, sampler_InnerMap, depthUV1).r;
                half innerCloud2 = SAMPLE_TEXTURE2D(_InnerMap, sampler_InnerMap, depthUV2).r;
                half volumetricNoise = (innerCloud1 * 0.6) + (innerCloud2 * 0.4);

                // 3. Core Edge Masking
                float NoV = saturate(dot(normalWS, input.viewDirWS));
                float fresnelMask = pow(NoV, _CoreDensity);

                float normalChange = length(fwidth(normalWS));
                float pixelWorldSize = length(fwidth(input.positionWS));
                float edgeDelta = normalChange / max(pixelWorldSize, 0.0001);
                float geometricFaceMask = 1.0 - saturate(edgeDelta * 0.1 * _EdgeSharpness);
                float finalCoreMask = lerp(fresnelMask, geometricFaceMask, _ThicknessInfluence);
                half3 coreGlow = _InnerColor.rgb * volumetricNoise * finalCoreMask;

                // 4. Background Refraction
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float2 refractionOffset = normalTS.xy * _Distortion;

                float edgeFade = smoothstep(0.0, 0.1, screenUV.x) * smoothstep(1.0, 0.9, screenUV.x) * smoothstep(0.0, 0.1, screenUV.y) * smoothstep(1.0, 0.9, screenUV.y);
                screenUV += refractionOffset * edgeFade;

                half3 background = SampleSceneColor(screenUV);
                half3 transmission = lerp(background, background * _BaseColor.rgb, _BaseColor.a);

                // 5. Surface Specular & Reflections (Routed through Global Switch)
                half perceptualRoughness = 1.0 - _Smoothness;
                half3 f0 = half3(0.04, 0.04, 0.04);
                half3 lightingAccumulation = 0.0;
                half4 modelParameters = GetDefaultLoogaModelParameters();

                #if !defined(_SPECULARHIGHLIGHTS_OFF)
                    half4 shadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h);
                    float2 lightingScreenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                    Light mainLight = GetMainLight(shadowCoord, input.positionWS, shadowMask);
                    ApplyLoogaReceiveShadowOption(mainLight);
                    ApplyLoogaScreenSpaceDirectAO(mainLight, lightingScreenUV);
                    half3 mainRadiance = mainLight.color * mainLight.shadowAttenuation * mainLight.distanceAttenuation;

                    // Passing 0 diffuse routes only the stylized specular to the accumulation
                    lightingAccumulation += EvaluateLoogaAdditionalLight(mainLight, GetMeshRenderingLayer(), half3(0,0,0), f0, perceptualRoughness, normalWS, 1.0, input.viewDirWS, NoV, modelParameters, false, false, 0.0, 0.0);

                    lightingAccumulation += EvaluateLoogaAdditionalLights(half3(0, 0, 0), f0, perceptualRoughness, normalWS, 1.0, input.viewDirWS, NoV, modelParameters, input.positionWS, lightingScreenUV, shadowMask, false, false, 0.0, 0.0);
                #endif

                half3 indirectLighting = 0.0;
                #if !defined(_ENVIRONMENTREFLECTIONS_OFF)
                    indirectLighting = EvaluateGlobalLoogaIndirect(f0, perceptualRoughness, 1.0, input.viewDirWS, normalWS, normalWS, NoV, input.positionWS, input.uv, modelParameters);
                #endif

                // 6. Final Composite
                half3 finalColor = transmission + coreGlow + indirectLighting + lightingAccumulation;

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

    CustomEditor "LoogaSoft.Lighting.Editor.LoogaCrystalShaderGUI"
    Fallback "Universal Render Pipeline/Lit"
}
