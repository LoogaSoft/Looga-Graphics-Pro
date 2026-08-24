#ifndef LOOGA_WIND_INCLUDED
#define LOOGA_WIND_INCLUDED

float4 _LoogaWindDirectionAndSpeed; 
float4 _LoogaWindTurbulence;        

float3 ApplyProceduralWind(float3 positionOS, float3 positionWS, float flutterMask, float windInfluence)
{
    // Bounded, predictable height weighting. Vertices at or above ~5m of object-space
    // height get full sway; lower vertices get a quadratic falloff so roots stay still.
    // This avoids the unbounded quadratic-meters-squared growth that produced
    // 10m+ displacements on tall meshes.
    float heightRef = 5.0;
    float bendWeight = saturate(max(0.0, positionOS.y) / heightRef);
    bendWeight = bendWeight * bendWeight;

    float time = _Time.y * _LoogaWindDirectionAndSpeed.w;
    float phase = positionWS.x * 0.1 + positionWS.z * 0.1;
    float sway = sin(time + phase) * _LoogaWindTurbulence.x;

    float flutterPhase = positionWS.x * 2.0 + positionWS.y * 2.0 + positionWS.z * 2.0;
    float flutter = sin(_Time.y * _LoogaWindTurbulence.y + flutterPhase) * _LoogaWindTurbulence.z * flutterMask;

    // SafeNormalize tolerates a zero-vector global (returns 0) instead of producing NaN
    // and culling the whole mesh's triangles.
    float3 windDir = SafeNormalize(_LoogaWindDirectionAndSpeed.xyz);

    float3 displacement = windDir * (sway + flutter) * bendWeight * windInfluence;
    displacement.y -= (sway * sway) * 0.5 * bendWeight * windInfluence;

    return positionOS + displacement;
}

// Calculates a 0 to 1 rolling wave based on the global wind direction and speed
float CalculateWindGust(float3 positionWS)
{
    float time = _Time.y * _LoogaWindDirectionAndSpeed.w;
    float phase = positionWS.x * 0.1 + positionWS.z * 0.1;
    return (sin(time + phase) * 0.5) + 0.5;
}

#endif
