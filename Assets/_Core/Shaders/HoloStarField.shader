Shader "SU/HoloStarField"
{
    // Galaxy overview on the holo table: every system is one quad of a single mesh (one draw call), expanded
    // here into a camera-facing billboard around its centre (all four vertices carry the centre; uv = corner,
    // uv2.x = size in object metres) so the stars read as glowing points floating in the volume.
    // Glyph is procedural (hot pin-point core, tight glow, four fine diffraction spikes, a gentle twinkle —
    // a star, not a bubble; uv2.y = twinkle phase, +2 = "you are here" ring), tinted per vertex;
    // quads fade out at the disc rim (object-space radius) so pans never spill past the table.
    Properties
    {
        _DiscRadius ("Disc Radius (m)", Float) = 0.95
        _RimFade ("Rim Fade (m)", Float) = 0.08
        _Intensity ("Intensity", Float) = 1.6
    }
    SubShader
    {
        Tags { "Queue"="Transparent+8" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha One
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

            float _DiscRadius;
            float _RimFade;
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
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float mark : TEXCOORD1;
                float ringOnly : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float scale = length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20));
                float3 view = UnityObjectToViewPos(v.vertex.xyz);
                view.xy += (v.uv * 2.0 - 1.0) * (v.uv2.x * 0.5 * scale);
                o.vertex = mul(UNITY_MATRIX_P, float4(view, 1.0));
                o.uv = v.uv * 2.0 - 1.0;
                float r = length(v.vertex.xz);
                float rim = saturate((_DiscRadius - r) / max(1e-4, _RimFade));
                o.color = v.color;
                float phase = frac(v.uv2.y);
                o.ringOnly = step(3.5, v.uv2.y);
                o.mark = step(1.5, v.uv2.y) * (1.0 - o.ringOnly);
                // Slow twinkle, a different phase per star.
                o.color.a *= rim * (0.82 + 0.18 * sin(_Time.y * (1.3 + phase * 1.7) + phase * 40.0));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float d = length(i.uv);
                // Owner ring only (a second quad under a held star): a thin circle in the empire's colour.
                if (i.ringOnly > 0.5)
                {
                    float band = saturate(1.0 - abs(d - 0.78) * 16.0);
                    return float4(i.color.rgb * _Intensity, band * 0.75 * i.color.a);
                }
                float core = saturate(1.0 - d * 4.0);
                core *= core * core;
                // A coloured glow around a small hot core: the star keeps its hue instead of burning white.
                float glow = saturate(1.0 - d);
                glow = glow * glow * glow * 0.9;
                float2 a = abs(i.uv);
                float spikes = (saturate(1.0 - a.x * 22.0) * pow(saturate(1.0 - a.y), 3.0)
                    + saturate(1.0 - a.y * 22.0) * pow(saturate(1.0 - a.x), 3.0)) * 0.35;
                float ring = saturate(1.0 - abs(d - 0.8) * 14.0) * 0.55 * i.mark;
                float cover = saturate(core + glow + spikes + ring) * i.color.a;
                float3 col = lerp(i.color.rgb, 1.0.xxx, core * 0.35) * _Intensity;
                return float4(col, cover);
            }
            ENDCG
        }
    }
    FallBack Off
}
