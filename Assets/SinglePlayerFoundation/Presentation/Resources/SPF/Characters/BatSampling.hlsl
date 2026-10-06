#ifndef SPF_BAT_SAMPLING_INCLUDED
#define SPF_BAT_SAMPLING_INCLUDED
#include "BatCommon.hlsl"
Texture2D<float4> _BoneTexture;
BatRows BatLoad(uint bone, uint frame)
{
    BatRows p;
    p.row0 = _BoneTexture.Load(int3(2 * bone, frame, 0));
    p.row1 = _BoneTexture.Load(int3(2 * bone + 1, frame, 0));
    return p;
}
BatRows BatSample(uint bone, float4 frames)
{
    BatRows a = BatLoad(bone, (uint)frames.x);
    BatRows b = BatLoad(bone, (uint)frames.y);
    BatRows p;
    p.row0 = lerp(a.row0, b.row0, frames.z);
    p.row1 = lerp(a.row1, b.row1, frames.z);
    return p;
}
#endif
