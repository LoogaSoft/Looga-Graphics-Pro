Shader "Hidden/LoogaSoft/Runtime Virtual Texture/Clear"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 Vert(uint id : SV_VertexID) : SV_POSITION
            {
                return GetFullScreenTriangleVertexPosition(id);
            }
            struct Output
            {
                float4 albedo : SV_Target0;
                float4 normal : SV_Target1;
                float4 height : SV_Target2;
            };
            Output Frag() { return (Output)0; }
            ENDHLSL
        }
    }
}
