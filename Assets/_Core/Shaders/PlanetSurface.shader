Shader "SU/PlanetSurface"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _RimColor ("Rim", Color) = (0.25, 0.92, 1, 1)
        _RimPower ("Rim Power", Float) = 3.2
        _RimMul ("Rim Mul", Float) = 0.35
        _Hemi ("Hemi", Float) = 0.55
        _LightDir ("Light Dir", Vector) = (1, 0.35, 0.2, 0)
        _LightColor ("Light Color", Color) = (1, 0.92, 0.75, 1)
        _Ambient ("Ambient", Color) = (0.04, 0.06, 0.1, 1)
        _NightSide ("Night Side", Float) = 0.12
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
            float _RimMul;
            float _Hemi;
            float4 _LightDir;
            float4 _LightColor;
            float4 _Ambient;
            float _NightSide;

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
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 albedo = tex2D(_MainTex, i.uv).rgb * _Color.rgb;
                float3 n = normalize(i.worldNormal);
                float3 l = normalize(_LightDir.xyz);
                float ndl = saturate(dot(n, l));
                float hemi = saturate(n.y * 0.5 + 0.5);
                float lit = lerp(_NightSide, 1.0, ndl);
                lit = lerp(lit, lit * (0.65 + 0.45 * hemi), _Hemi);
                float3 col = albedo * (_Ambient.rgb + _LightColor.rgb * lit);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
                col += _RimColor.rgb * rim * _RimMul;
                return float4(col, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
