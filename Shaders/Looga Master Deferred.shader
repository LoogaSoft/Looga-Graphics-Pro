Shader "Hidden/LoogaSoft/Lighting/MasterDeferred"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Looga Master Deferred Lighting"
            ZWrite Off ZTest Always ZClip False Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma exclude_renderers gles3 glcore
            #pragma vertex Vert
            #pragma fragment LoogaDeferredLightingFrag
            #define _CLUSTER_LIGHT_LOOP 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #define LOOGA_FIXED_LIGHTING_MODEL 0
            #pragma require cubearray
            #include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaLightingPass.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "Looga Clear Deferred Stencil"
            ZWrite Off ZTest Always ZClip False Cull Off
            ColorMask 0
            Stencil
            {
                Ref 0
                Comp Always
                Pass Replace
                WriteMask 96
            }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma exclude_renderers gles3 glcore
            #pragma vertex Vert
            #pragma fragment LoogaDeferredStencilClearFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 LoogaDeferredStencilClearFrag(Varyings input) : SV_Target
            {
                return 0.0h;
            }

            ENDHLSL
        }
    }
}
