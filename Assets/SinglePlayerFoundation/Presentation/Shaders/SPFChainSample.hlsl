// Trail sampling identical to SPF.L1.Body.TrailMath.SampleAtArc (C#).
#ifndef SPF_CHAIN_SAMPLE_INCLUDED
#define SPF_CHAIN_SAMPLE_INCLUDED

float2 SPFTrailPoint(StructuredBuffer<float2> trail, ChainHeader h, uint globalIndex)
{
    return trail[h.trailStart + (globalIndex & h.trailMask)];
}

// Position at arc distance s behind the (interpolated) head.
float2 SPFSampleChain(StructuredBuffer<float2> trail, ChainHeader h, float alpha, float s)
{
    float headArc = lerp(h.arcPrev, h.arcCurr, alpha);
    float2 head = lerp(h.headPrev, h.headCurr, alpha);
    float2 last = SPFTrailPoint(trail, h, h.newest);
    float a = headArc - s;
    if (a >= 0.0)
        return headArc > 1e-5 ? lerp(last, head, a / headArc) : head;
    float along = -a / h.spacing;
    uint k = (uint)along;
    uint maxK = h.count > 0 ? h.count - 1 : 0;
    if (k >= maxK)
        return SPFTrailPoint(trail, h, h.newest - maxK);
    float2 p0 = SPFTrailPoint(trail, h, h.newest - k);
    float2 p1 = SPFTrailPoint(trail, h, h.newest - k - 1);
    return lerp(p0, p1, along - k);
}

float SPFChainLength(StructuredBuffer<float2> trail, ChainHeader h, float alpha)
{
    float headArc = lerp(h.arcPrev, h.arcCurr, alpha);
    return max(headArc, 0.0) + (h.count > 0 ? (h.count - 1) : 0) * h.spacing;
}

// Tail taper: the last 25% of the body narrows to 55% of the radius.
float SPFTaper(float t)
{
    return lerp(1.0, 0.55, saturate((t - 0.75) / 0.25));
}

#endif
