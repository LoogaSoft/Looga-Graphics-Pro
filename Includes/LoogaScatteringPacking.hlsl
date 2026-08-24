#ifndef LOOGA_SCATTERING_PACKING_INCLUDED
#define LOOGA_SCATTERING_PACKING_INCLUDED

half PackLoogaBacklightShape(float rimPower, float distortion)
{
    uint rim = (uint)round(saturate((rimPower - 1.0) / 15.0) * 15.0);
    uint bend = (uint)round(saturate(distortion) * 15.0);
    return (half)(rim | (bend << 4u)) / 255.0h;
}

void UnpackLoogaBacklightShape(half packedShape, out half rimPower, out half distortion)
{
    uint packed = (uint)round(saturate(packedShape) * 255.0h);
    rimPower = 1.0h + 15.0h * ((packed & 15u) / 15.0h);
    distortion = ((packed >> 4u) & 15u) / 15.0h;
}

#endif
