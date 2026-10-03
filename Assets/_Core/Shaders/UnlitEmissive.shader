Shader "SU/UnlitEmissive"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Emission ("Emission", Color) = (0, 0, 0, 1)
        _EmissionMul ("Emission Mul", Float) = 1
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
            #include "SUAlert.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float4 _Emission;
            float _EmissionMul;

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
                float4 tex = tex2D(_MainTex, i.uv) * _Color;
                // Soft ambient lift so CIC interiors aren't pitch-black on unlit mats.
                float3 amb = UNITY_LIGHTMODEL_AMBIENT.rgb;
                float3 lit = tex.rgb * (1.0 + amb * 3.5) + _Emission.rgb * _EmissionMul;
                // The ship's alert reaches every room surface: albedo catches the wash and the beacon sweep.
                float3 n = normalize(i.worldNormal);
                float3 alert = SU_AlertLight(i.worldPos, n);
                float3 flash = SU_FlashLight(i.worldPos, n);
                lit += tex.rgb * (alert * 2.2 + flash * 1.9) + alert * 0.05 + flash * 0.05;
                return float4(lit, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
