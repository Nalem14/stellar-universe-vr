Shader "SU/HoloCrystal"
{
    // Faceted tech crystal of the research lab: flat-shaded facets under a fixed key, fresnel rim in the
    // tint, and a slow emissive pulse. Opaque, one pass, per-renderer tint through MaterialPropertyBlock.
    Properties
    {
        _Color ("Tint", Color) = (0.7, 0.5, 1, 1)
        _Emission ("Emission", Color) = (0.7, 0.5, 1, 1)
        _EmissionMul ("Emission Mul", Float) = 1
        _Rim ("Rim", Float) = 1.6
        _Pulse ("Pulse Speed", Float) = 0.3
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

            float4 _Color;
            float4 _Emission;
            float _EmissionMul;
            float _Rim;
            float _Pulse;

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
                float3 objPos : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.objPos = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 key = normalize(float3(0.35, 0.85, -0.4));
                float facet = saturate(dot(n, key)) * 0.55 + 0.22;
                float fres = pow(1.0 - saturate(dot(n, v)), 2.2);
                // Inner light: brighter toward the core, breathing slowly.
                float core = saturate(1.0 - length(i.objPos) * 1.6);
                float pulse = 0.78 + 0.22 * sin(_Time.y * _Pulse * 6.2832);
                float3 col = _Color.rgb * facet
                           + _Emission.rgb * _EmissionMul * pulse * (0.45 + core)
                           + _Color.rgb * fres * _Rim;
                return float4(col, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
