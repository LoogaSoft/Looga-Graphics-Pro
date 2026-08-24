Shader "Hidden/LoogaSoft/SSSS"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        
        ZWrite Off 
        ZTest Always 
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        TEXTURE2D_X_HALF(_SSSSProfileTexture);
        TEXTURE2D_X_HALF(_SSSSProfileExtraTexture);
        int _LoogaSubsurfaceScatteringEnabled;

        // UPGRADE 1: 13-Tap non-linear profile (Jimenez-style)
        // Tighter near the center, wider at the edges to eliminate banding.
        static const float SSS_WEIGHTS[13] = { 0.003, 0.007, 0.015, 0.035, 0.080, 0.150, 0.420, 0.150, 0.080, 0.035, 0.015, 0.007, 0.003 };
        static const float SSS_OFFSETS[13] = { -2.0, -1.4, -0.9, -0.5, -0.2, -0.05, 0.0, 0.05, 0.2, 0.5, 0.9, 1.4, 2.0 };

        float SampleSceneDepthLod(float2 uv)
        {
            uv = ClampAndScaleUVForBilinear(UnityStereoTransformScreenSpaceTex(uv), _CameraDepthTexture_TexelSize.xy);
            return SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, uv, 0).r;
        }

        half4 PerformBlur(Varyings input, float2 direction)
        {
            float2 uv = input.texcoord;

            // 1. Read Profile Target FIRST
            half4 sssData = SAMPLE_TEXTURE2D_X_LOD(_SSSSProfileTexture, sampler_LinearClamp, uv, 0);
            half4 sssExtra = SAMPLE_TEXTURE2D_X_LOD(_SSSSProfileExtraTexture, sampler_PointClamp, uv, 0);

            // EARLY OUT: If the profile is blank, skip the expensive blur entirely.
            if (_LoogaSubsurfaceScatteringEnabled == 0 || sssData.a <= 0.001 || sssExtra.a < 0.5h)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
            }

            half4 centerColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
            float centerDepth = SampleSceneDepthLod(uv);
            float linearCenterDepth = LinearEyeDepth(centerDepth, _ZBufferParams);
            
            float localScatterWidth = sssData.a * 5.0; 
            half3 localSubsurfaceColor = sssData.rgb;

            // UPGRADE 2: Clamp the maximum pixel spread so it doesn't break when the camera gets super close
            float maxSpreadPixels = 35.0; 
            float calculatedSpread = (localScatterWidth * 25.0) / max(linearCenterDepth, 0.001);
            float spread = min(calculatedSpread, maxSpreadPixels);

            float2 texelSize = _ScreenSize.zw;
            float2 step = texelSize * direction * spread;

            half3 blurredColor = centerColor.rgb * SSS_WEIGHTS[6];
            float totalWeight = SSS_WEIGHTS[6];

            [unroll] for(int i = 0; i < 13; i++)
            {
                if (i == 6) continue;

                float2 offsetUV = uv + step * SSS_OFFSETS[i];
                float sampleDepth = SampleSceneDepthLod(offsetUV);
                float linearSampleDepth = LinearEyeDepth(sampleDepth, _ZBufferParams);

                // Slightly tighter depth rejection to preserve geometric edges
                float depthDiff = abs(linearCenterDepth - linearSampleDepth);
                float depthWeight = exp(-depthDiff * 12.0); 
                
                float weight = SSS_WEIGHTS[i] * depthWeight;

                half3 sampleColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, offsetUV, 0).rgb;
                blurredColor += sampleColor * weight;
                totalWeight += weight;
            }

            blurredColor /= max(totalWeight, 0.0001);

            half3 finalColor = lerp(centerColor.rgb, blurredColor, localSubsurfaceColor);

            return half4(finalColor, centerColor.a);
        }
        ENDHLSL

        // ====================================================================
        // PASS 0: HORIZONTAL BLUR
        // ====================================================================
        Pass
        {
            Name "Horizontal SSSS Blur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragHorizontal

            half4 FragHorizontal(Varyings input) : SV_Target
            {
                return PerformBlur(input, float2(1.0, 0.0));
            }
            ENDHLSL
        }

        // ====================================================================
        // PASS 1: VERTICAL BLUR
        // ====================================================================
        Pass
        {
            Name "Vertical SSSS Blur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragVertical

            half4 FragVertical(Varyings input) : SV_Target
            {
                return PerformBlur(input, float2(0.0, 1.0));
            }
            ENDHLSL
        }
    }
}
