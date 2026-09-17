Shader "SU/HoloSurface"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (0.25, 0.95, 1, 0.65)
        _Emission ("Emission", Color) = (0.15, 0.85, 1, 1)
        _ScanSpeed ("Scan Speed", Float) = 0.35
        _ScanDensity ("Scan Density", Float) = 42
        _Fresnel ("Fresnel", Float) = 2.4
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float4 _Emission;
            float _ScanSpeed;
            float _ScanDensity;
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

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float4 tex = tex2D(_MainTex, i.uv);
                float scan = sin((i.worldPos.y + i.uv.y) * _ScanDensity + _Time.y * _ScanSpeed * 12) * 0.5 + 0.5;
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float fres = pow(1.0 - saturate(dot(normalize(i.worldNormal), viewDir)), _Fresnel);
                float3 col = tex.rgb * _Color.rgb + _Emission.rgb * (0.35 + scan * 0.65 + fres);
                float alpha = saturate(_Color.a * (0.45 + tex.r * 0.4 + fres * 0.5));
                return float4(col, alpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
