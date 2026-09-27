Shader "SU/FakeScreen"
{
    // Bridge dressing screens and indicator panels, drawn entirely in the shader (no texture, no CPU per frame):
    // every screen of the dressing lives in ONE combined mesh. Per vertex: colour = the screen's tint, uv2.x = page
    // (0 waveform, 1 bar graph, 2 scrolling log, 3 radar sweep, 4 LED grid), uv2.y = seed (phase / pattern).
    Properties
    {
        _Base ("Base", Color) = (0.015, 0.035, 0.055, 1)
        _Intensity ("Intensity", Float) = 1.35
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

            float4 _Base;
            float _Intensity;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 info : TEXCOORD1;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.info = v.uv2;
                o.color = v.color;
                return o;
            }

            float h2(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 uv = i.uv;
                float page = floor(i.info.x + 0.5);
                float seed = i.info.y;
                float t = _Time.y + seed * 17.0;
                float3 tint = i.color.rgb;
                float ink = 0.0;
                float3 alt = tint;

                if (page < 0.5)
                {
                    // Oscilloscope over a faint grid.
                    float grid = step(frac(uv.x * 10.0), 0.04) + step(frac(uv.y * 6.0), 0.05);
                    float y = 0.5 + 0.2 * sin(uv.x * 13.0 + t * 2.2) + 0.08 * sin(uv.x * 37.0 - t * 3.1 + seed * 5.0);
                    float trace = 1.0 - smoothstep(0.0, 0.025, abs(uv.y - y));
                    ink = grid * 0.16 + trace;
                }
                else if (page < 1.5)
                {
                    // Bar graph easing between seeded readings.
                    float b = floor(uv.x * 12.0);
                    float fx = frac(uv.x * 12.0);
                    float k = floor(t * 0.6);
                    float hgt = lerp(h2(float2(b, k + seed)), h2(float2(b, k + 1.0 + seed)), smoothstep(0.0, 1.0, frac(t * 0.6))) * 0.78 + 0.08;
                    ink = step(0.18, fx) * step(fx, 0.82) * step(uv.y, hgt) * (0.45 + 0.55 * uv.y / hgt);
                    ink += step(frac(uv.y * 8.0), 0.04) * 0.12;
                }
                else if (page < 2.5)
                {
                    // Scrolling log: rows of "words" of seeded lengths (no real text, nothing to translate).
                    float yy = uv.y * 13.0 + t * 0.55;
                    float r = floor(yy);
                    float fy = frac(yy);
                    float len = 0.22 + 0.68 * h2(float2(r, seed));
                    float x = uv.x - 0.06 - step(0.7, h2(float2(r, seed + 3.0))) * 0.08;
                    float word = step(0.22, frac(x * 16.0 + h2(float2(r, 9.0)) * 3.0));
                    ink = step(0.0, x) * step(x, len) * step(0.3, fy) * step(fy, 0.72) * (0.35 + 0.65 * word);
                    alt = lerp(tint, float3(1.0, 0.72, 0.35), step(0.88, h2(float2(r, seed + 7.0))));
                }
                else if (page < 3.5)
                {
                    // Radar: range rings, a sweeping wedge, blips lit as the beam passes.
                    float2 c = uv - 0.5;
                    float d = length(c) * 2.0;
                    float inside = step(d, 1.0);
                    float rings = step(abs(frac(d * 3.0 + 0.5) - 0.5), 0.035) * inside;
                    float sweep = frac(atan2(c.y, c.x) / 6.28318 - t * 0.25);
                    float wedge = pow(1.0 - sweep, 6.0) * inside;
                    float2 g = floor(uv * 9.0);
                    float blip = step(0.92, h2(g + seed)) * (1.0 - smoothstep(0.08, 0.16, length(frac(uv * 9.0) - 0.5))) * inside;
                    ink = rings * 0.35 + wedge * 0.6 + blip * (0.3 + 0.7 * pow(1.0 - sweep, 2.0));
                }
                else
                {
                    // Indicator LEDs, each blinking at its own rate; a few amber, a few green.
                    float2 n = float2(6.0, 14.0);
                    float2 g = floor(uv * n);
                    float2 f = frac(uv * n) - 0.5;
                    float rate = 0.6 + h2(g + 1.3) * 3.0;
                    float on = step(0.42, h2(g + floor(t * rate) + seed));
                    ink = (1.0 - smoothstep(0.16, 0.3, length(f))) * (0.1 + 0.9 * on);
                    float kind = h2(g * 1.7 + seed);
                    alt = kind > 0.86 ? float3(1.0, 0.62, 0.22) : (kind > 0.74 ? float3(0.45, 1.0, 0.55) : tint);
                }

                // Glass: a thin lit border, scanlines, a soft vignette.
                float2 e = min(uv, 1.0 - uv);
                float edge = min(e.x, e.y);
                float border = step(edge, 0.018) * step(page, 3.5);
                float scan = 0.86 + 0.14 * sin(uv.y * 360.0);
                float vig = 0.65 + 0.35 * smoothstep(0.0, 0.2, edge);
                float3 col = _Base.rgb + alt * ink * _Intensity * scan * vig + tint * border * 0.55;
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}
