Shader "SU/ShieldDome"
{
    // The planetary shield over a city (CityExterior): an additive hex lattice, faint where it is seen face on
    // and brighter toward its edges, a slow energy ripple climbing it, and a white-blue flare across the whole
    // dome when it takes a hit (_SUCityNight.y). Seen from inside (we stand under it): both faces drawn.
    Properties
    {
        _Color ("Colour", Color) = (0.35, 0.75, 1, 1)
        _Strength ("Strength", Range(0, 2)) = 0.6
        _HexScale ("Hex scale", Float) = 46
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
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
            #include "SUCity.cginc"

            float4 _Color;
            float _Strength;
            float _HexScale;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            // Distance to the nearest hex edge (0 on an edge, ~0.5 at a cell's heart).
            float HexEdge(float2 p)
            {
                const float2 r = float2(1.0, 1.7320508);
                float2 h = r * 0.5;
                float2 a = fmod(abs(p), r) - h;
                float2 b = fmod(abs(p - h), r) - h;
                float2 g = dot(a, a) < dot(b, b) ? a : b;
                g = abs(g);
                return 0.5 - max(dot(g, normalize(r)), g.x);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float rim = pow(1.0 - abs(dot(n, v)), 2.5);
                float edge = 1.0 - smoothstep(0.0, 0.06, HexEdge(i.uv * float2(_HexScale * 2.0, _HexScale)));
                float t = _SUCityNight.z;
                float ripple = 0.5 + 0.5 * sin(i.uv.y * 40.0 - t * 1.6);
                float hit = _SUCityNight.y;
                float k = (0.05 + rim * 0.55) * (0.35 + 0.65 * edge) * (0.75 + 0.25 * ripple);
                k = k * _Strength + hit * (0.25 + edge * 0.6);
                // Base of the dome fades into the ground haze.
                k *= saturate(i.uv.y * 8.0);
                float3 col = _Color.rgb * k + float3(0.8, 0.9, 1.0) * hit * edge * 0.3;
                return float4(col, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
