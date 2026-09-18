Shader "SU/AsteroidRock"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Color", Color) = (0.42, 0.36, 0.3, 1)
        _RimColor ("Rim", Color) = (0.55, 0.48, 0.4, 1)
        _RimPower ("Rim Power", Float) = 2.6
        _Hemi ("Hemi", Float) = 0.45
        _LightDir ("Light Dir", Vector) = (1, 0.35, 0.2, 0)
        _LightColor ("Light Color", Color) = (1, 0.9, 0.7, 1)
        _Craters ("Craters", Float) = 0.35
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
            float _RimPower;
            float _Hemi;
            float4 _LightDir;
            float4 _LightColor;
            float _Craters;

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
                float3 worldNormal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
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
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
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
                float3 albedo = tex2D(_MainTex, i.uv).rgb * _Color.rgb;
                float grit = hash31(floor(i.objPos * 18.0));
                albedo *= 0.82 + grit * 0.35 * _Craters;
                float3 n = normalize(i.worldNormal);
                float3 l = normalize(_LightDir.xyz);
                float ndl = saturate(dot(n, l));
                float hemi = saturate(n.y * 0.5 + 0.5);
                float lit = lerp(0.18, 1.0, ndl);
                lit = lerp(lit, lit * (0.7 + 0.4 * hemi), _Hemi);
                float3 col = albedo * (_LightColor.rgb * lit + float3(0.03, 0.035, 0.045));
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
                col += _RimColor.rgb * rim * 0.22;
                return float4(col, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
