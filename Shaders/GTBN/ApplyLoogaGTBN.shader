Shader "Hidden/LoogaSoft/ApplyGTBN"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Apply GTBN"
            ZWrite Off ZTest Always Cull Off
            ColorMask A // Ensure ONLY the Alpha channel of the GBuffer is affected
            Blend DstColor Zero // Multiply the existing GBuffer AO by the incoming GTBN AO

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 Frag(Varyings input) : SV_Target
            {
                // Grab the final occlusion value we just computed
                half gtbnOcclusion = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, input.texcoord, 0).a;
                return half4(1.0, 1.0, 1.0, gtbnOcclusion);
            }
            ENDHLSL
        }
    }
}
