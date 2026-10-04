Shader "SU/RadarField"
{
    // Our field of view on the galaxy table (web 5021101 limited vision), one quad over the plate:
    // every vision source is a circle in table space (_Src[i] = centre x, z, radius, kind) — a small bubble on a
    // system we hold or fly in, a wide disc around a ship carrying a scanner. The circles melt into one shape
    // (smooth union), drawn as a radar screen — never a flat fill, so it cannot pass for a territory: a phosphor
    // dot matrix, dotted range rings drifting outward, a dashed rim crawling along the edge and, on scanners, a
    // turning beam whose afterglow lights the dots. Outside the shape a light veil dims the plate (what we do
    // not see). Disc-clipped by the quad's own radius. Quest: one draw, a ≤ 24-iteration loop, no texture.
    Properties
    {
        _Color ("Field colour", Color) = (0.1, 0.85, 1, 1)
        _Radius ("Plate radius (m)", Float) = 0.8
        _Fog ("Veil outside the field", Range(0, 0.6)) = 0.2
        _Fill ("Fill inside the field", Range(0, 0.4)) = 0.08
        _Blend ("Union softness (m)", Float) = 0.025
    }
    SubShader
    {
        // Over the territories and the nebula, under the stars (Transparent+8) and the tokens.
        Tags { "Queue"="Transparent+4" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
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

            #define MAX_SRC 24

            float4 _Color;
            float _Radius;
            float _Fog;
            float _Fill;
            float _Blend;
            float4 _Src[MAX_SRC];
            float _SrcCount;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 p : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                // Unit quad laid flat (x → table x, y → table z), scaled to the plate's diameter.
                o.p = v.vertex.xy * 2.0 * _Radius;
                return o;
            }

            // Polynomial smooth minimum: neighbouring circles fuse with a rounded neck.
            float smin(float a, float b, float k)
            {
                float h = saturate(0.5 + 0.5 * (b - a) / k);
                return lerp(b, a, h) - k * h * (1.0 - h);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p = i.p;
                float plate = length(p);
                clip(_Radius - plate);
                float edgeFade = saturate((_Radius - plate) / (_Radius * 0.05));

                float d = 1e4;
                float sweep = 0.0;
                float rings = 0.0;
                float nearSd = 1e4;
                float nearAngle = 0.0;
                float nearR = 1.0;
                int n = (int)_SrcCount;
                [loop]
                for (int k = 0; k < MAX_SRC; k++)
                {
                    if (k >= n)
                        break;
                    float4 s = _Src[k];
                    float2 v = p - s.xy;
                    float dc = length(v);
                    float sd = dc - s.z;
                    d = smin(d, sd, _Blend);
                    // The source whose edge is nearest gives the dashes their direction (along its circle).
                    if (abs(sd) < nearSd)
                    {
                        nearSd = abs(sd);
                        nearAngle = atan2(v.y, v.x);
                        nearR = s.z;
                    }

                    if (sd < 0.0)
                    {
                        float t = dc / max(s.z, 1e-4);
                        // Range rings: dotted circles at quarters of the reach, the outermost drifting out.
                        float ring = frac(t * 4.0 - _Time.y * 0.08);
                        float rw = fwidth(t * 4.0) * 1.2;
                        float rl = 1.0 - smoothstep(rw, rw * 2.0, min(ring, 1.0 - ring));
                        float dots = step(0.5, frac(atan2(v.y, v.x) * s.z * 160.0 / 6.2831853));
                        rings = max(rings, rl * dots * (1.0 - t * 0.5));
                        // Scanner: a beam turning once every 5 s, its afterglow trailing 70°.
                        if (s.w > 0.5)
                        {
                            float a = atan2(v.y, v.x);
                            float lag = frac((_Time.y * 0.2 + k * 0.13) - a / 6.2831853);
                            float beam = 1.0 - smoothstep(0.0, 0.012, lag);
                            float trail = pow(saturate(1.0 - lag / 0.2), 2.4);
                            sweep = max(sweep, (trail * 0.6 + beam) * smoothstep(0.0, 0.06, t));
                        }
                    }
                }

                float inside = 1.0 - smoothstep(-0.0015, 0.0015, d);

                // Phosphor dot matrix (not a flat fill: territories are fills, the radar is a screen).
                float2 g = frac(p * 70.0) - 0.5;
                float gw = fwidth(p.x * 70.0);
                float dotm = 1.0 - smoothstep(0.1, 0.1 + gw * 1.5, length(g));

                // Dashed rim crawling along the edge.
                float rimBand = 1.0 - smoothstep(0.0012, 0.0032, abs(d));
                float dash = step(0.45, frac(nearAngle * nearR * 90.0 / 6.2831853 - _Time.y * 0.6));
                float rim = rimBand * dash;

                float3 col = _Color.rgb;
                float a = inside * (dotm * (0.16 + sweep * 0.7) + rings * 0.4 + sweep * 0.22) + rim * 0.85;
                // Veil outside (dark, not coloured), so the field reads as where we see.
                float veil = _Fog * (1.0 - inside) * (1.0 - rimBand);
                float3 lit = col * (1.0 + sweep * 0.8 + rim * 0.4);
                float3 rgb = lerp(float3(0.0, 0.015, 0.03), lit, saturate(a / max(a + veil, 1e-4)));
                return float4(rgb, saturate(a + veil) * edgeFade);
            }
            ENDCG
        }
    }
    FallBack Off
}
