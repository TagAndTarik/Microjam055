Shader "Custom/RetroCameraLowAlias"
{
    Properties
    {
        _MainTex ("Base (Screen)", 2D) = "white" {}
        _Intensity ("Glitch Intensity", Range(0,1)) = 0.5
        _ScanlineStrength ("Scanline Strength", Range(0,1)) = 0.3
        _ColorOffset ("Color Offset", Float) = 0.002
        _RetroPixelSize ("Retro Pixel Size", Range(1,16)) = 16
        _BrightnessGain ("Brightness Gain", Range(0.8,1.3)) = 1.16
        [HideInInspector] _OverlayTex ("Overlay Texture", 2D) = "black" {}
        [HideInInspector] _OverlayEnabled ("Overlay Enabled", Float) = 0
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
            sampler2D _OverlayTex;
            float4 _MainTex_TexelSize;

            float _Intensity;
            float _ScanlineStrength;
            float _ColorOffset;
            float _RetroPixelSize;
            float _BrightnessGain;
            float _OverlayEnabled;

            float rand(float2 co)
            {
                return frac(sin(dot(co.xy, float2(12.9898, 78.233))) * 43758.5453);
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float time = _Time.y;

                float glitchLine = step(0.95, rand(float2(0, uv.y * time)));
                uv.x += glitchLine * (rand(float2(time, uv.y)) - 0.5) * 0.2 * _Intensity;

                float jitter = (rand(float2(uv.y, time)) - 0.5) * 0.01 * _Intensity;
                uv.x += jitter;

                float2 retroPixel = max(_MainTex_TexelSize.xy * _RetroPixelSize, _MainTex_TexelSize.xy);
                uv = (floor(uv / retroPixel) + 0.5) * retroPixel;

                float r = tex2D(_MainTex, uv + float2(_ColorOffset * _Intensity, 0)).r;
                float g = tex2D(_MainTex, uv).g;
                float b = tex2D(_MainTex, uv - float2(_ColorOffset * _Intensity, 0)).b;

                float3 col = float3(r, g, b);

                if (_OverlayEnabled > 0.5)
                {
                    float4 overlayCenter = tex2D(_OverlayTex, uv);
                    float overlayR = tex2D(_OverlayTex, uv + float2(_ColorOffset * _Intensity, 0)).r;
                    float overlayG = overlayCenter.g;
                    float overlayB = tex2D(_OverlayTex, uv - float2(_ColorOffset * _Intensity, 0)).b;
                    col = lerp(col, float3(overlayR, overlayG, overlayB), saturate(overlayCenter.a));
                }

                float scan = sin(uv.y * 800) * _ScanlineStrength * _Intensity;
                col -= scan;

                float flicker = rand(float2(time, 0)) * 0.1 * _Intensity;
                col -= flicker;

                col = saturate(col * _BrightnessGain);

                return float4(col, 1);
            }
            ENDCG
        }
    }
}
