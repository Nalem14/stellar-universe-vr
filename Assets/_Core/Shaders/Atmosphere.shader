Shader "SU/Atmosphere"
{
    Properties
    {
        _Color ("Color", Color) = (0.35, 0.7, 1, 1)
        _RimPower ("Rim Power", Float) = 2.8
        _Intensity ("Intensity", Float) = 1.35
        _Inner ("Inner Fade", Float) = 0.15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Front
        LOD 100

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _RimPower;
            float _Intensity;
            float _Inner;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                // Flip normal because we cull front (view shell from inside)
                o.worldNormal = -UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float ndv = saturate(dot(n, v));
                float rim = pow(1.0 - ndv, _RimPower);
                rim = saturate(rim - _Inner);
                float3 col = _Color.rgb * rim * _Intensity;
                return float4(col, rim);
            }
            ENDCG
        }
    }
    FallBack Off
}
