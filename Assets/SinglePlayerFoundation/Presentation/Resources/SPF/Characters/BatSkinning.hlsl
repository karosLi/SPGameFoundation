#ifndef SPF_BAT_SKINNING_INCLUDED
#define SPF_BAT_SKINNING_INCLUDED
#include "BatSampling.hlsl"
StructuredBuffer<BatInstance> _BatInstances;
float3 BatSkin(float2 bindPoint, float4 skin, BatInstance instance)
{
    uint bone0 = (uint)skin.x, bone1 = (uint)skin.y;
    BatRows p0 = BatSample(bone0, instance.frames);
    BatRows p1 = BatSample(bone1, instance.frames);
    if (instance.ik.z > 0.5)
    {
        BatRows upper, lower; BatSolveIk(instance.ik, upper, lower);
        if (bone0 == 1) p0 = upper; else if (bone0 == 2) p0 = lower;
        if (bone1 == 1) p1 = upper; else if (bone1 == 2) p1 = lower;
    }
    return BatBlendPlace(bindPoint,skin,p0,p1,instance);
}
#endif
