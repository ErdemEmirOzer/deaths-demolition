#ifndef DD_WIND_INCLUDED
#define DD_WIND_INCLUDED

// WP-013 · ADR-013-1 wind contract (single global, driven by WorldWind / WorldAtmosphere).
//
//   _DD_WindVector  xy = horizontal direction (x, z), unit length
//                   z  = strength 0..1 (gusts already folded in by WorldWind)
//                   w  = wind time (seconds, own clock so pause freezes the foliage)
//   _DD_WindParams  x  = sway amplitude in metres at strength 1, sway 1
//                   y  = sway frequency (Hz)
//                   z  = flutter amplitude in metres
//                   w  = flutter frequency (Hz)
//
// Vertex colour = AP-003 `_AP003_WIND` schema: R sway (0 at the root, 1 at the tip),
// G phase (per element), B flutter (leaf edges), A stiffness (1 at the root).
// A mesh WITHOUT a colour stream reads (1,1,1,1): R·(1−A) = 0, so a bamboo fence or a
// breakable crate that shares a foliage material does not move. That product is the
// whole reason the formula is written the way it is.

float4 _DD_WindVector;
float4 _DD_WindParams;

float3 DD_WindDirection()
{
    float2 d = _DD_WindVector.xy;
    float len = max(length(d), 1e-4);
    return float3(d.x / len, 0.0, d.y / len);
}

// Returns the world-space displacement for one vertex. positionWS is the undisplaced
// world position, normalWS its normal (flutter moves along it), windColor the vertex colour.
float3 DD_WindDisplacement(float3 positionWS, float3 normalWS, float4 windColor, float materialScale)
{
    float strength = saturate(_DD_WindVector.z) * materialScale;
    float t = _DD_WindVector.w;
    float3 dir = DD_WindDirection();

    float bend = saturate(windColor.r) * saturate(1.0 - windColor.a);
    float phase = windColor.g * 6.2831853;

    // Position-dependent phase so a row of shrubs does not move in lock-step; the
    // slow term is the gust front travelling along the wind direction.
    float along = dot(positionWS.xz, dir.xz);
    float gustFront = sin(t * 0.37 + along * 0.11) * 0.5 + 0.5;
    float sway = sin(t * _DD_WindParams.y * 6.2831853 + phase + along * 0.35) * 0.5 + 0.5;

    // Lean with the wind on average (0.35), oscillate on top (0.65).
    float lean = _DD_WindParams.x * strength * bend * (0.35 + 0.65 * (0.4 * gustFront + 0.6 * sway));
    float3 displacement = dir * lean;

    // A bent stem is shorter along its axis: pull the tip down a little so long
    // leaves do not visibly stretch. Small and quadratic, invisible at rest.
    displacement.y -= lean * lean * 0.5;

    float flutter = saturate(windColor.b) * saturate(1.0 - windColor.a) * _DD_WindParams.z * strength;
    float flutterWave = sin(t * _DD_WindParams.w * 6.2831853 + phase * 3.7 + positionWS.y * 2.3 + positionWS.x * 1.7);
    displacement += normalWS * (flutter * flutterWave);

    return displacement;
}

#endif
