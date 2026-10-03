Shader "SU/CityBlock"
{
    // The city's buildings, one mesh for the whole city (CityExterior). Per vertex:
    //   colour   = facade albedo (district / planet palette baked by CityKit)
    //   uv0      = facade coordinates in window bays (1 unit = one bay wide, one floor high)
    //   uv1      = x seed, y share of windows lit at night, z roof (1) / facade (0), w light band (1)
    // Windows are drawn here, procedurally: glass that mirrors the sky by day with a glint of sun, lit
    // offices and homes by night (warm or cool per window, more of them as the night deepens). Light bands
    // (boulevard strips, crown trims, beacons, landing-pad rings) share the mesh and glow by themselves.
    Properties
    {
        _WindowWarm ("Window warm", Color) = (1, 0.76, 0.46, 1)
        _WindowCool ("Window cool", Color) = (0.55, 0.82, 1, 1)
        _WindowGlow ("Window glow", Float) = 1.35
        _BandGlow ("Light band glow", Float) = 1.6
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

            float4 _WindowWarm;
            float4 _WindowCool;
            float _WindowGlow;
            float _BandGlow;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 data : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 data : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                float3 worldNormal : TEXCOORD3;
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
                o.data = v.data;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float night = _SUCityNight.x;
                float3 albedo = i.color.rgb;

                // Light bands: self-lit, brighter at night, never dark.
                if (i.data.w > 0.5)
                {
                    // Keep the hue at night (crystal violet, beacon red): a shoulder instead of a hard clip to white.
                    float3 band = albedo * _BandGlow * (0.55 + 1.15 * night);
                    band = band * 1.6 / (1.0 + max(max(band.r, band.g), band.b) * 0.55);
                    return float4(SU_CityFog(band, i.worldPos), 1);
                }

                float3 n = normalize(i.worldNormal);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float roof = i.data.z;

                float2 f = frac(i.uv);
                float2 cell = floor(i.uv);
                // Floor slabs: a darker line under every storey.
                float slab = (1.0 - roof) * (1.0 - smoothstep(0.0, 0.07, f.y)) * 0.35;
                float win = (1.0 - roof) * step(0.15, f.x) * step(f.x, 0.85) * step(0.24, f.y) * step(f.y, 0.84);

                float3 col = albedo * SU_CityLight(n) * (1.0 - slab);
                // Roofs: a touch darker, with the odd plant room picked out by the hash.
                col *= lerp(1.0, 0.78 + 0.18 * SU_Hash21(cell + i.data.x * 31.0), roof);

                if (win > 0.5)
                {
                    float h = SU_Hash21(cell + i.data.x * 97.13);
                    // Day: the glass mirrors the sky, with a glint of the sun.
                    float3 r = reflect(-v, n);
                    float glint = pow(saturate(dot(r, normalize(_SUCitySunDir.xyz))), 48.0) * _SUCitySunDir.w;
                    float fres = pow(1.0 - saturate(dot(n, v)), 3.0);
                    // Dark glass that only mirrors the sky at a glancing angle: the stone between the windows reads.
                    float3 glass = lerp(float3(0.035, 0.05, 0.07), _SUCityAmbient.rgb * 0.95 + _SUCityFog.rgb * 0.2, 0.12 + 0.6 * fres);
                    glass += _SUCitySun.rgb * glint * 0.9;
                    // Night (and a few by day): lit windows, warm homes or cool offices.
                    float litShare = i.data.y * (0.18 + 0.82 * night);
                    float lit = step(h, litShare);
                    float3 lamp = lerp(_WindowWarm.rgb, _WindowCool.rgb, step(0.62, frac(h * 7.31)));
                    float flicker = 0.85 + 0.15 * frac(h * 13.7);
                    col = glass * (1.0 - lit * 0.6) + lamp * lit * flicker * _WindowGlow * (0.22 + 0.9 * night);
                }

                col = SU_CityShoulder(col);
                return float4(SU_CityFog(col, i.worldPos), 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
