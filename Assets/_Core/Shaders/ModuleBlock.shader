Shader "SU/ModuleBlock"
{
    // Dry dock shelf block: a ship module in miniature on its cartridge plinth, baked as ONE vertex-coloured
    // mesh (rgb = albedo, a = self-lit share) so a whole shelf page is one material and one draw per block.
    // Lit like the room shells (hemisphere + the four RoomLightRig lights + a soft key), accent fresnel rim
    // for hover / selection, a holographic "ghost" look for modules the hangar does not hold, and a
    // replicator sweep (uv.x = block height 0..1) that materialises / dissolves the block bottom-up.
    // _Invert keeps what lies ABOVE the front instead: the module printer's hologram of the part still to print.
    // _Tint (material-wide) paints the body in the accent: the assembly table's miniatures.
    Properties
    {
        _Accent ("Accent (rim / hologram)", Color) = (0.4, 0.95, 0.55, 1)
        _Hover ("Hover rim", Range(0, 2)) = 0
        _Ghost ("Hologram (not in stock)", Range(0, 1)) = 0
        _Reveal ("Materialised (0..1)", Range(0, 1.1)) = 1.1
        _Invert ("Keep above the front", Range(0, 1)) = 0
        _Sky ("Ambient from above", Color) = (0.82, 0.88, 0.98, 1)
        _Ground ("Ambient from below", Color) = (0.3, 0.32, 0.37, 1)
        _LightGain ("Room light gain", Float) = 1.6
        _Tint ("Body tinted by the accent (assembly table)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _Sky;
            float4 _Ground;
            float _LightGain;
            float _Tint;
            float4 _SU_RoomLightPos[4];
            float4 _SU_RoomLightCol[4];

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Accent)
                UNITY_DEFINE_INSTANCED_PROP(float, _Hover)
                UNITY_DEFINE_INSTANCED_PROP(float, _Ghost)
                UNITY_DEFINE_INSTANCED_PROP(float, _Reveal)
                UNITY_DEFINE_INSTANCED_PROP(float, _Invert)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 objPos : TEXCOORD2;
                float height : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.objPos = v.vertex.xyz;
                o.height = v.uv.x;
                o.color = v.color;
                return o;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float4 accent = UNITY_ACCESS_INSTANCED_PROP(Props, _Accent);
                float hover = UNITY_ACCESS_INSTANCED_PROP(Props, _Hover);
                float ghost = UNITY_ACCESS_INSTANCED_PROP(Props, _Ghost);
                float reveal = UNITY_ACCESS_INSTANCED_PROP(Props, _Reveal);
                float invert = UNITY_ACCESS_INSTANCED_PROP(Props, _Invert);

                // Replicator front: everything above the sweep is not there yet; a ragged hot band rides it.
                float grain = Hash(floor(i.objPos * 320.0)) - 0.5;
                float front = i.height - reveal + grain * 0.05;
                clip(lerp(-front, front, invert));

                float3 n = normalize(i.worldNormal);
                float3 v = normalize(_WorldSpaceCameraPos - i.worldPos);
                float fres = pow(1.0 - saturate(dot(n, v)), 3.0);

                float3 light = lerp(_Ground.rgb, _Sky.rgb, n.y * 0.5 + 0.5);
                light += saturate(dot(n, normalize(float3(-0.45, 0.8, -0.4)))) * float3(0.55, 0.53, 0.5);
                [unroll]
                for (int k = 0; k < 4; k++)
                {
                    float3 d = _SU_RoomLightPos[k].xyz - i.worldPos;
                    float dd = dot(d, d);
                    float att = saturate(1.0 - dd * _SU_RoomLightPos[k].w);
                    att *= att;
                    float ndl = saturate(dot(n, d * rsqrt(max(dd, 1e-4))) * 0.75 + 0.25);
                    light += _SU_RoomLightCol[k].rgb * (att * ndl * _LightGain);
                }

                float self = i.color.a;
                float3 col = i.color.rgb * light * (1.0 - self) + i.color.rgb * self * 2.4;
                // On the assembly table each module wears its family colour, so the layout reads at a glance.
                col = lerp(col, col * (0.3 + accent.rgb * 1.1), _Tint);
                col += accent.rgb * fres * (0.18 + hover * 1.4);

                // Hologram: the part as a lattice of light in its family colour, scanlines rolling up.
                if (ghost > 0.001)
                {
                    float scan = frac(i.worldPos.y * 110.0 - _Time.y * 1.3);
                    float lines = step(0.45, scan);
                    float edge = saturate(fres * 2.2 + 0.18);
                    float3 holo = accent.rgb * (0.35 + fres * 2.4 + hover * 0.9) * (0.55 + 0.45 * lines);
                    col = lerp(col, holo, ghost);
                    // Dither the interior out so the hologram reads see-through without blending.
                    clip(lerp(1.0, edge + lines * 0.35 - 0.3, ghost));
                }

                float band = saturate(1.0 - abs(front) / 0.045) * step(reveal, 1.0);
                col += accent.rgb * band * 3.5 + band * 0.6;
                return float4(col, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
