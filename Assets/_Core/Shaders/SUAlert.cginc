#ifndef SU_ALERT_INCLUDED
#define SU_ALERT_INCLUDED

// The ship's alert condition, set once per frame by AlertDirector and read by every room surface:
// _SU_AlertTint  rgb = alert colour × the room-wide pulse (red) / breath (yellow)
// _SU_AlertColor rgb = alert hue at full strength (the beacon sweep)
// _SU_AlertSweep xy = beacon heading (sin, cos), z = sweep strength (0 = no alert), w = reach (m)
float4 _SU_AlertTint;
float4 _SU_AlertColor;
float4 _SU_AlertSweep;

// Light the alert throws on a surface: the room-wide wash plus two opposed beacon beams turning around the
// viewer, hardest on walls that face the beam. Uniform branch: free when no alert stands.
float3 SU_AlertLight(float3 worldPos, float3 n)
{
    if (_SU_AlertSweep.z <= 0.0)
        return _SU_AlertTint.rgb;
    float3 d = worldPos - _WorldSpaceCameraPos;
    float dist = length(d);
    float near = saturate(1.0 - dist / max(_SU_AlertSweep.w, 1.0));
    float2 flat = d.xz / max(length(d.xz), 1e-3);
    float c = dot(flat, _SU_AlertSweep.xy);
    float beam = pow(saturate(c), 9.0) + pow(saturate(-c), 9.0);
    float facing = 0.45 + 0.55 * saturate(-dot(n.xz, flat));
    return (_SU_AlertTint.rgb + _SU_AlertColor.rgb * (beam * facing * _SU_AlertSweep.z)) * near;
}

// One room-scale light event (the gate's event horizon forming and standing, a blast): a point light every
// unlit room surface can catch, since the Quest profile has no additional real-time lights.
// _SU_FlashPos xyz = world position, w = 1 / range²; _SU_FlashCol rgb = colour × intensity, a > 0 when live.
float4 _SU_FlashPos;
float4 _SU_FlashCol;

float3 SU_FlashLight(float3 worldPos, float3 n)
{
    if (_SU_FlashCol.a <= 0.0)
        return 0;
    float3 d = _SU_FlashPos.xyz - worldPos;
    float dd = dot(d, d);
    float att = saturate(1.0 - dd * _SU_FlashPos.w);
    att *= att;
    float ndl = saturate(dot(n, d * rsqrt(max(dd, 1e-4))) * 0.7 + 0.3);
    return _SU_FlashCol.rgb * (att * ndl);
}

#endif
