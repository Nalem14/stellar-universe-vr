Shader "SU/HullInterior"
{
    // Bridge shell (walls, soffits, ceiling, deck) without URP realtime lights (Quest budget):
    // panel texture tiled in metres, hemisphere ambient from the world normal, up to four room lights
    // fed as globals by RoomLightRig (the room's own Light components, so red alert and hits still
    // drive them), panel seams cut from the UV grid, and baked cove / corner occlusion in vertex colour.
    Properties
    {
        _MainTex ("Panel", 2D) = "white" {}
        _Color ("Tint", Color) = (0.3, 0.36, 0.42, 1)
        _Tiling ("Tiling (per metre)", Float) = 0.5
        _Sky ("Ambient from above", Color) = (0.78, 0.85, 0.95, 1)
        _Ground ("Ambient from below", Color) = (0.3, 0.33, 0.38, 1)
        _Seam ("Seam darkening", Range(0, 1)) = 0.45
        _PanelSize ("Panel size (m) u,v", Vector) = (1.2, 0.9, 0, 0)
        _LightGain ("Room light gain", Float) = 2.2
        _Lift ("Emissive lift", Float) = 0.05
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

            sampler2D _MainTex;
            float4 _Color;
            float _Tiling;
            float4 _Sky;
            float4 _Ground;
            float _Seam;
            float4 _PanelSize;
            float _LightGain;
            float _Lift;

            // xyz = world position, w = 1 / range²; rgb = colour × intensity.
            float4 _SU_RoomLightPos[4];
            float4 _SU_RoomLightCol[4];

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float ao : TEXCOORD3;
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
                o.uv = v.uv;
                o.ao = v.color.r;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.worldNormal);
                float3 albedo = tex2D(_MainTex, i.uv * _Tiling).rgb * _Color.rgb;

                // Panel seams: thin dark lines on the metre grid (anti-aliased with fwidth).
                float2 g = i.uv / _PanelSize.xy;
                float2 f = abs(frac(g + 0.5) - 0.5) / max(fwidth(g), 1e-4);
                float seam = 1.0 - saturate(1.5 - min(f.x, f.y));
                albedo *= lerp(1.0 - _Seam, 1.0, seam);

                float3 light = lerp(_Ground.rgb, _Sky.rgb, n.y * 0.5 + 0.5);
                [unroll]
                for (int k = 0; k < 4; k++)
                {
                    float3 d = _SU_RoomLightPos[k].xyz - i.worldPos;
                    float dd = dot(d, d);
                    float att = saturate(1.0 - dd * _SU_RoomLightPos[k].w);
                    att *= att;
                    float ndl = saturate(dot(n, d * rsqrt(max(dd, 1e-4))) * 0.75 + 0.25);
                    light += _SU_RoomLightCol[k].rgb * (att * ndl * _LightGain);
                }

                float3 col = albedo * light * i.ao + albedo * _Lift;
                return float4(col, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
