Shader "SU/NebulaCloud"
{
    Properties
    {
        _MainTex ("Cloud", 2D) = "white" {}
        _Color ("Color", Color) = (0.35, 0.55, 1, 1)
        _EmissionMul ("Emission Mul", Float) = 0.55
        _Soft ("Soft", Float) = 1.2
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend One One
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
            float _EmissionMul;
            float _Soft;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float4 tex = tex2D(_MainTex, i.uv);
                float a = pow(saturate(tex.a), _Soft) * _EmissionMul;
                float3 col = tex.rgb * _Color.rgb * a;
                return float4(col, a);
            }
            ENDCG
        }
    }
    FallBack Off
}
