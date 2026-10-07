Shader "SU/BioLiquid"
{
    // Glowing culture liquid of the fuel refinery (algae columns, the nanobot pool, the cryo tanks, the plasma
    // core): an opaque emissive body with bubbles rising through it (hashed cells scrolling up the UV in metres),
    // two drifting caustic bands and a bright rim toward grazing angles. One texture-free pass, no lighting, no
    // transparency (the glass around it is a separate holo shell): cheap on Quest.
    Properties
    {
        _Color ("Liquid", Color) = (0.08, 0.45, 0.32, 1)
        _Emission ("Glow", Color) = (0.35, 1, 0.65, 1)
        _EmissionMul ("Glow Mul", Float) = 1.2
        _BubbleDensity ("Bubbles per metre", Float) = 9
        _BubbleSpeed ("Bubble rise (m/s)", Float) = 0.12
        _Caustic ("Caustic strength", Float) = 0.6
        _Fresnel ("Rim power", Float) = 2.2
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
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _Color;
            float4 _Emission;
            float _EmissionMul;
            float _BubbleDensity;
            float _BubbleSpeed;
            float _Caustic;
            float _Fresnel;

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

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

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

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float t = _Time.y;

                // Bubbles: one candidate per cell, a little over half the cells carry one; each rises with its cell
                // and wobbles sideways. Ring + soft core, so they read as gas in a liquid, not as dots.
                float2 g = i.uv * _BubbleDensity;
                g.y -= t * _BubbleSpeed * _BubbleDensity;
                float2 cell = floor(g);
                float2 f = frac(g) - 0.5;
                float h = Hash21(cell);
                float2 c = float2(h - 0.5, frac(h * 7.13) - 0.5) * 0.45;
                c.x += sin(t * 2.1 + h * 31.0) * 0.08;
                float r = 0.07 + 0.13 * frac(h * 13.7);
                float d = length(f - c);
                float ring = saturate(1.0 - abs(d - r) / 0.035);
                float core = saturate(1.0 - d / max(r, 1e-3)) * 0.3;
                float bubble = (ring + core) * step(h, 0.58);

                // Caustics: two crossing sine bands, sharpened.
                float cau = sin(i.uv.x * 6.0 + t * 0.9 + sin(i.uv.y * 4.0 - t * 0.6) * 1.7) *
                            sin(i.uv.y * 5.0 - t * 0.7 + sin(i.uv.x * 3.0 + t * 1.1) * 1.3);
                cau = pow(saturate(cau * 0.5 + 0.5), 4.0);

                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float fres = pow(1.0 - saturate(abs(dot(normalize(i.worldNormal), viewDir))), _Fresnel);

                float3 col = _Color.rgb * (0.6 + cau * _Caustic) +
                             _Emission.rgb * _EmissionMul * (0.45 + bubble * 0.9 + fres * 0.75 + cau * _Caustic * 0.4);
                return float4(col, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
