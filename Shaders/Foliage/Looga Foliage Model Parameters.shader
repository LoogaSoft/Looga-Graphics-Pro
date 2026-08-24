Shader "Hidden/LoogaSoft/Foliage Model Parameters"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "LoogaFoliageCore.hlsl"

        struct ModelParameterAttributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct ModelParameterVaryings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        struct ModelParameterOutput
        {
            half4 materialExtras : SV_Target0;
            half4 modelParameters : SV_Target1;
        };

        ModelParameterVaryings VertBarkParameters(ModelParameterAttributes input)
        {
            ModelParameterVaryings output = (ModelParameterVaryings)0;
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            input.positionOS.xyz = ApplyProceduralWind(input.positionOS.xyz, positionWS, 0.0, _WindInfluence);
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = input.uv;
            return output;
        }

        ModelParameterVaryings VertFoliageParameters(ModelParameterAttributes input)
        {
            ModelParameterVaryings output = (ModelParameterVaryings)0;
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            input.positionOS.xyz = ApplyProceduralWind(input.positionOS.xyz, positionWS, 1.0, _WindInfluence);
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = input.uv;
            return output;
        }

        ModelParameterVaryings VertGrassParameters(ModelParameterAttributes input)
        {
            ModelParameterVaryings output = (ModelParameterVaryings)0;
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            float3 interactionPushWS = ApplyGrassInteraction(positionWS, input.positionOS.xyz, _InteractionBend);
            input.positionOS.xyz += mul(GetWorldToObjectMatrix(), float4(interactionPushWS, 0.0)).xyz;
            positionWS = TransformObjectToWorld(input.positionOS.xyz);
            input.positionOS.xyz = ApplyProceduralWind(input.positionOS.xyz, positionWS, 1.0, _WindInfluence);
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = input.uv;
            return output;
        }

        ModelParameterOutput FragModelParameters(ModelParameterVaryings input)
        {
            ModelParameterOutput output;
            output.materialExtras = 0.0h;
            output.modelParameters = LOOGA_SAMPLE_MODEL_PARAMETERS(input.uv);
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "Bark Material Extras"
            Tags { "LightMode" = "LoogaMaterialExtras" }
            ZWrite Off
            ZTest Equal
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex VertBarkParameters
            #pragma fragment FragModelParameters
            ENDHLSL
        }

        Pass
        {
            Name "Foliage Material Extras"
            Tags { "LightMode" = "LoogaMaterialExtras" }
            ZWrite Off
            ZTest Equal
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex VertFoliageParameters
            #pragma fragment FragModelParameters
            ENDHLSL
        }

        Pass
        {
            Name "Grass Material Extras"
            Tags { "LightMode" = "LoogaMaterialExtras" }
            ZWrite Off
            ZTest Equal
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex VertGrassParameters
            #pragma fragment FragModelParameters
            ENDHLSL
        }
    }
}
