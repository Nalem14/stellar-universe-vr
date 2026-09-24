Shader "SU/RadarIcon"
{
    Properties
    {
        _MainTex ("Icon", 2D) = "white" {}
        _Color ("Tint", Color) = (0.3, 0.95, 1, 1)
        _Emission ("Emission", Color) = (0.2, 0.8, 1, 1)
        _EmissionMul ("Emission Mul", Float) = 2.2
        _Cutoff ("Alpha Cut", Float) = 0.06
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
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
            float _EmissionMul;
            float _Cutoff;

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
                // PNGs with black matte (no alpha): treat luminance as coverage.
                float lum = max(tex.r, max(tex.g, tex.b));
                float cover = max(tex.a, lum);
                clip(cover - _Cutoff);
                float3 col = tex.rgb * _Color.rgb + _Emission.rgb * _EmissionMul * cover;
                float alpha = saturate(cover * _Color.a);
                return float4(col, alpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
