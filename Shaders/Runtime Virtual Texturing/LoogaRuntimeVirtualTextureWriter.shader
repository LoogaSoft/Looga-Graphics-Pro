Shader "Hidden/LoogaSoft/Runtime Virtual Texture/Writer"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1
        _Metallic ("Metallic", Range(0, 1)) = 0
        _BaseSmoothnessScale ("Smoothness", Range(0, 1)) = 0
        _Smoothness ("URP Smoothness", Range(0, 1)) = 0
        _AlphaClip ("Alpha Clip", Float) = 0
        _AlphaClipThreshold ("URP Alpha Clip", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _LoogaRvtMask ("RVT Mask", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        Pass
        {
            Name "LoogaRuntimeVirtualTexture"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct FragmentOutput
            {
                half4 albedoCoverage : SV_Target0;
                half4 normalMaterial : SV_Target1;
                half4 heightMask : SV_Target2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _BumpScale;
                half _Metallic;
                half _BaseSmoothnessScale;
                half _Smoothness;
                half _AlphaClip;
                half _AlphaClipThreshold;
                half _Cutoff;
                half _LoogaRvtMask;
            CBUFFER_END

            float4 _LoogaRvtHeightRange;

            half2 EncodeOctahedralNormal(half3 normal)
            {
                normal /= abs(normal.x) + abs(normal.y) + abs(normal.z);
                half2 encoded = normal.xz;
                if (normal.y < 0.0h)
                {
                    encoded = (1.0h - abs(encoded.yx)) *
                        half2(encoded.x >= 0.0h ? 1.0h : -1.0h,
                              encoded.y >= 0.0h ? 1.0h : -1.0h);
                }
                return encoded * 0.5h + 0.5h;
            }

            half2 EncodeHeight(float normalizedHeight)
            {
                half clampedHeight = min(saturate(normalizedHeight), 0.99998h);
                half2 encoded = frac(clampedHeight * half2(1.0h, 255.0h));
                encoded.x -= encoded.y / 255.0h;
                return encoded;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = float4(normalInputs.tangentWS, input.tangentOS.w);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            FragmentOutput Frag(Varyings input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                FragmentOutput output;
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                if (max(_AlphaClip, _AlphaClipThreshold) > 0.5h)
                    clip(albedo.a - _Cutoff);

                half3 normalTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv),
                    _BumpScale);
                half tangentSign = input.tangentWS.w * GetOddNegativeScale();
                half3 bitangentWS = cross(input.normalWS, input.tangentWS.xyz) * tangentSign;
                half3x3 tangentToWorld = half3x3(
                    input.tangentWS.xyz,
                    bitangentWS,
                    input.normalWS);
                half3 normalWS = NormalizeNormalPerPixel(
                    TransformTangentToWorld(normalTS, tangentToWorld));

                half smoothness = max(_BaseSmoothnessScale, _Smoothness);
                float normalizedHeight =
                    (input.positionWS.y - _LoogaRvtHeightRange.x) * _LoogaRvtHeightRange.z;

                output.albedoCoverage = half4(albedo.rgb, 1.0h);
                output.normalMaterial = half4(
                    EncodeOctahedralNormal(normalWS),
                    saturate(smoothness),
                    saturate(_Metallic));
                output.heightMask = half4(
                    EncodeHeight(normalizedHeight),
                    saturate(_LoogaRvtMask),
                    1.0h);
                return output;
            }
            ENDHLSL
        }
    }
}
