Shader "SU/HoloTerritory"
{
    // Galaxy territories on the holo table (Stellaris-style): RGBA ownership texture (fill in the empire's
    // colour, bright border where owners meet in the alpha > 0.6 band), clipped to the table's disc in world
    // space with a soft edge. Borders breathe gently; fills stay still (readability first).
    Properties
    {
        _MainTex ("Territory", 2D) = "black" {}
        _Opacity ("Opacity", Float) = 1
        _ClipCenter ("Clip Centre (world)", Vector) = (0, 0, 0, 0)
        _ClipRadius ("Clip Radius (m)", Float) = 1
        _EmissionMul ("Border Glow", Float) = 1.8
        _BorderOnly ("Border Only (curtain layers)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
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
            float4 _MainTex_ST;
            float _Opacity;
            float4 _ClipCenter;
            float _ClipRadius;
            float _EmissionMul;
            float _BorderOnly;

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
                float3 worldPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float4 t = tex2D(_MainTex, i.uv);
                float2 d = i.worldPos.xz - _ClipCenter.xz;
                float r = length(d);
                float edge = saturate((_ClipRadius - r) / max(0.001, _ClipRadius * 0.06));
                float border = step(0.6, t.a);
                float pulse = 0.85 + 0.15 * sin(_Time.y * 1.6);
                float3 col = t.rgb * lerp(1.0, _EmissionMul * pulse, border);
                // Curtain layers stacked above the plate keep only the borders: glowing walls, not a thicker fill.
                float a = lerp(t.a, t.a * border, step(0.5, _BorderOnly));
                return float4(col, a * edge * _Opacity);
            }
            ENDCG
        }
    }
    FallBack Off
}
