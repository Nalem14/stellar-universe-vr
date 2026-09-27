// Black hole accretion disk: additive, on the shared ring mesh (inner 0.62 → outer 1 in object space).
// Hot white-blue at the inner edge cooling to orange then deep red, spiral streaks sheared by orbital
// speed (faster inside), one side brighter (relativistic beaming), soft inner and outer edges.
Shader "SU/AccretionDisk"
{
    Properties
    {
        _Inner ("Inner Colour", Color) = (0.8, 0.9, 1, 1)
        _Mid ("Mid Colour", Color) = (1, 0.62, 0.25, 1)
        _Outer ("Outer Colour", Color) = (0.6, 0.12, 0.05, 1)
        _Speed ("Orbit Speed", Float) = 0.35
        _Intensity ("Intensity", Float) = 1.6
        _Beam ("Beaming", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _Inner;
            float4 _Mid;
            float4 _Outer;
            float _Speed;
            float _Intensity;
            float _Beam;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 p : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.p = v.vertex.xz;
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash(i), b = hash(i + float2(1, 0)), c = hash(i + float2(0, 1)), d = hash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float r = length(i.p);
                float t = saturate((r - 0.62) / 0.38);
                float ang = atan2(i.p.y, i.p.x);
                // Keplerian shear: the inside laps the outside.
                float swirl = ang + _Time.y * _Speed / max(0.25, r * r) + log(r) * 9.0;
                float streak = noise(float2(swirl * 3.0, t * 14.0)) * 0.6 + noise(float2(swirl * 9.0, t * 40.0)) * 0.4;
                float3 col = t < 0.35 ? lerp(_Inner.rgb, _Mid.rgb, t / 0.35) : lerp(_Mid.rgb, _Outer.rgb, (t - 0.35) / 0.65);
                float edge = smoothstep(0.0, 0.08, t) * (1.0 - smoothstep(0.55, 1.0, t));
                float heat = pow(1.0 - t, 1.6) * 1.6 + 0.25;
                float beam = 1.0 + _Beam * sin(ang);
                float k = edge * heat * beam * (0.55 + streak * 0.9) * _Intensity;
                return float4(col * k, k);
            }
            ENDCG
        }
    }
    FallBack Off
}
