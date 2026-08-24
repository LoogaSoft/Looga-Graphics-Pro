Shader "Hidden/LoogaSoft/Model Parameters"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Looga Material Extras"
            Tags { "LightMode" = "LoogaMaterialExtras" }

            ZWrite Off
            ZTest Equal
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex VertModelParameters
            #pragma fragment FragModelParameters

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaModelParameters.hlsl"

            struct AttributesModelParameters
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct VaryingsModelParameters
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            LOOGA_DECLARE_MODEL_PARAMETER_TEXTURES;

            CBUFFER_START(UnityPerMaterial)
                LOOGA_MODEL_PARAMETER_CBUFFER_FIELDS;
            CBUFFER_END

            VaryingsModelParameters VertModelParameters(AttributesModelParameters input)
            {
                VaryingsModelParameters output = (VaryingsModelParameters)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            struct ModelParameterOutput
            {
                half4 materialExtras : SV_Target0;
                half4 modelParameters : SV_Target1;
            };

            ModelParameterOutput FragModelParameters(VaryingsModelParameters input)
            {
                ModelParameterOutput output;
                output.materialExtras = 0.0h;
                output.modelParameters = LOOGA_SAMPLE_MODEL_PARAMETERS(input.uv);
                return output;
            }
            ENDHLSL
        }
    }
}
