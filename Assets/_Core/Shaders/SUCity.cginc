#ifndef SU_CITY_INCLUDED
#define SU_CITY_INCLUDED

// The city's light, set by CityExterior (once per second, and on entering a city):
// _SUCitySunDir  xyz = direction toward the sun (world), w = daylight 0..1
// _SUCitySun     rgb = sunlight colour × strength
// _SUCityAmbient rgb = sky light (from above)
// _SUCityBounce  rgb = light bounced from the ground (from below)
// _SUCityFog     rgb = haze colour (the sky at the horizon), a = exp² density
// _SUCityNight   x = night 0..1, y = shield hit flash 0..1, z = time (s), w = siege 0..1
// _SUCityCentre  xyz = foot of the citadel (world), w = city radius
float4 _SUCitySunDir;
float4 _SUCitySun;
float4 _SUCityAmbient;
float4 _SUCityBounce;
float4 _SUCityFog;
float4 _SUCityNight;
float4 _SUCityCentre;

// Hemisphere sky + ground light, and the sun, on a surface.
float3 SU_CityLight(float3 n)
{
    float hemi = saturate(n.y * 0.5 + 0.5);
    float3 amb = lerp(_SUCityBounce.rgb, _SUCityAmbient.rgb, hemi);
    float ndl = saturate(dot(n, normalize(_SUCitySunDir.xyz)));
    return amb + _SUCitySun.rgb * ndl;
}

// Exp² haze toward the sky's horizon colour: far districts and the mountains melt into it.
float3 SU_CityFog(float3 col, float3 worldPos)
{
    float d = distance(_WorldSpaceCameraPos, worldPos) * _SUCityFog.a;
    float f = exp(-d * d);
    return lerp(_SUCityFog.rgb, col, f);
}

// Soft shoulder (as SU/PlanetSurface): lit stone stays under the Quest bloom threshold; only what is meant
// to glow (lit windows at night, boulevards, beacons) is pushed past it by its emission.
float3 SU_CityShoulder(float3 col)
{
    return col * 1.18 / (1.0 + col * 0.45);
}

float SU_Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

#endif
