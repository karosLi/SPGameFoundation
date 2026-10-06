#ifndef SPF_BAT_SKINNING_INCLUDED
#define SPF_BAT_SKINNING_INCLUDED
// Shared vertex-stage BAT skinning and bounded IK for production and numeric probes.
struct BatInstance { float4 placement; float4 frames; float4 tint; float4 ik; };
StructuredBuffer<BatInstance> _BatInstances;
Texture2D<float4> _BoneTexture;
// Bounded demo only: root 0 is static; upper=1, lower=2; zero bind rotations;
// lower local bind offset = (length1,0); no descendants of the lower bone.
float4 _IkShape; // shoulder x/y, length1/length2 (> 1e-4)

struct BatRows { float4 row0; float4 row1; };
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
BatRows BatRigidPalette(float2 posedOrigin, float angle, float2 bindOrigin)
{
    float s, c; sincos(angle, s, c);
    BatRows p;
    p.row0 = float4(c, -s, posedOrigin.x - c * bindOrigin.x + s * bindOrigin.y, 0);
    p.row1 = float4(s, c, posedOrigin.y - s * bindOrigin.x - c * bindOrigin.y, 0);
    return p;
}
void BatSolveIk(float4 ik, out BatRows upper, out BatRows lower)
{
    float2 shoulder = _IkShape.xy;
    float l1 = _IkShape.z, l2 = _IkShape.w;
    float2 d = ik.xy - shoulder;
    float distance = clamp(length(d), abs(l1-l2) + 1e-4, l1+l2-1e-4);
    float cosInner = clamp((l1*l1 + l2*l2 - distance*distance) / (2*l1*l2), -1.0, 1.0);
    float cosA = clamp((l1*l1 + distance*distance - l2*l2) / (2*l1*distance), -1.0, 1.0);
    // Define atan2(0,0) explicitly so CPU and GPU agree on this degenerate target.
    float aim = dot(d,d) == 0 ? 0 : atan2(d.y,d.x);
    float upperAngle = aim + ik.w * acos(cosA);
    float lowerAngle = upperAngle - ik.w * (3.14159265358979323846 - acos(cosInner));
    float su, cu; sincos(upperAngle, su, cu);
    float2 elbow = shoulder + l1 * float2(cu, su);
    upper = BatRigidPalette(shoulder, upperAngle, shoulder);
    lower = BatRigidPalette(elbow, lowerAngle, shoulder + float2(l1,0));
}
float2 BatTransform(BatRows rows, float2 bindPoint)
{
    float3 p = float3(bindPoint,1);
    return float2(dot(rows.row0.xyz,p), dot(rows.row1.xyz,p));
}
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
    float2 posed = skin.z * BatTransform(p0,bindPoint) + skin.w * BatTransform(p1,bindPoint);
    posed.x *= instance.placement.w;
    return float3(instance.placement.xy + posed * instance.placement.z, instance.frames.w);
}
#endif
