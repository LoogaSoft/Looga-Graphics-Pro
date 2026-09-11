Shader "Looga/Runtime Virtual Texture/Surface Blend"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (0.5, 0.5, 0.5, 1)
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 2)) = 1
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.3
        _RvtBlendDistance ("Surface Blend Distance", Float) = 2
        _RvtBlendStrength ("Surface Blend Strength", Range(0, 1)) = 1
        _RvtNormalStrength ("Surface Normal Strength", Range(0, 1)) = 1
        _RvtHeightOffset ("Surface Height Offset", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [HideInInspector] _LoogaRvtReceiverOnly ("Receiver Only", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
        #include "../../Includes/LoogaRuntimeVirtualTexture.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            half _Metallic;
            half _Smoothness;
            float _RvtBlendDistance;
            half _RvtBlendStrength;
            half _RvtNormalStrength;
            float _RvtHeightOffset;
            half _Cull;
            half _LoogaRvtReceiverOnly;
        CBUFFER_END

        struct ReceiverAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            float2 lightmapUV : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct ReceiverVaryings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            half4 tangentWS : TEXCOORD2;
            float2 uv : TEXCOORD3;
            half fog : TEXCOORD4;
            DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 5);
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        ReceiverVaryings ReceiverVertex(ReceiverAttributes input)
        {
            ReceiverVaryings output = (ReceiverVaryings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
            VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS, input.tangentOS);
            output.positionCS = positions.positionCS;
            output.positionWS = positions.positionWS;
            output.normalWS = normals.normalWS;
            output.tangentWS = half4(normals.tangentWS, input.tangentOS.w * GetOddNegativeScale());
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.fog = ComputeFogFactor(output.positionCS.z);
            OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
            OUTPUT_SH(output.normalWS, output.vertexSH);
            return output;
        }

        void ReceiverSurface(ReceiverVaryings input, out SurfaceData surface, out half3 normalWS)
        {
            surface = (SurfaceData)0;
            surface.albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
            surface.metallic = _Metallic;
            surface.smoothness = _Smoothness;
            surface.occlusion = 1;
            surface.alpha = 1;
            surface.normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
            half3 bitangent = cross(input.normalWS, input.tangentWS.xyz) * input.tangentWS.w;
            normalWS = normalize(mul(surface.normalTS, half3x3(input.tangentWS.xyz, bitangent, input.normalWS)));
            LoogaRuntimeVirtualTextureSample cached = LoogaRvtSample(input.positionWS);
            half weight = LoogaRvtSurfaceBlendWeight(cached, input.positionWS.y - _RvtHeightOffset, _RvtBlendDistance) * _RvtBlendStrength;
            surface.albedo = lerp(surface.albedo, cached.albedo, weight);
            surface.metallic = lerp(surface.metallic, cached.metallic, weight);
            surface.smoothness = lerp(surface.smoothness, cached.smoothness, weight);
            normalWS = normalize(lerp(normalWS, cached.normalWS, weight * _RvtNormalStrength));
        }

        half4 ReceiverFragment(ReceiverVaryings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            SurfaceData surface;
            half3 normalWS;
            ReceiverSurface(input, surface, normalWS);
            InputData lighting = (InputData)0;
            lighting.positionWS = input.positionWS;
            lighting.normalWS = normalWS;
            lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
            lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            lighting.bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, normalWS);
            lighting.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);
            lighting.vertexLighting = VertexLighting(input.positionWS, normalWS);
            lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            half4 color = UniversalFragmentPBR(lighting, surface);
            color.rgb = MixFog(color.rgb, input.fog);
            return color;
        }

        half4 ReceiverNormals(ReceiverVaryings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            SurfaceData surface;
            half3 normalWS;
            ReceiverSurface(input, surface, normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(normalWS);
                return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0);
            #else
                return half4(normalWS, 0);
            #endif
        }

        half4 ReceiverDepth(ReceiverVaryings input) : SV_Target
        {
            return 0;
        }

        float3 _LightDirection;
        float3 _LightPosition;
        ReceiverVaryings ReceiverShadowVertex(ReceiverAttributes input)
        {
            ReceiverVaryings output = ReceiverVertex(input);
            float3 direction = _LightDirection;
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                direction = normalize(_LightPosition - output.positionWS);
            #endif
            output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS, output.normalWS, direction));
            #if UNITY_REVERSED_Z
                output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
            #else
                output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE * output.positionCS.w);
            #endif
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull [_Cull]
            ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ReceiverVertex
            #pragma fragment ReceiverFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            Cull [_Cull]
            ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ReceiverVertex
            #pragma fragment ReceiverNormals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull [_Cull]
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ReceiverVertex
            #pragma fragment ReceiverDepth
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull [_Cull]
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ReceiverShadowVertex
            #pragma fragment ReceiverDepth
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
    }
}
