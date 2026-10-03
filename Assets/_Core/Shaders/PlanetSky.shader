Shader "SU/PlanetSky"
{
    // A planet's sky over its city (CityExterior): an inside-out sphere that follows the eye. Zenith to
    // horizon gradient by planet kind and hour, the sun's disc and halo, two layers of drifting cloud from
    // the shared tileable noise projected on a high cloud deck. At night it thins out (alpha) so the star sky
    // drawn just before it (SpaceBackdrop, Background queue) shows through — clouds stay, lit by the city.
    Properties
    {
        _Noise ("Cloud noise (tileable)", 2D) = "gray" {}
        _Zenith ("Zenith", Color) = (0.22, 0.42, 0.78, 1)
        _Horizon ("Horizon", Color) = (0.72, 0.82, 0.92, 1)
        _NightZenith ("Night zenith", Color) = (0.015, 0.03, 0.08, 1)
        _NightHorizon ("Night horizon", Color) = (0.06, 0.09, 0.16, 1)
        _CloudColor ("Cloud colour", Color) = (1, 1, 1, 1)
        _CloudCover ("Cloud cover", Range(0, 1)) = 0.45
        _CloudScale ("Cloud scale", Float) = 0.32
        _SunSize ("Sun size", Float) = 0.9985
    }
    SubShader
    {
        Tags { "Queue"="Background+1" "RenderType"="Background" "RenderPipeline"="UniversalPipeline" "PreviewType"="Skybox" }
        Blend SrcAlpha OneMinusSrcAlpha
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
            #include "SUCity.cginc"

            sampler2D _Noise;
            float4 _Zenith;
            float4 _Horizon;
            float4 _NightZenith;
            float4 _NightHorizon;
            float4 _CloudColor;
            float _CloudCover;
            float _CloudScale;
            float _SunSize;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.dir = mul(unity_ObjectToWorld, v.vertex).xyz - _WorldSpaceCameraPos;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 d = normalize(i.dir);
                float night = _SUCityNight.x;
                float up = saturate(d.y);
                float3 zen = lerp(_Zenith.rgb, _NightZenith.rgb, night);
                float3 hor = lerp(_Horizon.rgb, _NightHorizon.rgb, night);
                float3 col = lerp(hor, zen, pow(up, 0.55));

                // Sun: halo round it, a hot disc, the horizon warmed when it stands low.
                float3 s = normalize(_SUCitySunDir.xyz);
                float cs = dot(d, s);
                float day = _SUCitySunDir.w;
                col += _SUCitySun.rgb * pow(saturate(cs), 24.0) * 0.35 * day;
                col += _SUCitySun.rgb * pow(saturate(cs), 4.0) * 0.08 * day * (1.0 - up);
                float disc = smoothstep(_SunSize, _SunSize + 0.0006, cs);
                col = lerp(col, _SUCitySun.rgb * 1.6 + 0.3, disc * saturate(s.y * 8.0 + 0.4));

                // Clouds on a high deck, two layers drifting at different speeds.
                float cover = 0.0;
                if (d.y > 0.015)
                {
                    float2 p = d.xz / (d.y + 0.08) * _CloudScale;
                    float t = _SUCityNight.z;
                    float n1 = tex2D(_Noise, p + float2(t * 0.0021, t * 0.0009)).r;
                    float n2 = tex2D(_Noise, p * 2.3 - float2(t * 0.0013, -t * 0.0017)).r;
                    float n = n1 * 0.65 + n2 * 0.35;
                    cover = smoothstep(1.0 - _CloudCover, 1.0 - _CloudCover + 0.22, n) * smoothstep(0.015, 0.12, d.y);
                    float3 litCloud = _CloudColor.rgb * (_SUCityAmbient.rgb * 1.4 + _SUCitySun.rgb * (0.35 + 0.65 * saturate(cs * 0.5 + 0.5)));
                    // At night the city's glow lights the cloud bellies from below.
                    litCloud += float3(0.9, 0.55, 0.3) * night * 0.12 * (1.0 - up);
                    col = lerp(col, litCloud, cover * 0.92);
                }

                // Below the horizon: the haze the land fades into.
                col = lerp(col, _SUCityFog.rgb, saturate(-d.y * 12.0));
                col = SU_CityShoulder(col);
                // Night: let the stars through the clear sky, not through the clouds.
                float alpha = lerp(1.0, 0.42, night * (1.0 - cover)) ;
                alpha = d.y < 0.0 ? 1.0 : alpha;
                return float4(col, alpha);
            }
            ENDCG
        }
    }
    FallBack Off
}
