#ifndef SPF_BAT_COMMON_INCLUDED
#define SPF_BAT_COMMON_INCLUDED
// Shared arithmetic for vertex BAT, compute palette generation and computed-palette skinning.
struct BatInstance { float4 placement; float4 frames; float4 tint; float4 ik; };
struct BatRows { float4 row0; float4 row1; };
float4 _IkShape; // static shoulder xy, upper/lower lengths
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
float3 BatBlendPlace(float2 bindPoint, float4 skin, BatRows p0, BatRows p1, BatInstance instance)
{
    float2 posed = skin.z * BatTransform(p0,bindPoint) + skin.w * BatTransform(p1,bindPoint);
    posed.x *= instance.placement.w;
    return float3(instance.placement.xy + posed * instance.placement.z, instance.frames.w);
}
#endif
