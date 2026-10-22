Shader "Hidden/Strata/Copy"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }

    SubShader
    {
        ZTest Always Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Mode, _SrgbData, _Lod;
            float4 _Tile;

            float4 frag(v2f_img i) : SV_Target
            {
                float4 c = tex2Dlod(_MainTex, float4(i.uv * _Tile.xy, 0, _Lod));
                if (_Mode > 1.5)
                {
                    float3 n = UnpackNormal(c);
                    return float4(n * 0.5 + 0.5, 1);
                }
                //! Undo sRGB decoding to preserve raw data values.
                if (_Mode > 0.5 && _SrgbData > 0.5) c.rgb = float3(LinearToGammaSpaceExact(c.r), LinearToGammaSpaceExact(c.g), LinearToGammaSpaceExact(c.b));
                return c;
            }
            ENDCG
        }
    }
}
