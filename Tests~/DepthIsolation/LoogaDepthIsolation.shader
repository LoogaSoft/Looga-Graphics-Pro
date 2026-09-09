Shader "Hidden/Tests/LoogaDepthIsolation"
{
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D_X_FLOAT(_LoogaValidationExpectedDepth);
            float4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float expected=LOAD_TEXTURE2D_X(_LoogaValidationExpectedDepth,uint2(i.positionCS.xy)).r;
                float actual=LoadSceneDepth(uint2(i.positionCS.xy));
                return float4(actual==expected?0:1,expected,actual,1);
            }
            ENDHLSL
        }
    }
}
