Shader "SU/Viewscreen"
{
    // The bridge's curved main viewscreen: shows the hull camera feed (HUD included) with the read of a real
    // display — fine scanlines, a soft edge falloff, a tint for the alert state, and a static-noise flicker
    // while the feed is switching (_Switch 0..1). One texture sample, a few ALU: cheap on Quest.
    Properties
    {
        _MainTex ("Feed", 2D) = "black" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Gain ("Gain", Float) = 1.15
        _Scan ("Scanlines", Range(0, 1)) = 0.12
        _Lines ("Line count", Float) = 400
        _Switch ("Switching", Range(0, 1)) = 0
        _Edge ("Edge glow", Color) = (0.25, 0.92, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Tint;
            float _Gain;
            float _Scan;
            float _Lines;
            float _Switch;
            float4 _Edge;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float hash(float2 p)
            {
                p = frac(p * float2(443.9, 397.3));
                p += dot(p, p + 19.19);
                return frac(p.x * p.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.uv;
                // While switching, rows tear sideways a little.
                float row = floor(uv.y * 60.0);
                uv.x += (hash(float2(row, floor(_Time.y * 24.0))) - 0.5) * 0.03 * _Switch;
                float3 col = tex2D(_MainTex, uv).rgb * _Tint.rgb * _Gain;

                float scan = 0.5 + 0.5 * sin(i.uv.y * _Lines * 6.2832);
                col *= 1.0 - _Scan * scan;
                float noise = hash(i.uv * 512.0 + _Time.y);
                col = lerp(col, _Edge.rgb * noise * 0.6, _Switch * 0.55);

                // Edge falloff and a thin lit border (the panel's light guide).
                float2 d = min(i.uv, 1.0 - i.uv);
                float edge = saturate(min(d.x * 2.875, d.y) * 28.0);
                col *= lerp(0.55, 1.0, edge);
                float rim = 1.0 - saturate(min(d.x * 2.875, d.y) * 260.0);
                col += _Edge.rgb * rim * 0.8;
                return float4(col, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
