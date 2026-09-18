Shader "SU/StarSurface"
{
    Properties
    {
        _MainTex ("Plasma", 2D) = "white" {}
        _Color ("Color", Color) = (1, 0.85, 0.45, 1)
        _Emission ("Emission", Color) = (1, 0.8, 0.35, 1)
        _EmissionMul ("Emission Mul", Float) = 2.4
        _Flow ("Flow", Float) = 0.08
        _Contrast ("Contrast", Float) = 1.15
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
            float4 _Emission;
            float _EmissionMul;
            float _Flow;
            float _Contrast;

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
                float2 uv = i.uv + float2(_Time.y * _Flow * 0.15, _Time.y * _Flow * 0.07);
                float3 tex = tex2D(_MainTex, uv).rgb;
                float3 tex2 = tex2D(_MainTex, uv * 1.7 + 0.13).rgb;
                float3 plasma = lerp(tex, tex2, 0.35);
                plasma = pow(saturate(plasma), _Contrast);
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float rim = pow(1.0 - saturate(dot(n, v)), 2.4);
                float3 col = plasma * _Color.rgb;
                col += _Emission.rgb * _EmissionMul * (0.55 + plasma.r * 0.55);
                col += _Emission.rgb * rim * 0.65;
                // Keep Quest bloom sane — soft clamp
                col = col / (1.0 + col * 0.35);
                return float4(col, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
