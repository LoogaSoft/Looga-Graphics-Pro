Shader "Hidden/LoogaSoft/Shadows/CasterCache"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // A cached level stores its static casters toroidally: level texel p lives at cache texel
        // (p + offset) mod resolution, so scrolling the level moves no stored texels.
        Texture2D<float> _LoogaShadowCasterCache;
        float4 _LoogaShadowCasterCacheOffset; // xy: wrap offset in texels, z: resolution. Whole numbers.

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        // A triangle over the whole viewport, with texture coordinates that copy one render texture
        // onto another without flipping.
        Varyings Vert(uint vertexID : SV_VertexID)
        {
            Varyings output;
            output.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
            output.uv = GetFullScreenTriangleTexCoord(vertexID);
            return output;
        }

        // Empties exposed cache texels before their casters are drawn.
        float FragClear(Varyings input) : SV_Depth
        {
            return UNITY_RAW_FAR_CLIP_VALUE;
        }

        // Writes the cached casters into a level's atlas tile, which then takes the moving casters.
        float FragUnwrap(Varyings input) : SV_Depth
        {
            uint resolution = (uint)round(_LoogaShadowCasterCacheOffset.z);
            uint2 levelTexel = min((uint2)floor(input.uv * resolution), resolution - 1u);
            uint2 cacheTexel = (levelTexel + (uint2)round(_LoogaShadowCasterCacheOffset.xy)) % resolution;
            return _LoogaShadowCasterCache.Load(int3(cacheTexel, 0));
        }
        ENDHLSL

        Pass
        {
            Name "Clear"
            ZTest Always
            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragClear
            ENDHLSL
        }

        Pass
        {
            Name "Unwrap"
            ZTest Always
            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragUnwrap
            ENDHLSL
        }
    }
}
