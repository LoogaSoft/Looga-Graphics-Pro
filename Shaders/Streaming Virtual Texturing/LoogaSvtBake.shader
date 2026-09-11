Shader "Hidden/LoogaSoft/SVT/Bake"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            int _Channel;
            float4 frag(v2f_img i) : SV_Target
            {
                float4 value = tex2D(_MainTex, i.uv);
                if (_Channel == 0)
                {
                    #ifndef UNITY_COLORSPACE_GAMMA
                    value.rgb = LinearToGammaSpace(value.rgb);
                    #endif
                }
                if (_Channel == 1)
                {
                    value = float4(UnpackNormal(value) * 0.5 + 0.5, 1);
                }
                return value;
            }
            ENDHLSL
        }
    }
}
