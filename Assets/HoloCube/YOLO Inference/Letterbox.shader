Shader "HoloCube/YOLO Letterbox"
{
    Properties { _MainTex ("Camera image", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            // Bottom-left corner and size of the resized image inside the padded input.
            float4 _ImageRect;
            float _SourceIsSRGB;
            float4 frag(v2f_img input) : SV_Target
            {
                float2 uv = (input.uv - _ImageRect.xy) / _ImageRect.zw;
                if (any(uv < 0.0) || any(uv > 1.0))
                    return float4(114.0 / 255.0, 114.0 / 255.0, 114.0 / 255.0, 1.0);
                float4 color = tex2D(_MainTex, uv);
                #ifndef UNITY_COLORSPACE_GAMMA
                if (_SourceIsSRGB > 0.5) color.rgb = LinearToGammaSpace(color.rgb);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
