Shader "SU/HoloHexGrid"
{
    // Battle board on the holo table: every hex of the grid is a 7-vertex fan of ONE mesh (one draw call).
    // uv.x = hex-norm distance from the cell centre (0 centre, 1 rim), so the rim line and the inner glow
    // are exact hexagons; vertex colour = cell state (base / team zone / reach / target / hover), uv.y > 0.5
    // marks cells that breathe (valid choices). _Reveal (0..1) grows the board out from its centre when the
    // table switches to combat, and folds it back when the fight ends.
    Properties
    {
        _Reveal ("Reveal (0..1)", Float) = 1
        _RevealRadius ("Board Radius (m)", Float) = 0.8
        _Intensity ("Intensity", Float) = 1.4
    }
    SubShader
    {
        Tags { "Queue"="Transparent+2" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha One
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

            float _Reveal;
            float _RevealRadius;
            float _Intensity;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float2 local : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.local = v.vertex.xz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float e = i.uv.x;
                float rim = smoothstep(0.84, 0.95, e) * (1.0 - smoothstep(0.97, 1.0, e));
                float inner = e * e * 0.35 + 0.12;
                float breathe = i.uv.y > 0.5 ? 0.75 + 0.25 * sin(_Time.y * 4.0) : 1.0;
                // Fine scanlines drifting across the board: hologram, not paint.
                float scan = 0.88 + 0.12 * sin(i.local.y * 900.0 - _Time.y * 6.0);
                float r = length(i.local);
                float front = _Reveal * (_RevealRadius + 0.12);
                float reveal = saturate((front - r) / 0.08);
                // The unfolding front itself glows brighter as it sweeps outwards.
                float wave = (1.0 - saturate(abs(front - r) / 0.06)) * step(_Reveal, 0.999);
                float a = (rim * 0.9 + inner) * i.color.a * breathe * scan * reveal;
                float3 col = i.color.rgb * _Intensity * (1.0 + rim * 0.8) + wave * float3(0.4, 0.9, 1.0);
                return float4(col, saturate(a + wave * 0.5 * reveal));
            }
            ENDCG
        }
    }
    FallBack Off
}
