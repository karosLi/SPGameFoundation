// Shared data layouts and helpers for SPF instanced / chain rendering.
// Must match SPF.Presentation.InstanceData and ChainHeader (C#).
#ifndef SPF_COMMON_INCLUDED
#define SPF_COMMON_INCLUDED

struct InstanceData
{
    float2 position;
    float radius;
    float depth;
    float4 color;       // rgb + alpha (straight)
};

struct ChainHeader
{
    float2 headPrev;
    float2 headCurr;
    float arcPrev;
    float arcCurr;
    uint trailStart;
    uint trailMask;
    uint newest;
    uint count;
    float spacing;
    float nodeSpacing;
    uint nodeOffset;
    uint nodeCount;
    float radius;
    float depth;
    float4 colorA;
    float4 colorB;
    uint stripe;
    uint flags;          // bit0: translucent (equal depth), bit1: additive
    float nodeStride;
    float pad;
};

#define SPF_FLAG_TRANSLUCENT 1u
#define SPF_FLAG_ADDITIVE 2u

// Shading for a unit disc: rim darkening + a small highlight so circles read as rounded.
float4 SPFShadeDisc(float2 uv, float4 color)
{
    float r = length(uv);
    float rim = smoothstep(0.75, 1.0, r);
    float highlight = saturate(1.0 - length(uv - float2(-0.35, 0.35)) * 2.2) * 0.25;
    float3 rgb = color.rgb * (1.0 - rim * 0.35) + highlight;
    return float4(rgb, color.a);
}

#endif
