Shader "Hidden/LoogaSoft/Impasto"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        #include "Packages/com.loogasoft.loogagraphicspro/Includes/LoogaImpastoCommon.hlsl"

        TEXTURE2D_X_FLOAT(_LoogaImpastoDepth);
        TYPED_TEXTURE2D_X(uint2, _LoogaImpastoStencil);
        TEXTURE2D_X(_LoogaImpastoGBuffer1);
        TEXTURE2D_X(_LoogaImpastoGBuffer2);

        TEXTURE2D(_LoogaImpastoStrokes);
        float4 _LoogaImpastoStrokes_TexelSize;

        float4 _LoogaImpastoParams0; // x stroke tile size (m), y normal strength, z detail replacement, w triplanar sharpness
        float4 _LoogaImpastoParams1; // x cavity occlusion, y ridge gloss, z overlap sharpness, w cell rotation
        float4 _LoogaImpastoParams2; // x fade start (m), y fade end (m), z hex cells per tile, w G-buffer normals are octahedral
        float4 _LoogaImpastoFootprint; // x world size of a pixel per metre of distance, y constant world size of a pixel

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
        };

        Varyings Vert(uint vertexID : SV_VertexID)
        {
            Varyings output;
            output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
            return output;
        }

        bool LoogaImpastoIsSky(float deviceDepth)
        {
        #if UNITY_REVERSED_Z
            return deviceDepth <= 0.000001;
        #else
            return deviceDepth >= 0.999999;
        #endif
        }

        float3 LoogaImpastoPosition(int2 pixel)
        {
            int2 size = (int2)_ScaledScreenParams.xy;
            pixel = clamp(pixel, int2(0, 0), size - 1);
            float deviceDepth = LOAD_TEXTURE2D_X(_LoogaImpastoDepth, pixel).r;
        #if !UNITY_REVERSED_Z
            deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, deviceDepth);
        #endif
            float2 uv = GetNormalizedScreenSpaceUV(float2(pixel) + 0.5);
            return ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
        }

        // The surface's shape without material normal maps. Each axis takes the neighbour on the same surface,
        // so silhouettes do not tilt the normal.
        float3 LoogaImpastoGeometricNormal(int2 pixel, float3 center)
        {
            float3 left = LoogaImpastoPosition(pixel + int2(-1, 0));
            float3 right = LoogaImpastoPosition(pixel + int2(1, 0));
            float3 down = LoogaImpastoPosition(pixel + int2(0, -1));
            float3 up = LoogaImpastoPosition(pixel + int2(0, 1));
            float3 dx = distance(right, center) < distance(left, center) ? right - center : center - left;
            float3 dy = distance(up, center) < distance(down, center) ? up - center : center - down;
            float3 normal = normalize(cross(dy, dx));
            float3 toCamera = _WorldSpaceCameraPos - center;
            return dot(normal, toCamera) < 0.0 ? -normal : normal;
        }

        float3 LoogaImpastoDecodeNormal(float3 packed)
        {
            if (_LoogaImpastoParams2.w > 0.5)
                return UnpackNormalOctQuadEncode(Unpack888ToFloat2(packed) * 2.0 - 1.0);
            return packed;
        }

        float3 LoogaImpastoEncodeNormal(float3 normal)
        {
            if (_LoogaImpastoParams2.w > 0.5)
                return PackFloat2To888(saturate(PackNormalOctQuadEncode(normal) * 0.5 + 0.5));
            return normal;
        }

        struct ImpastoOutput
        {
            half4 gBuffer1 : SV_Target0;
            half4 gBuffer2 : SV_Target1;
        };

        ImpastoOutput FragApply(Varyings input)
        {
            int2 pixel = (int2)input.positionCS.xy;
            half4 gBuffer1 = LOAD_TEXTURE2D_X(_LoogaImpastoGBuffer1, pixel);
            half4 gBuffer2 = LOAD_TEXTURE2D_X(_LoogaImpastoGBuffer2, pixel);
            ImpastoOutput output;
            output.gBuffer1 = gBuffer1;
            output.gBuffer2 = gBuffer2;

            float deviceDepth = LOAD_TEXTURE2D_X(_LoogaImpastoDepth, pixel).r;
            if (LoogaImpastoIsSky(deviceDepth))
                return output;

            // Materials with their own impasto layer already painted these pixels, anchored to their objects.
            uint stencil = GetStencilValue(LOAD_TEXTURE2D_X(_LoogaImpastoStencil, pixel).xy);
            if ((stencil & LOOGA_IMPASTO_STENCIL_BIT) != 0)
                return output;

            float3 positionWS = LoogaImpastoPosition(pixel);
            float distanceToCamera = distance(positionWS, _WorldSpaceCameraPos);
            float fade = 1.0 - smoothstep(_LoogaImpastoParams2.x, _LoogaImpastoParams2.y, distanceToCamera);
            if (fade <= 0.0)
                return output;

            float3 geometricNormal = LoogaImpastoGeometricNormal(pixel, positionWS);
            float3 materialNormal = normalize(LoogaImpastoDecodeNormal(gBuffer2.rgb));
            // Large shapes survive, fine material grain gives way to the paint.
            float3 baseNormal = normalize(lerp(materialNormal, geometricNormal, _LoogaImpastoParams0.z * fade));

            // Mip level from the pixel's footprint on the surface. Screen derivatives would read across
            // silhouettes in a full-screen pass.
            float3 viewDirection = normalize(_WorldSpaceCameraPos - positionWS);
            float pixelWorld = (distanceToCamera * _LoogaImpastoFootprint.x + _LoogaImpastoFootprint.y) /
                max(abs(dot(geometricNormal, viewDirection)), 0.25);
            float texelsPerMetre = _LoogaImpastoStrokes_TexelSize.z / _LoogaImpastoParams0.x;
            float lod = max(log2(max(pixelWorld * texelsPerMetre, 0.0001)), 0.0);

            float4 painted = LoogaImpastoTriplanar(
                TEXTURE2D_ARGS(_LoogaImpastoStrokes, sampler_LinearRepeat),
                positionWS / _LoogaImpastoParams0.x,
                baseNormal,
                LoogaImpastoAxisWeights(geometricNormal, _LoogaImpastoParams0.w),
                lod,
                _LoogaImpastoParams0.y * fade,
                _LoogaImpastoParams2.z,
                _LoogaImpastoParams1.w,
                _LoogaImpastoParams1.z);

            float cavity = LoogaImpastoCavity(painted.w, _LoogaImpastoParams1.x * fade);
            float smoothness = LoogaImpastoSmoothness(gBuffer2.a, painted.w, _LoogaImpastoParams1.y * fade);
            output.gBuffer1 = half4(gBuffer1.rgb, gBuffer1.a * cavity);
            output.gBuffer2 = half4(LoogaImpastoEncodeNormal(painted.xyz), smoothness);
            return output;
        }

        ImpastoOutput FragCopy(Varyings input)
        {
            int2 pixel = (int2)input.positionCS.xy;
            ImpastoOutput output;
            output.gBuffer1 = LOAD_TEXTURE2D_X(_LoogaImpastoGBuffer1, pixel);
            output.gBuffer2 = LOAD_TEXTURE2D_X(_LoogaImpastoGBuffer2, pixel);
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "Apply"
            ZTest Always ZWrite Off Cull Off Blend Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragApply
            ENDHLSL
        }

        Pass
        {
            Name "Copy"
            ZTest Always ZWrite Off Cull Off Blend Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragCopy
            ENDHLSL
        }
    }
}
