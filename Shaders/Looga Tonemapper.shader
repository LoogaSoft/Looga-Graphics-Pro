Shader "Hidden/LoogaSoft/Tonemapper"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "LoogaTonemap"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            int _TonemapMode;
            float _PreExposure;
            float _PostExposure;
            
            float _BlackPoint;
            float _WhitePoint;
            float _Contrast;
            float _Saturation;
            
            float _SigmoidCurve;
            float _ReinhardLimit;

            static const float LOOGA_MIDDLE_GRAY = 0.18;

            // AgX's fitted curve maps 18% gray to 18% after this input
            // compensation. It keeps mode changes exposure-neutral without
            // altering AgX's highlight and color behavior.
            static const float AGX_MIDDLE_GRAY_EXPOSURE = 0.8084177705;
            static const float PBR_NEUTRAL_MIDDLE_GRAY_EXPOSURE = 1.2222222222;

            // --- 0: AgX (Base Contrast) ---
            float3 AgX(float3 color)
            {
                // These matrices are transposed from the GLSL reference so
                // mul(matrix, vector) has the same orientation in HLSL.
                const float3x3 agx_mat = float3x3(
                    0.842479062253094, 0.0784335999999992, 0.0792237451477643,
                    0.0423282422610123, 0.878468636469772, 0.0791661274605434,
                    0.0423756549057051, 0.0784336, 0.879142973793104
                );
                const float3x3 agx_mat_inv = float3x3(
                    1.19687900512017, -0.0980208811401368, -0.0990297440797205,
                    -0.0528968517574562, 1.15190312990417, -0.0989611768448433,
                    -0.0529716355144438, -0.0980434501171241, 1.15107367264116
                );

                const float minEv = -12.47393;
                const float maxEv = 4.026069;

                float3 val = max(mul(agx_mat, color), 1e-10);
                float3 x = saturate((log2(val) - minEv) / (maxEv - minEv));
                
                float3 x2 = x * x;
                float3 x4 = x2 * x2;
                float3 y = 15.5 * x4 * x2 - 40.14 * x4 * x + 31.96 * x4 - 6.868 * x2 * x + 0.4298 * x2 + 0.1191 * x - 0.00232;

                // The fitted curve is display encoded. Convert it back to
                // linear light before Unity writes to an sRGB target.
                float3 displayLinear = max(mul(agx_mat_inv, y), 0.0);
                return pow(displayLinear, 2.2);
            }

            // --- 1: Khronos PBR Neutral ---
            float3 KhronosPBRNeutral(float3 color)
            {
                const float startCompression = 0.8 - 0.04;
                const float desaturation = 0.15;

                float x = min(color.r, min(color.g, color.b));
                float offset = x < 0.08 ? x - 6.25 * x * x : 0.04;
                color -= offset;

                float peak = max(color.r, max(color.g, color.b));
                if (peak < startCompression) return color;

                float d = 1.0 - startCompression;
                float newPeak = 1.0 - d * d / (peak + d - startCompression);
                color *= newPeak / peak;

                float g = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
                return lerp(color, newPeak.xxx, g);
            }

            // --- 2: Sigmoid (generalized log-logistic, zero skew) ---
            float3 Sigmoid(float3 color)
            {
                float curveContrast = max(_SigmoidCurve, 0.01);
                float3 filmResponse = pow(max(color, 0.0), curveContrast);
                float pivotResponse = pow(LOOGA_MIDDLE_GRAY, curveContrast);
                float paperExposure = pivotResponse * (rcp(LOOGA_MIDDLE_GRAY) - 1.0);
                return filmResponse / (paperExposure + filmResponse);
            }

            // --- 3: Reinhard Extended ---
            float3 ReinhardExtended(float3 color)
            {
                float lum = Luminance(color);
                if (lum <= 1e-6)
                    return 0.0;

                float limit = max(_ReinhardLimit, 1e-3);
                float num = lum * (1.0 + (lum / (limit * limit)));
                float newLum = num / (1.0 + lum);
                return color * (newLum / lum);
            }

            float GetTonemapExposureCompensation()
            {
                if (_TonemapMode == 0)
                    return AGX_MIDDLE_GRAY_EXPOSURE;

                if (_TonemapMode == 1)
                    return PBR_NEUTRAL_MIDDLE_GRAY_EXPOSURE;

                if (_TonemapMode == 3)
                {
                    // Solve extended Reinhard for the input that produces
                    // 18% output at the selected white point.
                    float limit = max(_ReinhardLimit, 1e-3);
                    float limitSquared = limit * limit;
                    float oneMinusGray = 1.0 - LOOGA_MIDDLE_GRAY;
                    float inputAtMiddleGray = 0.5 * limitSquared *
                        (-oneMinusGray + sqrt(oneMinusGray * oneMinusGray +
                        4.0 * LOOGA_MIDDLE_GRAY / limitSquared));
                    return inputAtMiddleGray / LOOGA_MIDDLE_GRAY;
                }

                // The sigmoid curve is analytically anchored at middle gray.
                return 1.0;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                
                float4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float3 color = source.rgb;
                
                // 1. User exposure and mode-neutral middle-gray calibration.
                color *= _PreExposure * GetTonemapExposureCompensation();

                // 2. Tonemap curve.
                if (_TonemapMode == 0)      color = AgX(color);
                else if (_TonemapMode == 1) color = KhronosPBRNeutral(color);
                else if (_TonemapMode == 2) color = Sigmoid(color);
                else if (_TonemapMode == 3) color = ReinhardExtended(color);

                // --- GLOBAL COLOR GRADING BLOCK ---
                
                // 3A. Levels (Black Point / White Point Mapping)
                // This remaps the lowest and highest values, effectively crushing or stretching the dynamic range.
                color = saturate((color - _BlackPoint) / (_WhitePoint - _BlackPoint + 1e-5));
                
                // 3B. Contrast (Power Curve)
                // Applied in linear display space to push midtones apart.
                color = pow(max(color, 0.0), _Contrast);
                
                // 3C. Saturation
                float lum = dot(color, float3(0.2126, 0.7152, 0.0722));
                color = lerp(lum.xxx, color, _Saturation);

                // 4. Post-exposure
                color *= _PostExposure;

                return half4(color, source.a);
            }
            ENDHLSL
        }
    }
}
