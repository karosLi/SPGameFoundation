#ifndef SPF_BAT_COMPUTED_SKINNING_INCLUDED
#define SPF_BAT_COMPUTED_SKINNING_INCLUDED
#include "BatCommon.hlsl"
StructuredBuffer<BatInstance> _BatInstances;
StructuredBuffer<BatRows> _ComputedPalette;
float3 BatSkinComputed(float2 bindPoint,float4 skin,BatInstance instance,uint instanceId)
{
    uint first=instanceId*3;
    return BatBlendPlace(bindPoint,skin,_ComputedPalette[first+(uint)skin.x],_ComputedPalette[first+(uint)skin.y],instance);
}
#endif
