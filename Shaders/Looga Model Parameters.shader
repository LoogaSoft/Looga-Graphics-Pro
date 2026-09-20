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
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #pragma vertex VertModelParameters
            #pragma fragment FragModelParameters

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaModelParameters.hlsl"

            struct AttributesModelParameters
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct VaryingsModelParameters
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            LOOGA_DECLARE_MODEL_PARAMETER_TEXTURES;

            CBUFFER_START(UnityPerMaterial)
                LOOGA_MODEL_PARAMETER_CBUFFER_FIELDS;
            CBUFFER_END

            VaryingsModelParameters VertModelParameters(AttributesModelParameters input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                VaryingsModelParameters output = (VaryingsModelParameters)0;
                UNITY_TRANSFER_INSTANCE_ID(input, output);
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
                UNITY_SETUP_INSTANCE_ID(input);
                ModelParameterOutput output;
                output.materialExtras = 0.0h;
                output.modelParameters = LOOGA_SAMPLE_MODEL_PARAMETERS(input.uv);
                return output;
            }
            ENDHLSL
        }
    }
}
