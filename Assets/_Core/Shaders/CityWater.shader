Shader "SU/CityWater"
{
    // Seas and lakes round a city (CityExterior): a flat sheet that mirrors the sky toward the horizon, with
    // the sun's path glittering on two drifting ripple layers sampled from the shared tileable noise.
    Properties
    {
        _Noise ("Ripple noise (tileable)", 2D) = "gray" {}
        _Deep ("Deep colour", Color) = (0.03, 0.12, 0.18, 1)
        _Shallow ("Shallow colour", Color) = (0.08, 0.28, 0.33, 1)
        _RippleScale ("Ripple scale (m)", Float) = 26
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
            #include "SUCity.cginc"

            sampler2D _Noise;
            float4 _Deep;
            float4 _Shallow;
            float _RippleScale;

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
                float3 worldPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float t = _SUCityNight.z;
                float2 uv = i.uv / _RippleScale;
                float r1 = tex2D(_Noise, uv + float2(t * 0.011, t * 0.006)).r;
                float r2 = tex2D(_Noise, uv * 1.9 - float2(t * 0.008, -t * 0.013)).r;
                float3 n = normalize(float3((r1 - 0.5) * 0.18, 1.0, (r2 - 0.5) * 0.18));
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float fres = pow(1.0 - saturate(dot(n, v)), 4.0);
                float3 body = lerp(_Deep.rgb, _Shallow.rgb, r1 * 0.5) * SU_CityLight(float3(0, 1, 0)) * 0.8;
                float3 sky = _SUCityFog.rgb * 1.05;
                float3 col = lerp(body, sky, 0.25 + 0.65 * fres);
                float3 r = reflect(-v, n);
                float spec = pow(saturate(dot(r, normalize(_SUCitySunDir.xyz))), 160.0) * _SUCitySunDir.w;
                col += _SUCitySun.rgb * spec * 1.4;
                col = SU_CityShoulder(col);
                return float4(SU_CityFog(col, i.worldPos), 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
