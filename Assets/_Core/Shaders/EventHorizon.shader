Shader "SU/EventHorizon"
{
    // The standing wormhole surface inside the gate ring. The disc mesh is radial rings in its local XY
    // plane (radius 1, facing +Z); uv = local xy in -1..1. Vertex: a forward surge on opening (_Surge, a
    // gaussian bulge thrown toward the room then pulled back), an idle breathing wobble, and up to four
    // ripples (xy centre, start time, strength) where something crosses. Fragment: layered interference
    // bands drifting inward, a hot rim, depth darkening toward the centre, _Alarm tints it red (incoming).
    Properties
    {
        _Core ("Core", Color) = (0.28, 0.42, 1.0, 1)
        _Glow ("Glow", Color) = (0.55, 0.9, 1.0, 1)
        _Alarm ("Alarm (0..1)", Float) = 0
        _Open ("Open (0..1)", Float) = 1
        _Surge ("Surge (0..1)", Float) = 0
        _SurgeDepth ("Surge Depth", Float) = 2.4
        _Intensity ("Intensity", Float) = 1.6
        _Ripple0 ("Ripple 0", Vector) = (0, 0, -100, 0)
        _Ripple1 ("Ripple 1", Vector) = (0, 0, -100, 0)
        _Ripple2 ("Ripple 2", Vector) = (0, 0, -100, 0)
        _Ripple3 ("Ripple 3", Vector) = (0, 0, -100, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
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

            float4 _Core;
            float4 _Glow;
            float _Alarm;
            float _Open;
            float _Surge;
            float _SurgeDepth;
            float _Intensity;
            float4 _Ripple0;
            float4 _Ripple1;
            float4 _Ripple2;
            float4 _Ripple3;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 p : TEXCOORD0;
                float lift : TEXCOORD1;
                float3 viewN : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Ripple(float2 p, float4 r, float t)
            {
                float age = t - r.z;
                if (age < 0.0 || age > 3.0) return 0.0;
                float d = distance(p, r.xy);
                return sin(d * 22.0 - age * 11.0) * exp(-age * 1.4) * exp(-d * 2.6) * r.w;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float2 p = v.uv;
                float r = length(p);
                float t = _Time.y;
                float wobble = sin(r * 11.0 - t * 1.7) * 0.012 + sin(atan2(p.y, p.x) * 3.0 + t * 0.8) * 0.006 * r;
                float ripple = Ripple(p, _Ripple0, t) + Ripple(p, _Ripple1, t) + Ripple(p, _Ripple2, t) + Ripple(p, _Ripple3, t);
                float surge = _Surge * _SurgeDepth * exp(-r * r * 2.2);
                float lift = wobble + ripple * 0.07 + surge;
                float4 pos = v.vertex;
                pos.z += lift;
                o.vertex = UnityObjectToClipPos(pos);
                o.p = p;
                o.lift = ripple + surge * 0.6;
                float3 worldPos = mul(unity_ObjectToWorld, pos).xyz;
                float3 n = normalize(mul((float3x3)unity_ObjectToWorld, float3(0, 0, 1)));
                o.viewN = float3(abs(dot(n, normalize(_WorldSpaceCameraPos - worldPos))), 0, 0);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float r = length(i.p);
                float a = atan2(i.p.y, i.p.x);
                float t = _Time.y;
                if (r > _Open) discard;
                // Interference bands sinking toward the centre, sheared by the angle: liquid, not a target.
                // Angular shear fades out toward the centre (no pinch point), mixed from incommensurate
                // lobes and a drifting cartesian field so no star pattern locks in.
                float shear = smoothstep(0.0, 0.45, r);
                float warp = (sin(a * 2.0 + t * 0.61) * 0.9 + sin(a * 3.0 - t * 0.43 + r * 4.0) * 0.6
                            + sin(i.p.x * 5.3 + t * 0.9) * sin(i.p.y * 4.7 - t * 0.7) * 1.2) * shear;
                float b1 = sin(r * 24.0 + t * 2.4 + warp * 1.5);
                float b2 = sin(r * 13.0 - t * 1.2 + warp * 2.2 + 1.7);
                float b3 = sin((i.p.x * 0.8 + i.p.y) * 8.0 + t * 1.6 + warp);
                float bands = (b1 * 0.5 + b2 * 0.35 + b3 * 0.15) * 0.5 + 0.5;
                float depth = lerp(0.35, 1.0, smoothstep(0.0, 0.95, r));
                float rim = smoothstep(_Open - 0.12, _Open, r);
                float3 core = lerp(_Core.rgb, float3(1.0, 0.18, 0.12), _Alarm);
                float3 glow = lerp(_Glow.rgb, float3(1.0, 0.55, 0.35), _Alarm);
                float3 col = lerp(core * 0.55, glow, pow(bands, 3.0) * 0.8) * depth;
                col += glow * rim * 1.6;
                col += glow * saturate(abs(i.lift)) * 1.2;
                // Brighter where it faces the viewer head-on, a sheen at grazing angles.
                col *= 0.75 + 0.35 * i.viewN.x;
                float alpha = saturate(0.82 + bands * 0.12 + rim * 0.2);
                return float4(col * _Intensity, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
