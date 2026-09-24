Shader "SU/ConsoleMetal"
{
    // Bridge furniture / console hardware without realtime lights (Quest budget):
    // brushed metal read through a fixed key + hemisphere + view spec, and an emissive
    // accent that lights the bevels of rounded meshes (object-space normals off the face axis).
    // _AccentMul is driven per renderer through a MaterialPropertyBlock (hover / press / alert).
    Properties
    {
        _Color ("Base", Color) = (0.2, 0.23, 0.27, 1)
        _Accent ("Accent", Color) = (0.25, 0.92, 1, 1)
        _AccentMul ("Accent Mul", Float) = 0.6
        _BevelWidth ("Bevel Width", Range(0.05, 1)) = 0.35
        _FaceGlow ("Face Glow", Range(0, 1)) = 0.08
        _Brush ("Brush", Range(0, 1)) = 0.35
        _Gloss ("Gloss", Range(4, 128)) = 42
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
            float4 _Accent;
            float _AccentMul;
            float _BevelWidth;
            float _FaceGlow;
            float _Brush;
            float _Gloss;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 objPos : TEXCOORD2;
                float3 objNormal : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.objPos = v.vertex.xyz;
                o.objNormal = v.normal;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.worldNormal);
                float3 view = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 key = normalize(float3(0.25, 0.85, -0.45));

                // Brushed streaks along the object X axis (fine, stable per object).
                float streak = hash21(float2(floor(i.objPos.y * 900.0), floor(i.objPos.z * 900.0)));
                float brush = lerp(1.0, 0.86 + streak * 0.22, _Brush);

                float wrap = saturate(dot(n, key) * 0.6 + 0.4);
                float hemi = lerp(0.55, 1.0, n.y * 0.5 + 0.5);
                float3 h = normalize(view + key);
                float spec = pow(saturate(dot(n, h)), _Gloss) * 0.35;
                float fres = pow(1.0 - saturate(dot(n, view)), 4.0);

                // Bevel mask: normal leaves the dominant face axis on rounded edges.
                float3 an = abs(normalize(i.objNormal));
                float face = max(an.x, max(an.y, an.z));
                float bevel = smoothstep(1.0, 1.0 - _BevelWidth, face);

                float3 albedo = _Color.rgb * brush;
                float3 col = albedo * (wrap * hemi * 0.85 + 0.2) + spec + fres * 0.08;
                float glow = saturate(bevel + _FaceGlow) * _AccentMul;
                col += _Accent.rgb * glow;
                return float4(col, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
