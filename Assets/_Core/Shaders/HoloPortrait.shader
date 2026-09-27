Shader "SU/HoloPortrait"
{
    // The correspondent's face on the main screen: a head-and-shoulders hologram drawn from distance fields,
    // no texture. _Variant = silhouette family from the empire's species (0 humanoid, 1 synthetic, 2 aquatic,
    // 3 reptilian, 4 avian, 5 insectoid, 6 plant / fungal, 7 lithoid, 8 command emblem for system mail);
    // _Seed varies proportions per commander; _Talk (0–1) animates the mouth while their words come in.
    // Additive over the dark comms panel (UGUI RawImage: vertex colour = tint).
    Properties
    {
        [PerRendererData] _MainTex ("Unused", 2D) = "white" {}
        _Variant ("Variant", Float) = 0
        _Seed ("Seed", Float) = 0
        _Talk ("Talk", Float) = 0
        _Intensity ("Intensity", Float) = 1.4
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        ZTest [unity_GUIZTestMode]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Variant;
            float _Seed;
            float _Talk;
            float _Intensity;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float h1(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

            float sdEllipse(float2 p, float2 c, float2 r)
            {
                float2 q = (p - c) / r;
                return (length(q) - 1.0) * min(r.x, r.y);
            }

            float sdBox(float2 p, float2 c, float2 b, float rad)
            {
                float2 d = abs(p - c) - b + rad;
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - rad;
            }

            float sdSeg(float2 p, float2 a, float2 b, float w)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h) - w;
            }

            float sdHex(float2 p, float2 c, float r)
            {
                const float3 k = float3(-0.866025404, 0.5, 0.577350269);
                p = abs(p - c);
                p -= 2.0 * min(dot(k.xy, p), 0.0) * k.xy;
                p -= float2(clamp(p.x, -k.z * r, k.z * r), r);
                return length(p) * sign(p.y);
            }

            float U(float a, float b) { return min(a, b); }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float t = _Time.y;
                float2 uv = i.uv;

                // A transmission glitch now and then: rows slip sideways for a few frames.
                float glitch = step(frac(t * 0.37 + _Seed * 7.0), 0.035);
                uv.x += glitch * (h1(floor(uv.y * 24.0) + floor(t * 40.0)) - 0.5) * 0.08;

                // Portrait space: centre, 1.5:1 panel, the bust rising from the bottom edge.
                float2 p = (uv - float2(0.5, 0.45)) * float2(2.25, 1.5) * 1.12;
                float v = floor(_Variant + 0.5);
                float s = 0.92 + h1(_Seed * 31.0) * 0.16;
                float eyeGap = 0.12 + h1(_Seed * 17.0) * 0.05;

                float body = sdEllipse(p, float2(0.0, -1.12), float2(0.98, 0.58));
                body = U(body, sdBox(p, float2(0.0, -0.46), float2(0.14, 0.2), 0.05));
                float head = 1.0;
                float feat = 1.0;     // eyes, visors: bright
                float mouthY = -0.2;

                if (v < 0.5)
                {
                    head = sdEllipse(p, float2(0.0, 0.05), float2(0.34, 0.45) * s);
                    feat = U(sdEllipse(p, float2(-eyeGap, 0.1), float2(0.05, 0.03)), sdEllipse(p, float2(eyeGap, 0.1), float2(0.05, 0.03)));
                }
                else if (v < 1.5)
                {
                    head = sdBox(p, float2(0.0, 0.05), float2(0.32, 0.4) * s, 0.09);
                    feat = sdBox(p, float2(0.0, 0.1), float2(0.25, 0.028), 0.02);
                    head = U(head, sdSeg(p, float2(0.18, 0.42), float2(0.3, 0.72), 0.015));
                    feat = U(feat, sdEllipse(p, float2(0.3, 0.74), float2(0.035, 0.035)));
                    mouthY = -0.16;
                }
                else if (v < 2.5)
                {
                    head = sdEllipse(p, float2(0.0, 0.05), float2(0.4, 0.38) * s);
                    head = U(head, sdEllipse(p, float2(-0.44, 0.12), float2(0.12, 0.22)));
                    head = U(head, sdEllipse(p, float2(0.44, 0.12), float2(0.12, 0.22)));
                    for (int k1 = -1; k1 <= 1; k1++)
                        head = U(head, sdSeg(p, float2(k1 * 0.12, -0.25), float2(k1 * 0.16, -0.5 - abs(k1) * 0.05 + 0.03 * sin(t * 2.0 + k1)), 0.025));
                    feat = U(sdEllipse(p, float2(-eyeGap - 0.04, 0.1), float2(0.07, 0.05)), sdEllipse(p, float2(eyeGap + 0.04, 0.1), float2(0.07, 0.05)));
                }
                else if (v < 3.5)
                {
                    head = sdEllipse(p, float2(0.0, 0.05), float2(0.31, 0.5) * s);
                    head = U(head, sdSeg(p, float2(0.0, 0.45), float2(0.0, 0.82), 0.045));
                    for (int k2 = 0; k2 < 3; k2++)
                        head = U(head, sdSeg(p, float2(0.0, 0.5 + k2 * 0.1), float2(0.12, 0.58 + k2 * 0.1), 0.02));
                    feat = U(sdEllipse(p, float2(-eyeGap, 0.14), float2(0.06, 0.018)), sdEllipse(p, float2(eyeGap, 0.14), float2(0.06, 0.018)));
                    mouthY = -0.26;
                }
                else if (v < 4.5)
                {
                    head = sdEllipse(p, float2(0.0, 0.08), float2(0.33, 0.38) * s);
                    head = U(head, sdSeg(p, float2(0.0, -0.02), float2(0.0, -0.3), 0.06));
                    for (int k3 = -1; k3 <= 1; k3++)
                        head = U(head, sdSeg(p, float2(k3 * 0.08, 0.4), float2(k3 * 0.22, 0.72 - abs(k3) * 0.08), 0.022));
                    feat = U(sdEllipse(p, float2(-eyeGap - 0.03, 0.14), float2(0.045, 0.045)), sdEllipse(p, float2(eyeGap + 0.03, 0.14), float2(0.045, 0.045)));
                    mouthY = -0.36;
                }
                else if (v < 5.5)
                {
                    head = sdEllipse(p, float2(0.0, 0.02), float2(0.37, 0.33) * s);
                    head = U(head, sdSeg(p, float2(-0.1, 0.28), float2(-0.34, 0.78 + 0.02 * sin(t * 3.0)), 0.015));
                    head = U(head, sdSeg(p, float2(0.1, 0.28), float2(0.34, 0.78 + 0.02 * sin(t * 3.0 + 1.3)), 0.015));
                    feat = U(sdEllipse(p, float2(-0.19, 0.08), float2(0.13, 0.17)), sdEllipse(p, float2(0.19, 0.08), float2(0.13, 0.17)));
                    mouthY = -0.22;
                }
                else if (v < 6.5)
                {
                    head = sdEllipse(p, float2(0.0, 0.0), float2(0.3, 0.36) * s);
                    head = U(head, sdEllipse(p, float2(0.0, 0.34), float2(0.56, 0.24)));
                    feat = U(sdEllipse(p, float2(-eyeGap, 0.02), float2(0.045, 0.045)), sdEllipse(p, float2(eyeGap, 0.02), float2(0.045, 0.045)));
                    feat = U(feat, sdEllipse(p, float2(-0.28, 0.4), float2(0.05, 0.04)));
                    feat = U(feat, sdEllipse(p, float2(0.2, 0.46), float2(0.04, 0.035)));
                    mouthY = -0.2;
                }
                else if (v < 7.5)
                {
                    head = sdHex(p, float2(0.0, 0.05), 0.4 * s);
                    head = U(head, sdHex(p, float2(0.26, 0.38), 0.12));
                    feat = sdBox(p, float2(0.0, 0.1), float2(0.22, 0.03), 0.0);
                    mouthY = -0.18;
                }

                float fill, edge, bright;
                if (v > 7.5)
                {
                    // Command emblem: nested rings turning, a star at the heart.
                    float r = length(p - float2(0.0, 0.1));
                    float rings = exp(-abs(r - 0.55) * 60.0) + exp(-abs(r - 0.38) * 70.0) * (0.6 + 0.4 * sin(atan2(p.y - 0.1, p.x) * 6.0 + t * 2.0));
                    float a = atan2(p.y - 0.1, p.x);
                    float star = r - (0.16 + 0.08 * pow(abs(cos(a * 2.5 + t * 0.4)), 8.0));
                    fill = smoothstep(0.02, -0.02, star);
                    edge = rings + exp(-abs(star) * 50.0);
                    bright = fill * 0.8;
                }
                else
                {
                    float d = U(body, head);
                    fill = smoothstep(0.015, -0.015, d);
                    edge = exp(-abs(d) * 55.0);
                    // Eyes blink every few seconds; the mouth moves while words come in.
                    float open = step(0.06, frac(t * 0.27 + _Seed * 3.0));
                    bright = smoothstep(0.012, -0.012, feat) * open;
                    float mouth = sdBox(p, float2(0.0, mouthY), float2(0.09, 0.012 + 0.03 * _Talk * abs(sin(t * 13.0 + sin(t * 7.0)))), 0.01);
                    bright += smoothstep(0.012, -0.012, mouth) * (0.35 + 0.65 * _Talk);
                    // Shading: lit from above, fading into the projector at the bottom.
                    fill *= 0.16 + 0.22 * saturate(p.y + 0.8);
                }

                float scan = 0.72 + 0.28 * sin(uv.y * 240.0 - t * 5.0);
                float flicker = 0.93 + 0.07 * sin(t * 37.0) * sin(t * 11.0);
                // The projector's cone rising from the bottom edge.
                float cone = exp(-abs(p.x) * 3.0) * saturate(1.0 - uv.y * 1.6) * 0.12;
                float lum = (fill + edge * 0.9 + bright * 1.6 + cone) * scan * flicker * _Intensity;
                float3 col = lerp(i.color.rgb, 1.0.xxx, saturate(bright * 0.6));
                return fixed4(col * lum, saturate(lum) * i.color.a);
            }
            ENDCG
        }
    }
    FallBack Off
}
