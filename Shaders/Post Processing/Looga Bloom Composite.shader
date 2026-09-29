Shader "Hidden/LoogaSoft/BloomComposite"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "LoogaBloomComposite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // _BlitTexture is the scene color. _LoogaBloomTexture is the pyramid bloom at half size, or the FFT output
            // buffer with the bloom in a region at its origin.
            TEXTURE2D(_LoogaBloomTexture);
            TEXTURE2D(_LoogaLensDirtTexture);
            SAMPLER(sampler_LoogaLensDirtTexture);

            // 1 / width, 1 / height of the bloom texture.
            float4 _LoogaBloomTexelSize;
            // Screen UV to bloom texture UV: scale, offset.
            float4 _LoogaBloomScaleOffset;
            // Intensity, lens dirt intensity.
            float4 _LoogaBloomParams;
            float4 _LoogaBloomTint;
            // Threshold, threshold - knee, 2 * knee, 0.25 / knee. It matches the prefilter threshold.
            float4 _LoogaBloomThreshold;
            // Lens dirt UV scale and offset for a cover fit.
            float4 _LoogaLensDirtScaleOffset;

            // The tent filter removes the texel pattern of the half-size bloom.
            float3 SampleBloom(float2 uv)
            {
                float4 offset = _LoogaBloomTexelSize.xyxy * float4(1.0, 1.0, -1.0, 0.0);
                float3 sum = SAMPLE_TEXTURE2D_LOD(_LoogaBloomTexture, sampler_LinearClamp, uv - offset.xy, 0).rgb;
                sum += SAMPLE_TEXTURE2D_LOD(_LoogaBloomTexture, sampler_LinearClamp, uv - offset.wy, 0).rgb * 2.0;
                sum += SAMPLE_TEXTURE2D_LOD(_LoogaBloomTexture, sampler_LinearClamp, uv - offset.zy, 0).rgb;
                sum += SAMPLE_TEXTURE2D_LOD(_LoogaBloomTexture, sampler_LinearClamp, uv + offset.zw, 0).rgb * 2.0;
                sum += SAMPLE_TEXTURE2D_LOD(_LoogaBloomTexture, sampler_LinearClamp, uv, 0).rgb * 4.0;
                sum += SAMPLE_TEXTURE2D_LOD(_LoogaBloomTexture, sampler_LinearClamp, uv + offset.xw, 0).rgb * 2.0;
                sum += SAMPLE_TEXTURE2D_LOD(_LoogaBloomTexture, sampler_LinearClamp, uv + offset.zy, 0).rgb;
                sum += SAMPLE_TEXTURE2D_LOD(_LoogaBloomTexture, sampler_LinearClamp, uv + offset.wy, 0).rgb * 2.0;
                sum += SAMPLE_TEXTURE2D_LOD(_LoogaBloomTexture, sampler_LinearClamp, uv + offset.xy, 0).rgb;
                // FFT rounding can give small negative values.
                return max(sum * (1.0 / 16.0), 0.0);
            }

            // The same soft-knee curve as the prefilter. It returns the part of the light that enters the bloom.
            float3 ScatteredLight(float3 color)
            {
                color = clamp(color, 0.0, 65000.0);
                if (_LoogaBloomThreshold.x <= 0.0)
                {
                    return color;
                }
                float brightness = Max3(color.r, color.g, color.b);
                float soft = clamp(brightness - _LoogaBloomThreshold.y, 0.0, _LoogaBloomThreshold.z);
                soft = soft * soft * _LoogaBloomThreshold.w;
                return color * max(soft, brightness - _LoogaBloomThreshold.x) / max(brightness, 1e-4);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float4 scene = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
                float3 bloom = SampleBloom(uv * _LoogaBloomScaleOffset.xy + _LoogaBloomScaleOffset.zw) * _LoogaBloomTint.rgb;
                float2 dirtUV = uv * _LoogaLensDirtScaleOffset.xy + _LoogaLensDirtScaleOffset.zw;
                float3 dirt = SAMPLE_TEXTURE2D_LOD(_LoogaLensDirtTexture, sampler_LoogaLensDirtTexture, dirtUV, 0).rgb;
                float intensity = _LoogaBloomParams.x;
                // The lens moves the scattered part of each pixel into the bloom, so the bloom conserves energy.
                // Light below the threshold does not scatter and stays in place. Lens dirt adds light.
                float3 color = scene.rgb - ScatteredLight(scene.rgb) * intensity +
                    bloom * (intensity + dirt * _LoogaBloomParams.y);
                return half4(color, scene.a);
            }
            ENDHLSL
        }
    }
}
