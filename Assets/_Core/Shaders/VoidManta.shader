// Void fauna: a flat manta-like body (object x = span −1..1, z = length −1..1) whose wings beat in a slow
// wave (vertex), dark hide with a fresnel rim and bioluminescent spots that pulse down the body. Opaque.
// Phase comes from the object's world position, so each creature beats on its own without a property block.
Shader "SU/VoidManta"
{
    Properties
    {
        _Hide ("Hide", Color) = (0.03, 0.06, 0.09, 1)
        _Glow ("Glow", Color) = (0.3, 0.95, 1, 1)
        _Flap ("Flap Speed", Float) = 0.9
        _Amp ("Wing Amplitude", Float) = 0.28
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float4 _Hide;
            float4 _Glow;
            float _Flap;
            float _Amp;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 obj : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                float phase : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 origin = unity_ObjectToWorld._m03_m13_m23;
                float phase = dot(origin, float3(0.071, 0.113, 0.053));
                float4 p = v.vertex;
                float t = _Time.y * _Flap + phase;
                // Wings beat (strongest at the tips, a wave running outward), the tail undulates.
                p.y += sin(t - abs(p.x) * 1.4) * _Amp * p.x * p.x;
                p.y += sin(p.z * 3.0 - t * 1.7) * 0.05 * saturate(-p.z);
                o.vertex = UnityObjectToClipPos(p);
                o.obj = v.vertex.xyz;
                o.worldPos = mul(unity_ObjectToWorld, p).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.phase = phase;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.worldNormal);
                float3 view = normalize(_WorldSpaceCameraPos - i.worldPos);
                float fres = pow(1.0 - abs(dot(n, view)), 3.0);
                float t = _Time.y + i.phase;
                // Two rows of spots along the wings, a pulse travelling nose to tail.
                float rows = smoothstep(0.55, 0.95, sin(abs(i.obj.x) * 18.0) * sin(i.obj.z * 11.0));
                float pulse = 0.35 + 0.65 * saturate(sin(i.obj.z * 4.0 + t * 2.2) * 0.5 + 0.5);
                float spine = smoothstep(0.08, 0.0, abs(i.obj.x)) * 0.6;
                float3 col = _Hide.rgb * (0.6 + 0.4 * saturate(n.y * 0.5 + 0.5)) + _Glow.rgb * (fres * 0.55 + (rows + spine) * pulse * 1.3);
                return float4(col, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}
