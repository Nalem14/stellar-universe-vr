Shader "SU/HullMetal"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Color", Color) = (0.16, 0.2, 0.24, 1)
        _RimColor ("Rim", Color) = (0.25, 0.92, 1, 1)
        _Emission ("Emission", Color) = (0, 0, 0, 1)
        _EmissionMul ("Emission Mul", Float) = 0
        _RimPower ("Rim Power", Float) = 3.1
        _Hemi ("Hemi", Float) = 0.5
        _PanelScale ("Panel Scale", Float) = 2.4
        _Groove ("Groove", Float) = 0.55
        _Wear ("Wear", Float) = 0.22
        _Windows ("Windows", Float) = 0
        _WindowColor ("Window Color", Color) = (1, 0.78, 0.45, 1)
        _WindowScale ("Window Scale", Float) = 1
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
            float4 _MainTex_ST;
            float4 _Color;
            float4 _RimColor;
            float4 _Emission;
            float _EmissionMul;
            float _RimPower;
            float _Hemi;
            float _PanelScale;
            float _Groove;
            float _Wear;
            float _Windows;
            float4 _WindowColor;
            float _WindowScale;

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
                float3 objPos : TEXCOORD3;
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
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.objPos = v.vertex.xyz;
                return o;
            }

            float hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.worldNormal);
                float3 view = normalize(_WorldSpaceCameraPos - i.worldPos);
                float fres = pow(1.0 - saturate(dot(n, view)), _RimPower);
                float wrap = saturate(dot(n, float3(0.22, 0.82, 0.38)) * 0.55 + 0.45);
                float hemi = lerp(1.0 - _Hemi, 1.0, n.y * 0.5 + 0.5);

                float3 an = abs(n);
                float2 puv = (an.y >= an.x && an.y >= an.z) ? i.objPos.xz
                    : (an.x >= an.z) ? i.objPos.yz
                    : i.objPos.xy;
                float2 g = abs(frac(puv * _PanelScale) - 0.5);
                float panelLine = min(g.x, g.y);
                float groove = 1.0 - smoothstep(0.0, 0.05, panelLine);
                float wear = hash31(floor(i.objPos * 4.0)) * _Wear;
                float3 tex = tex2D(_MainTex, i.uv).rgb;
                float3 albedo = tex * _Color.rgb;
                albedo *= (1.0 - groove * _Groove) * (1.0 - wear);
                float spec = pow(saturate(dot(n, normalize(view + float3(0.15, 0.9, 0.25)))), 48.0) * 0.18;
                float3 col = albedo * (wrap * hemi * 0.7 + 0.42)
                    + _RimColor.rgb * fres * 0.4
                    + spec
                    + _Emission.rgb * _EmissionMul;

                // Civil hulls: rows of lit portholes on the flanks (seeded per window, some dark), off by default.
                float side = 1.0 - smoothstep(0.16, 0.34, an.y);
                float2 wuv = float2(an.x >= an.z ? i.objPos.z : i.objPos.x, i.objPos.y) * float2(1.05, 1.2) * _WindowScale;
                float2 cell = floor(wuv);
                float2 wf = abs(frac(wuv) - 0.5);
                float pane = (1.0 - smoothstep(0.18, 0.24, wf.x)) * (1.0 - smoothstep(0.12, 0.17, wf.y));
                float lit = step(0.3, hash31(float3(cell, sign(i.objPos.x) + sign(i.objPos.z) * 3.0)));
                float3 warm = lerp(_WindowColor.rgb, float3(0.7, 0.9, 1.0), step(0.86, hash31(float3(cell.yx, 5.0))));
                col += warm * pane * lit * side * _Windows * (1.0 - groove);
                return float4(col, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
