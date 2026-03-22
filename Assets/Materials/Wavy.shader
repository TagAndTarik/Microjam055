Shader "Custom/ScreenGlitchHorror"
{
    Properties
    {
        _MainTex ("Base (Screen)", 2D) = "white" {}

        _Intensity ("Glitch Intensity", Range(0,1)) = 0.5
        _ScanlineStrength ("Scanline Strength", Range(0,1)) = 0.3
        _ColorOffset ("Color Offset", Float) = 0.002
    }

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

            float _Intensity;
            float _ScanlineStrength;
            float _ColorOffset;

            // Simple random
            float rand(float2 co)
            {
                return frac(sin(dot(co.xy ,float2(12.9898,78.233))) * 43758.5453);
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;

                float time = _Time.y;

                // Horizontal glitch offset (tearing)
                float glitchLine = step(0.95, rand(float2(0, uv.y * time)));
                uv.x += glitchLine * (rand(float2(time, uv.y)) - 0.5) * 0.2 * _Intensity;

                // Scanline jitter
                float jitter = (rand(float2(uv.y, time)) - 0.5) * 0.01 * _Intensity;
                uv.x += jitter;

                // RGB split
                float r = tex2D(_MainTex, uv + float2(_ColorOffset * _Intensity, 0)).r;
                float g = tex2D(_MainTex, uv).g;
                float b = tex2D(_MainTex, uv - float2(_ColorOffset * _Intensity, 0)).b;

                float3 col = float3(r, g, b);

                // Scanlines
                float scan = sin(uv.y * 800) * _ScanlineStrength * _Intensity;
                col -= scan;

                // Flicker
                float flicker = rand(float2(time, 0)) * 0.1 * _Intensity;
                col -= flicker;

                return float4(col, 1);
            }
            ENDCG
        }
    }
}