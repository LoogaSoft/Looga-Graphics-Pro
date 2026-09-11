Shader "Looga/Streaming Virtual Texture/Lit"
{
    Properties
    {
        [Enum(Lit,0,Albedo,1,Normal,2,Mask,3)] _SvtDebugView ("Debug View", Float) = 0
        _SvtUV ("Tiling XY / Offset ZW", Vector) = (1,1,0,0)
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _BumpScale ("Normal Strength", Range(0, 2)) = 1
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 1
        [HideInInspector] _LoogaSvtId ("Stack ID", Float) = 0
        [HideInInspector] _LoogaSvtLayout ("Layout", Vector) = (128,128,8,1)
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
        #include "../../Includes/LoogaStreamingVirtualTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _SvtUV;
            float _SvtDebugView;
            half4 _BaseColor;
            half _BumpScale;
            half _Metallic;
            half _Smoothness;
            float _LoogaSvtId;
            float4 _LoogaSvtLayout;
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
            output.uv = input.uv * _SvtUV.xy + _SvtUV.zw;
            output.fog = ComputeFogFactor(output.positionCS.z);
            OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
            OUTPUT_SH(output.normalWS, output.vertexSH);
            return output;
        }

        void ReceiverSurface(ReceiverVaryings input, out SurfaceData surface, out half3 normalWS)
        {
            surface = (SurfaceData)0;
            LoogaSvtSurface streamed = LoogaSvtSample(input.uv, _LoogaSvtLayout, (uint)_LoogaSvtId);
            surface.albedo = streamed.albedo * _BaseColor.rgb;
            surface.metallic = saturate(streamed.mask.r + _Metallic);
            surface.smoothness = streamed.mask.a * _Smoothness;
            surface.occlusion = streamed.mask.g;
            surface.alpha = 1;
            surface.normalTS = normalize(float3(streamed.normalTS.xy * _BumpScale, streamed.normalTS.z));
            half3 bitangent = cross(input.normalWS, input.tangentWS.xyz) * input.tangentWS.w;
            normalWS = normalize(mul(surface.normalTS, half3x3(input.tangentWS.xyz, bitangent, input.normalWS)));
        }

        half4 ReceiverFragment(ReceiverVaryings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            SurfaceData surface;
            half3 normalWS;
            ReceiverSurface(input, surface, normalWS);
            if (_SvtDebugView > 2.5) return half4(surface.metallic, surface.occlusion, surface.smoothness, 1);
            if (_SvtDebugView > 1.5) return half4(surface.normalTS * 0.5 + 0.5, 1);
            if (_SvtDebugView > 0.5) return half4(surface.albedo, 1);
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
        float _LoogaSvtFeedbackScale;
        uint ReceiverFeedback(ReceiverVaryings input) : SV_Target
        {
            float2 uv = input.positionCS.xy / (_ScaledScreenParams.xy * _LoogaSvtFeedbackScale);
            float sceneDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            float fragmentDepth = -TransformWorldToView(input.positionWS).z;
            clip(sceneDepth + max(0.05, sceneDepth * 0.0001) - fragmentDepth);
            return LoogaSvtFeedback(input.uv, _LoogaSvtLayout, (uint)_LoogaSvtId, _LoogaSvtFeedbackScale);
        }
        ENDHLSL

        Pass
        {
            Name "Feedback"
            Tags { "LightMode"="LoogaSvtFeedback" }
            Cull [_Cull]
            ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ReceiverVertex
            #pragma fragment ReceiverFeedback
            ENDHLSL
        }
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
