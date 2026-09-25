Shader "SU/HoloStarField"
{
    // Galaxy overview on the holo table: every system is one flat quad of a single mesh (one draw call).
    // Glyph is procedural (hot core + soft halo + faint ring), tinted per vertex by owner stance;
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
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * 2.0 - 1.0;
                float r = length(v.vertex.xz);
                float rim = saturate((_DiscRadius - r) / max(1e-4, _RimFade));
                o.color = v.color;
                o.color.a *= rim;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float d = length(i.uv);
                float core = saturate(1.0 - d * 3.2);
                core *= core;
                float halo = saturate(1.0 - d);
                halo = halo * halo * 0.45;
                float ring = saturate(1.0 - abs(d - 0.78) * 14.0) * 0.35;
                float cover = saturate(core + halo + ring) * i.color.a;
                float3 col = lerp(i.color.rgb, 1.0.xxx, core * 0.6) * _Intensity;
                return float4(col, cover);
            }
            ENDCG
        }
    }
    FallBack Off
}
