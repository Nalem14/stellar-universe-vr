Shader "SU/WarpTunnel"
{
    // Interstellar transit seen from inside: an open cylinder (uv.x around, uv.y along, 0 = aft, 1 = bow)
    // or a sphere cage around the ship. Streaks of a tileable noise texture race aft (_Scroll, driven from
    // C# so the speed can ramp without jumps), twisted by _Twist. _Ribs adds rings flying past (jumpgate
    // fold), _Hex a triangular lattice (Bond PRL cage). Additive, no depth write, no fog: it is light.
    // _Open fades the whole thing, _Flash whitens it at the jump / drop-out; both mesh ends fade out so the
    // tube never shows a cut edge.
    Properties
    {
        _MainTex ("Streak noise", 2D) = "black" {}
        _ColA ("Deep", Color) = (0.05, 0.12, 0.45, 1)
        _ColB ("Bright", Color) = (0.45, 0.8, 1.0, 1)
        _Tile ("Tile (around, along)", Vector) = (3, 6, 0, 0)
        _Scroll ("Scroll", Float) = 0
        _Twist ("Twist", Float) = 0.35
        _Density ("Streak threshold", Range(0, 0.95)) = 0.55
        _Ribs ("Ribs", Range(0, 1)) = 0
        _RibCount ("Rib count", Float) = 24
        _Hex ("Lattice", Range(0, 1)) = 0
        _HexCount ("Lattice cells", Vector) = (18, 9, 0, 0)
        _HexWidth ("Lattice line width", Range(0.01, 0.2)) = 0.08
        _Open ("Open", Range(0, 1)) = 1
        _Flash ("Flash", Range(0, 1)) = 0
        _Intensity ("Intensity", Float) = 1.6
        _EndFade ("End fade (aft, bow)", Vector) = (0.18, 0.22, 0, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        LOD 100

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _ColA;
            float4 _ColB;
            float4 _Tile;
            float _Scroll;
            float _Twist;
            float _Density;
            float _Ribs;
            float _RibCount;
            float _Hex;
            float4 _HexCount;
            float _HexWidth;
            float _Open;
            float _Flash;
            float _Intensity;
            float4 _EndFade;

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

            float Line(float x, float w)
            {
                float f = abs(frac(x) - 0.5) * 2.0; // 1 on the line, 0 mid-cell
                return smoothstep(1.0 - w, 1.0, f);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.uv;
                float2 p = float2(uv.x * _Tile.x + uv.y * _Twist, uv.y * _Tile.y + _Scroll);
                float4 n1 = tex2D(_MainTex, p);
                float4 n2 = tex2D(_MainTex, p * float2(2.0, 0.55) + float2(0.37, _Scroll * 0.45));
                // R = long streaks, G = soft clouds. Lines from the first octave, broken up by the second so
                // they race past in dashes of every length; clouds stay dim so the lines carry the motion.
                float streak = saturate((n1.r - _Density) / max(1.0 - _Density, 0.05));
                streak *= saturate(n2.r * 1.8 + 0.15);
                streak = streak * streak * (3.0 - 2.0 * streak);
                float cloud = n1.g * 0.55 + n2.g * 0.45;
                cloud *= cloud;

                float3 col = _ColA.rgb * (0.12 + cloud * 1.1) + _ColB.rgb * streak * 2.4;

                // Jumpgate fold: rings racing aft.
                float rib = pow(1.0 - abs(frac(uv.y * _RibCount + _Scroll * 0.35) - 0.5) * 2.0, 18.0);
                col += _ColB.rgb * rib * _Ribs * 1.4;

                // Bond PRL: triangular lattice (three line families), brighter at the crossings.
                float2 h = float2(uv.x * _HexCount.x, uv.y * _HexCount.y + _Scroll * 0.2);
                float l1 = Line(h.x, _HexWidth);
                float l2 = Line(h.x * 0.5 + h.y * 0.866, _HexWidth);
                float l3 = Line(-h.x * 0.5 + h.y * 0.866, _HexWidth);
                float lattice = saturate(l1 + l2 + l3) + l1 * l2 * l3 * 2.0;
                col += _ColB.rgb * lattice * _Hex * (0.6 + cloud);

                col = lerp(col, float3(1.0, 0.97, 0.92) * 2.2, _Flash * 0.85);

                float ends = smoothstep(0.0, _EndFade.x, uv.y) * smoothstep(1.0, 1.0 - _EndFade.y, uv.y);
                return float4(col * (_Open * ends * _Intensity), 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
