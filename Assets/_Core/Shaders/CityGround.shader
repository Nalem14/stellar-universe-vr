Shader "SU/CityGround"
{
    // The land around a city out to the horizon (CityExterior): plains, dunes, ice, canyons — or, on a gas
    // giant, the sea of cloud under the city's platform. Per vertex colour = terrain albedo baked by CityKit
    // (height, slope, kind); uv0 = world metres for the detail noise. At night the city lights the land
    // round it (light pollution falling off past the walls).
    Properties
    {
        _Noise ("Detail noise (tileable)", 2D) = "gray" {}
        _DetailScale ("Detail scale (m)", Float) = 38
        _Detail ("Detail strength", Float) = 0.28
        _Gas ("Cloud sea (gas giant)", Float) = 0
        _CityGlow ("City glow at night", Color) = (1, 0.62, 0.32, 1)
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
            float _DetailScale;
            float _Detail;
            float _Gas;
            float4 _CityGlow;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
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
                o.color = v.color;
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float t = _SUCityNight.z;
                float2 drift = _Gas > 0.5 ? float2(t * 0.004, t * 0.0017) : float2(0, 0);
                float a = tex2D(_Noise, i.uv / _DetailScale + drift).r;
                float b = tex2D(_Noise, i.uv / (_DetailScale * 5.3) - drift * 0.6).r;
                float detail = (a * 0.6 + b * 0.4) - 0.5;
                float3 albedo = i.color.rgb * (1.0 + detail * _Detail * 2.0);
                float3 n = normalize(i.worldNormal);
                float3 col = albedo * SU_CityLight(n);
                // Cloud sea: billows catch the sun on their crowns.
                col += _Gas * _SUCitySun.rgb * saturate(detail * 1.6) * 0.18;

                // The city lights its surroundings at night.
                float2 off = i.worldPos.xz - _SUCityCentre.xz;
                float near = saturate(1.0 - length(off) / max(_SUCityCentre.w * 1.35, 1.0));
                col += _CityGlow.rgb * near * near * _SUCityNight.x * 0.16;

                col = SU_CityShoulder(col);
                return float4(SU_CityFog(col, i.worldPos), 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
